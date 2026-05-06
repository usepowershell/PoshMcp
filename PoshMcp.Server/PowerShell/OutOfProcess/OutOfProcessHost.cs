using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PoshMcp.Server.PowerShell.OutOfProcess;

/// <summary>
/// Encapsulates a single <c>pwsh</c> subprocess and the ndjson request/response
/// protocol channel used to communicate with it. Owns the process lifecycle,
/// the stdin/stdout/stderr streams, the send-side serialization lock, the
/// pending-request correlation map, the background read loops, and the
/// graceful shutdown sequence.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="OutOfProcessHost"/> instance is single-use: call
/// <see cref="StartAsync"/> once, then any number of <see cref="SendRequestAsync"/>
/// calls, then <see cref="DisposeAsync"/>. To "restart" the subprocess, dispose
/// the current host and construct a new one.
/// </para>
/// <para>
/// This type is the seam used by higher-level executors (such as
/// <see cref="OutOfProcessCommandExecutor"/>) and by experimental host
/// strategies (e.g., runspace pool prototypes).
/// </para>
/// </remarks>
public sealed class OutOfProcessHost : IAsyncDisposable
{
    private readonly ILogger<OutOfProcessHost> _logger;
    private readonly string _pwshPath;
    private readonly string _hostScriptPath;
    private readonly TimeSpan _requestTimeout;

    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private Task? _readLoopTask;
    private Task? _stderrLoopTask;
    private bool _disposed;
    private bool _started;

    /// <summary>
    /// Creates a new <see cref="OutOfProcessHost"/>.
    /// </summary>
    /// <param name="pwshPath">Absolute path to the <c>pwsh</c> executable.</param>
    /// <param name="hostScriptPath">Absolute path to the <c>oop-host.ps1</c> script.</param>
    /// <param name="logger">Logger for diagnostics; if <see langword="null"/>, a null logger is used.</param>
    /// <param name="requestTimeout">
    /// Default timeout applied to <see cref="SendRequestAsync"/>. Defaults to 30 seconds.
    /// Individual calls may override via the <c>requestTimeoutOverride</c> parameter.
    /// </param>
    public OutOfProcessHost(
        string pwshPath,
        string hostScriptPath,
        ILogger<OutOfProcessHost>? logger = null,
        TimeSpan? requestTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pwshPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostScriptPath);

        _pwshPath = pwshPath;
        _hostScriptPath = hostScriptPath;
        _logger = logger ?? NullLogger<OutOfProcessHost>.Instance;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// True once <see cref="StartAsync"/> has launched the subprocess and the
    /// initial <c>ping</c> succeeded.
    /// </summary>
    public bool IsRunning
    {
        get
        {
            if (_disposed || !_started || _process is null)
            {
                return false;
            }

            try
            {
                return !_process.HasExited;
            }
            catch (InvalidOperationException)
            {
                // Process handle has been disposed.
                return false;
            }
        }
    }

