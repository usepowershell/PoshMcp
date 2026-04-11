using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell;

namespace PoshMcp.Server.PowerShell.OutOfProcess;

/// <summary>
/// Manages a persistent pwsh subprocess and communicates with it via
/// stdin/stdout ndjson to discover and invoke PowerShell commands.
/// </summary>
public class OutOfProcessCommandExecutor : ICommandExecutor
{
    private readonly ILogger<OutOfProcessCommandExecutor> _logger;
    private readonly TimeSpan _requestTimeout;

    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private Task? _readLoopTask;
    private Task? _stderrLoopTask;
    private bool _disposed;
    private IReadOnlyList<RemoteToolSchema>? _cachedSchemas;

    /// <summary>
    /// Creates a new OutOfProcessCommandExecutor.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="requestTimeout">
    /// Timeout for individual requests to the subprocess. Defaults to 30 seconds.
    /// </param>
    public OutOfProcessCommandExecutor(
        ILogger<OutOfProcessCommandExecutor> logger,
        TimeSpan? requestTimeout = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var scriptPath = ResolveHostScriptPath();
        var pwshPath = ResolvePwshPath();

        _logger.LogInformation("Starting OOP subprocess: {PwshPath} -NoProfile -NonInteractive -File {ScriptPath}",
            pwshPath, scriptPath);

        var psi = new ProcessStartInfo
        {
            FileName = pwshPath,
            Arguments = $"-NoProfile -NonInteractive -File \"{scriptPath}\"",
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

        // Start background reader tasks
        _readLoopTask = Task.Run(() => ReadLoopAsync(), CancellationToken.None);
        _stderrLoopTask = Task.Run(() => StderrLoopAsync(), CancellationToken.None);

        // Send ping to verify the subprocess is alive
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
                _logger.LogWarning("OOP subprocess ping returned unexpected result: {Result}",
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<RemoteToolSchema>> DiscoverCommandsAsync(
        PowerShellConfiguration config,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_cachedSchemas is not null)
        {
            _logger.LogDebug("Returning cached schemas ({Count} commands).", _cachedSchemas.Count);
            return _cachedSchemas;
        }

        var discoverParams = new
        {
            modules = config.Modules,
            functionNames = config.FunctionNames,
            includePatterns = config.IncludePatterns,
            excludePatterns = config.ExcludePatterns
        };

        _logger.LogInformation("Discovering commands via OOP subprocess.");

        var result = await SendRequestAsync<JsonElement>("discover", discoverParams, cancellationToken)
            .ConfigureAwait(false);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        List<RemoteToolSchema> schemas;
        if (result.TryGetProperty("commands", out var commandsElement))
        {
            schemas = JsonSerializer.Deserialize<List<RemoteToolSchema>>(commandsElement.GetRawText(), options)
                ?? new List<RemoteToolSchema>();
        }
        else
        {
            _logger.LogWarning("Discover response missing 'commands' property. Raw: {Raw}", result.GetRawText());
            schemas = new List<RemoteToolSchema>();
        }

        _logger.LogInformation("Discovered {Count} commands via OOP subprocess.", schemas.Count);
        _cachedSchemas = schemas;
        return _cachedSchemas;
    }

    /// <inheritdoc />
    public async Task<string> InvokeAsync(
        string commandName,
        IDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var invokeParams = new { command = commandName, parameters };

        _logger.LogInformation("Invoking command '{CommandName}' via OOP subprocess.", commandName);

        var result = await SendRequestAsync<JsonElement>("invoke", invokeParams, cancellationToken)
            .ConfigureAwait(false);

        var output = string.Empty;
        if (result.TryGetProperty("output", out var outputElement))
        {
            output = outputElement.GetString() ?? string.Empty;
        }

        if (result.TryGetProperty("hadErrors", out var hadErrorsElement) && hadErrorsElement.GetBoolean())
        {
            _logger.LogWarning("Command '{CommandName}' reported errors. Output: {Output}", commandName, output);
        }

        return output;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogDebug("Disposing OOP subprocess executor.");

        // Send shutdown if process is still running
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

            // Wait up to 5 seconds for graceful exit
            await WaitForExitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            // Kill if still running
            if (!_process.HasExited)
            {
                _logger.LogWarning("OOP subprocess did not exit gracefully. Killing PID {ProcessId}.", _process.Id);
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Process already exited between check and kill
                }
            }
        }

        // Complete all pending requests with cancellation
        foreach (var kvp in _pending)
        {
            kvp.Value.TrySetCanceled();
        }
        _pending.Clear();

        // Dispose streams and process
        if (_stdin is not null)
        {
            try { await _stdin.DisposeAsync().ConfigureAwait(false); }
            catch { /* best effort */ }
        }
        _stdout?.Dispose();
        _process?.Dispose();

        _sendLock.Dispose();

        // Wait for background tasks to finish
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

    /// <summary>
    /// Sends a JSON-RPC-style request to the subprocess and awaits the response.
    /// </summary>
    internal async Task<T> SendRequestAsync<T>(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
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

            // Await with timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_requestTimeout);

            var registration = timeoutCts.Token.Register(() =>
            {
                tcs.TrySetException(new TimeoutException(
                    $"Request {id} (method={method}) timed out after {_requestTimeout.TotalSeconds}s."));
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

    /// <summary>
    /// Background task that reads ndjson responses from stdout and completes pending requests.
    /// </summary>
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
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse OOP stdout JSON: {Line}", line);
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OOP stdout read loop terminated unexpectedly.");
        }

        _logger.LogDebug("OOP stdout read loop exited.");
    }

    /// <summary>
    /// Background task that reads stderr and logs diagnostic output.
    /// </summary>
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
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OOP stderr read loop terminated unexpectedly.");
        }

        _logger.LogDebug("OOP stderr read loop exited.");
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_disposed) return;

        var exitCode = _process?.ExitCode ?? -1;
        _logger.LogWarning("OOP subprocess exited unexpectedly with code {ExitCode}.", exitCode);

        // Fail all pending requests
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
            // Timed out waiting for exit
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
            // Already exited
        }
    }

    /// <summary>
    /// Resolves the path to the oop-host.ps1 script.
    /// </summary>
    internal static string ResolveHostScriptPath()
    {
        // Primary: relative to the app base directory (build output)
        var basePath = Path.Combine(AppContext.BaseDirectory, "PowerShell", "OutOfProcess", "oop-host.ps1");
        if (File.Exists(basePath))
        {
            return basePath;
        }

        // Fallback: AppDomain base
        var domainPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PowerShell", "OutOfProcess", "oop-host.ps1");
        if (File.Exists(domainPath))
        {
            return domainPath;
        }

        throw new FileNotFoundException(
            "Could not locate oop-host.ps1. Searched:\n" +
            $"  {basePath}\n" +
            $"  {domainPath}");
    }

    /// <summary>
    /// Resolves the path to the pwsh executable.
    /// </summary>
    internal static string ResolvePwshPath()
    {
        // Check if pwsh is on PATH by trying to find it
        var pwshName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";

        // Check PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathDirs = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir, pwshName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Common install locations
        string[] commonPaths = OperatingSystem.IsWindows()
            ? new[]
            {
                @"C:\Program Files\PowerShell\7\pwsh.exe",
                @"C:\Program Files (x86)\PowerShell\7\pwsh.exe",
            }
            : new[]
            {
                "/usr/bin/pwsh",
                "/usr/local/bin/pwsh",
                "/opt/microsoft/powershell/7/pwsh",
                "/snap/bin/pwsh",
            };

        foreach (var candidate in commonPaths)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"Could not find '{pwshName}' on PATH or in common install locations.");
    }
}