    /// <summary>
    /// The OS process id of the subprocess once started; <see langword="null"/> otherwise.
    /// </summary>
    public int? ProcessId
    {
        get
        {
            if (_process is null) return null;
            try
            {
                return _process.Id;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Launches the <c>pwsh</c> subprocess, starts the background stdout/stderr
    /// read loops, and verifies the host with a single <c>ping</c> request.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the subprocess fails to start or fails the initial ping.
    /// </exception>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("OutOfProcessHost has already been started.");
        }

        _logger.LogInformation(
            "Starting OOP subprocess: {PwshPath} -NoProfile -NonInteractive -File {ScriptPath}",
            _pwshPath, _hostScriptPath);

        var psi = new ProcessStartInfo
        {
            FileName = _pwshPath,
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{_hostScriptPath}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Exited += OnProcessExited;

        if (!_process.Start())
        {
            throw new InvalidOperationException("Failed to start pwsh subprocess.");
        }

        _stdin = _process.StandardInput;
        _stdin.AutoFlush = true;
        _stdout = _process.StandardOutput;

        _readLoopTask = Task.Run(ReadLoopAsync, CancellationToken.None);
        _stderrLoopTask = Task.Run(StderrLoopAsync, CancellationToken.None);

        _started = true;

        try
        {
            var result = await SendRequestAsync<JsonElement>("ping", null, cancellationToken)
                .ConfigureAwait(false);

            if (result.TryGetProperty("status", out var status) && status.GetString() == "ok")
            {
                _logger.LogInformation("OOP subprocess is alive (PID {ProcessId}).", _process.Id);
            }
            else
            {
                _logger.LogWarning(
                    "OOP subprocess ping returned unexpected result: {Result}",
                    result.GetRawText());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OOP subprocess failed ping health check. Killing process.");
            await TerminateProcessAsync().ConfigureAwait(false);
            throw new InvalidOperationException("OOP subprocess failed ping health check.", ex);
        }
    }

    /// <summary>
    /// Sends a JSON-RPC-style request to the subprocess and awaits the matching response.
    /// </summary>
    /// <typeparam name="T">
    /// Expected response type. Use <see cref="JsonElement"/> to receive the raw result element.
    /// </typeparam>
    /// <param name="method">Method name handled by the subprocess host script.</param>
    /// <param name="parameters">Method parameters; serialized as the <c>params</c> field.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <param name="requestTimeoutOverride">Optional per-call timeout override.</param>
    public async Task<T> SendRequestAsync<T>(
        string method,
        object? parameters,
        CancellationToken cancellationToken,
        TimeSpan? requestTimeoutOverride = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_process is null || _process.HasExited)
        {
            throw new InvalidOperationException("OOP subprocess is not running.");
        }

        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        _pending[id] = tcs;

        try
        {
            var request = new
            {
                id,
                method,
                @params = parameters
            };

            var json = JsonSerializer.Serialize(request);

            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _logger.LogDebug("Sending request {Id} method={Method}", id, method);
                await _stdin!.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
                await _stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var requestTimeout = requestTimeoutOverride ?? _requestTimeout;
            timeoutCts.CancelAfter(requestTimeout);

            var registration = timeoutCts.Token.Register(() =>
            {
                tcs.TrySetException(new TimeoutException(
                    $"Request {id} (method={method}) timed out after {requestTimeout.TotalSeconds}s."));
            });

            try
            {
                var result = await tcs.Task.ConfigureAwait(false);

                if (typeof(T) == typeof(JsonElement))
                {
                    return (T)(object)result;
                }

                return JsonSerializer.Deserialize<T>(result.GetRawText())
                    ?? throw new InvalidOperationException($"Failed to deserialize response for method '{method}'.");
            }
            finally
            {
                await registration.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogDebug("Disposing OOP subprocess host.");

        if (_process is not null && !_process.HasExited)
        {
            try
            {
                using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await SendRequestAsync<JsonElement>("shutdown", null, shutdownCts.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Shutdown request to OOP subprocess failed or timed out.");
            }

            await WaitForExitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            if (!_process.HasExited)
            {
                _logger.LogWarning(
                    "OOP subprocess did not exit gracefully. Killing PID {ProcessId}.",
                    _process.Id);
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Process already exited between check and kill.
                }
            }
        }

        foreach (var kvp in _pending)
        {
            kvp.Value.TrySetCanceled();
        }
        _pending.Clear();

        if (_stdin is not null)
        {
            try { await _stdin.DisposeAsync().ConfigureAwait(false); }
            catch { /* best effort */ }
        }
        _stdout?.Dispose();
        _process?.Dispose();

        _sendLock.Dispose();

        try
        {
            if (_readLoopTask is not null)
                await _readLoopTask.ConfigureAwait(false);
        }
        catch { /* reader loop exits on stream close */ }

        try
        {
            if (_stderrLoopTask is not null)
                await _stderrLoopTask.ConfigureAwait(false);
        }
        catch { /* stderr loop exits on stream close */ }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (_stdout is not null)
            {
                var line = await _stdout.ReadLineAsync().ConfigureAwait(false);
                if (line is null) break; // EOF
                if (string.IsNullOrWhiteSpace(line)) continue;

                _logger.LogDebug("OOP stdout: {Line}", line);

                if (IsNonJsonPowerShellStreamLine(line))
                {
                    _logger.LogDebug("OOP subprocess non-JSON stream output (suppressed): {Line}", line);
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("id", out var idProp))
                    {
                        _logger.LogWarning("OOP response missing 'id' field: {Line}", line);
                        continue;
                    }

                    var id = idProp.GetString();
                    if (id is null)
                    {
                        _logger.LogWarning("OOP response has null 'id': {Line}", line);
                        continue;
                    }

                    if (!_pending.TryGetValue(id, out var tcs))
                    {
                        _logger.LogWarning("OOP response for unknown request id '{Id}': {Line}", id, line);
                        continue;
                    }

                    if (root.TryGetProperty("error", out var errorProp))
                    {
                        var msg = errorProp.TryGetProperty("message", out var msgProp)
                            ? msgProp.GetString() ?? "Unknown error"
                            : "Unknown error";
                        tcs.TrySetException(new InvalidOperationException($"OOP error: {msg}"));
                    }
                    else if (root.TryGetProperty("result", out var resultProp))
                    {
                        tcs.TrySetResult(resultProp.Clone());
                    }
                    else
                    {
                        _logger.LogWarning("OOP response has neither 'result' nor 'error': {Line}", line);
                        tcs.TrySetException(new InvalidOperationException(
                            "OOP response has neither 'result' nor 'error'."));
                    }
                }
                catch (JsonException)
                {
                    _logger.LogDebug("OOP stdout: skipping non-JSON line: {Line}", line);
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OOP stdout read loop terminated unexpectedly.");
        }

        _logger.LogDebug("OOP stdout read loop exited.");
    }

    private async Task StderrLoopAsync()
    {
        try
        {
            var stderr = _process?.StandardError;
            if (stderr is null) return;

            while (true)
            {
                var line = await stderr.ReadLineAsync().ConfigureAwait(false);
                if (line is null) break; // EOF
                _logger.LogDebug("OOP stderr: {Line}", line);
            }
        }
        catch (ObjectDisposedException)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OOP stderr read loop terminated unexpectedly.");
        }

        _logger.LogDebug("OOP stderr read loop exited.");
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="line"/> looks like output from a
    /// PowerShell informational stream (WARNING:, VERBOSE:, DEBUG:, INFORMATION:, ERROR:)
    /// rather than a JSON object. Some modules write directly to these streams — or to
    /// <see cref="Console.Out"/> — which can corrupt the ndjson protocol channel.
    /// </summary>
    internal static bool IsNonJsonPowerShellStreamLine(string line)
    {
        var trimmed = line.AsSpan().TrimStart();
        if (trimmed.IsEmpty) return false;
        if (trimmed[0] == '{' || trimmed[0] == '[' || trimmed[0] == '"') return false;

        return line.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("VERBOSE:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("DEBUG:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("INFORMATION:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_disposed) return;

        var exitCode = _process?.ExitCode ?? -1;
        _logger.LogWarning("OOP subprocess exited unexpectedly with code {ExitCode}.", exitCode);

        foreach (var kvp in _pending)
        {
            kvp.Value.TrySetException(new InvalidOperationException(
                $"OOP subprocess exited unexpectedly with code {exitCode}."));
        }
    }

    private async Task WaitForExitAsync(TimeSpan timeout)
    {
        if (_process is null || _process.HasExited) return;

        try
        {
            using var cts = new CancellationTokenSource(timeout);
            await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Timed out waiting for exit.
        }
    }

    private async Task TerminateProcessAsync()
    {
        if (_process is null || _process.HasExited) return;

        try
        {
            _process.Kill(entireProcessTree: true);
            await WaitForExitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }
}
