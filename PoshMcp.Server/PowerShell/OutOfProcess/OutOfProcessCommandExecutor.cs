using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PoshMcp.Server.PowerShell;

namespace PoshMcp.Server.PowerShell.OutOfProcess;

/// <summary>
/// Manages a persistent pwsh subprocess (via <see cref="OutOfProcessHost"/>) and
/// implements the <see cref="ICommandExecutor"/> contract used by the MCP server
/// to discover and invoke PowerShell commands out of process.
/// </summary>
/// <remarks>
/// Per-process state (the <see cref="System.Diagnostics.Process"/>, stdin/stdout
/// streams, the request correlation map, the read loops, and the shutdown
/// sequence) lives on <see cref="OutOfProcessHost"/>. This executor owns only
/// the higher-level <c>setup</c>/<c>discover</c>/<c>invoke</c> protocol calls
/// and the configuration for resolving <c>pwsh</c> and the host script.
/// </remarks>
public class OutOfProcessCommandExecutor : ICommandExecutor
{
    private readonly ILogger<OutOfProcessCommandExecutor> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeSpan _requestTimeout;
    private readonly SubprocessHostMode _hostMode;
    private readonly int _poolSize;

    private OutOfProcessHost? _host;
    private bool _disposed;
    private IReadOnlyList<RemoteToolSchema>? _cachedSchemas;
    private RemoteModuleImportsPayload? _cachedModuleImports;

    /// <inheritdoc />
    public RemoteModuleImportsPayload? LastModuleImports => _cachedModuleImports;

    // Cached setup parameters captured on first SetupAsync call so the
    // executor can replay environment configuration after an automatic
    // restart (see EnsureHostAliveAsync). Null until SetupAsync runs.
    private EnvironmentConfiguration? _lastSetupConfig;
    private string? _lastSetupConfigFilePath;
    private TimeSpan? _lastSetupTimeout;
    private string[]? _lastDiscoveryModules;
    private readonly SemaphoreSlim _restartLock = new(1, 1);

    /// <summary>
    /// Creates a new <see cref="OutOfProcessCommandExecutor"/>.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="requestTimeout">
    /// Timeout for individual requests to the subprocess. Defaults to 30 seconds.
    /// </param>
    /// <param name="hostMode">Selects the host script (Single or Pool). Defaults to Single.</param>
    /// <param name="runspacePoolSize">Pool size when <paramref name="hostMode"/> is Pool. 0 lets the host pick a default.</param>
    public OutOfProcessCommandExecutor(
        ILogger<OutOfProcessCommandExecutor> logger,
        TimeSpan? requestTimeout = null,
        SubprocessHostMode hostMode = SubprocessHostMode.Single,
        int runspacePoolSize = 0)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = NullLoggerFactory.Instance;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        _hostMode = hostMode;
        _poolSize = runspacePoolSize;
    }

    /// <summary>
    /// Creates a new <see cref="OutOfProcessCommandExecutor"/> with a logger
    /// factory that is also used to obtain a logger for the underlying
    /// <see cref="OutOfProcessHost"/>.
    /// </summary>
    public OutOfProcessCommandExecutor(
        ILoggerFactory loggerFactory,
        TimeSpan? requestTimeout = null,
        SubprocessHostMode hostMode = SubprocessHostMode.Single,
        int runspacePoolSize = 0)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<OutOfProcessCommandExecutor>();
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        _hostMode = hostMode;
        _poolSize = runspacePoolSize;
    }

    /// <summary>
    /// The host mode this executor was constructed with.
    /// </summary>
    public SubprocessHostMode HostMode => _hostMode;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_host is not null)
        {
            throw new InvalidOperationException("Executor has already been started.");
        }

        var scriptPath = await ResolveHostScriptPathAsync().ConfigureAwait(false);
        var pwshPath = ResolvePwshPath();

        var hostLogger = _loggerFactory.CreateLogger<OutOfProcessHost>();
        _host = new OutOfProcessHost(pwshPath, scriptPath, hostLogger, _requestTimeout);

        try
        {
            await _host.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await _host.DisposeAsync().ConfigureAwait(false);
            _host = null;
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RemoteToolSchema>> DiscoverCommandsAsync(
        PowerShellConfiguration config,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var host = RequireHost();

        if (_cachedSchemas is not null)
        {
            _logger.LogDebug("Returning cached schemas ({Count} commands).", _cachedSchemas.Count);
            return _cachedSchemas;
        }

        var discoverParams = new
        {
            modules = config.Modules,
            functionNames = config.GetEffectiveCommandNames(),
            includePatterns = config.IncludePatterns,
            excludePatterns = config.ExcludePatterns
        };

        _logger.LogInformation("Discovering commands via OOP subprocess.");

        var result = await host.SendRequestAsync<JsonElement>("discover", discoverParams, cancellationToken)
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

        // Spec 011 FR-263-2 / FR-263-10: optional moduleImports payload from
        // the OOP host. Older hosts omit this entirely; missing is fine and
        // signals to consumers (DoctorService) to fall back to the in-process
        // probe path with a one-time warning.
        if (result.TryGetProperty("moduleImports", out var moduleImportsElement)
            && moduleImportsElement.ValueKind != JsonValueKind.Null)
        {
            try
            {
                _cachedModuleImports = JsonSerializer.Deserialize<RemoteModuleImportsPayload>(
                    moduleImportsElement.GetRawText(), options);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize moduleImports payload; treating as absent.");
                _cachedModuleImports = null;
            }
        }

        _logger.LogInformation("Discovered {Count} commands via OOP subprocess.", schemas.Count);
        _cachedSchemas = schemas;
        return _cachedSchemas;
    }

    /// <inheritdoc />
    public async Task SetupAsync(
        EnvironmentConfiguration config,
        string? configFilePath = null,
        TimeSpan? setupRequestTimeout = null,
        IEnumerable<string>? discoveryModules = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var host = RequireHost();

        // Cache so we can replay the environment after an automatic restart.
        _lastSetupConfig = config;
        _lastSetupConfigFilePath = configFilePath;
        _lastSetupTimeout = setupRequestTimeout;
        _lastDiscoveryModules = discoveryModules?.ToArray();

        await SendSetupAsync(host, config, configFilePath, setupRequestTimeout, discoveryModules, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendSetupAsync(
        OutOfProcessHost host,
        EnvironmentConfiguration config,
        string? configFilePath,
        TimeSpan? setupRequestTimeout,
        IEnumerable<string>? discoveryModules,
        CancellationToken cancellationToken)
    {
        var baseDir = !string.IsNullOrEmpty(configFilePath)
            ? Path.GetDirectoryName(Path.GetFullPath(configFilePath))!
            : Directory.GetCurrentDirectory();

        var resolvedModulePaths = config.ModulePaths
            .Select(p => Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(baseDir, p)))
            .ToArray();

        // Merge discovery modules (config.Modules) with the explicitly configured
        // ImportModules so both are available before the startup script runs.
        var allImportModules = config.ImportModules
            .Concat(discoveryModules ?? Enumerable.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var setupParams = new
        {
            modulePaths = resolvedModulePaths,
            trustPSGallery = config.TrustPSGallery,
            installModules = config.InstallModules.Select(m => new
            {
                name = m.Name,
                version = m.Version,
                minimumVersion = m.MinimumVersion,
                maximumVersion = m.MaximumVersion,
                repository = m.Repository,
                scope = m.Scope,
                force = m.Force,
                skipPublisherCheck = m.SkipPublisherCheck,
                allowPrerelease = m.AllowPrerelease,
            }).ToArray(),
            importModules = allImportModules,
            startupScriptPath = config.StartupScriptPath,
            startupScript = config.StartupScript,
            skipPublisherCheck = config.SkipPublisherCheck,
            allowClobber = config.AllowClobber,
            installTimeoutSeconds = config.InstallTimeoutSeconds,
            // Pool host reads this to size its runspace pool. Ignored by single-runspace host.
            runspacePoolSize = _poolSize,
        };

        _logger.LogInformation("Sending environment setup to OOP subprocess.");

        var result = await host
            .SendRequestAsync<JsonElement>("setup", setupParams, cancellationToken, setupRequestTimeout)
            .ConfigureAwait(false);

        if (result.TryGetProperty("success", out var successProp) && successProp.GetBoolean())
        {
            var installed = 0;
            var imported = 0;
            if (result.TryGetProperty("installedModules", out var installedProp))
                installed = installedProp.GetArrayLength();
            if (result.TryGetProperty("importedModules", out var importedProp))
                imported = importedProp.GetArrayLength();

            _logger.LogInformation(
                "OOP environment setup succeeded. Installed: {Installed}, Imported: {Imported}",
                installed, imported);
        }
        else
        {
            var errors = new List<string>();
            if (result.TryGetProperty("errors", out var errorsProp))
            {
                foreach (var err in errorsProp.EnumerateArray())
                {
                    var errStr = err.GetString();
                    if (errStr is not null)
                        errors.Add(errStr);
                }
            }

            var errorMessage = errors.Count > 0
                ? string.Join("; ", errors)
                : result.GetRawText();

            _logger.LogError("OOP environment setup failed: {Errors}", errorMessage);
            throw new InvalidOperationException($"OOP environment setup failed: {errorMessage}");
        }
    }

    /// <inheritdoc />
    public async Task<string> InvokeAsync(
        string commandName,
        IDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var host = RequireHost();

        var invokeParams = new { command = commandName, parameters };

        _logger.LogInformation("Invoking command '{CommandName}' via OOP subprocess.", commandName);

        JsonElement result;
        try
        {
            result = await host.SendRequestAsync<JsonElement>("invoke", invokeParams, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation is delivered to pwsh as a best-effort control frame.
            // Never reuse that process: a host may acknowledge cancellation before
            // an interrupted pipeline has completely unwound.
            await QuarantineAndReplaceHostAsync(host).ConfigureAwait(false);
            throw;
        }
        catch (InvalidOperationException ex) when (!_disposed && IsSubprocessDead(host, ex))
        {
            _logger.LogWarning(ex,
                "OOP subprocess died while invoking '{CommandName}'. Restarting and retrying once.",
                commandName);

            var restartedHost = await RestartHostAsync(cancellationToken).ConfigureAwait(false);
            result = await restartedHost.SendRequestAsync<JsonElement>("invoke", invokeParams, cancellationToken)
                .ConfigureAwait(false);
        }

        var output = string.Empty;
        if (result.TryGetProperty("output", out var outputElement))
        {
            output = outputElement.GetString() ?? string.Empty;
        }

        var hadErrors = result.TryGetProperty("hadErrors", out var hadErrorsElement)
            && hadErrorsElement.GetBoolean();
        var cancelled = result.TryGetProperty("cancelled", out var cancelledElement)
            && cancelledElement.GetBoolean();

        if (hadErrors && !cancelled)
        {
            // Surface non-terminating errors as a thrown exception so the MCP
            // framework can mark the tool call as IsError=true. Without this,
            // commands that write to the error stream (e.g. parameter
            // validation that uses Write-Error rather than throw, or commands
            // that emit partial output before reporting an error) would
            // silently return that partial output as a successful tool result.
            var errorMessage = ExtractErrorMessage(result, commandName, output);
            _logger.LogWarning("Command '{CommandName}' reported errors. {Errors}", commandName, errorMessage);
            throw new InvalidOperationException($"OOP error: {errorMessage}");
        }

        return output;
    }

    /// <summary>
    /// Builds a human-readable error message from the invoke response's
    /// <c>errors</c> array. Falls back to a generic description when the
    /// response lacks structured error entries.
    /// </summary>
    private static string ExtractErrorMessage(JsonElement result, string commandName, string output)
    {
        var errors = new List<string>();
        if (result.TryGetProperty("errors", out var errorsProp) && errorsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var err in errorsProp.EnumerateArray())
            {
                var msg = err.GetString();
                if (!string.IsNullOrWhiteSpace(msg))
                    errors.Add(msg);
            }
        }

        if (errors.Count > 0)
        {
            return $"command '{commandName}' reported {errors.Count} error(s): {string.Join("; ", errors)}";
        }

        // hadErrors was true but no structured error messages came back —
        // include a hint that prior output (if any) is being discarded so
        // it cannot be mistaken for a successful result.
        var suffix = string.IsNullOrEmpty(output)
            ? string.Empty
            : $" (discarded {output.Length}-char output)";
        return $"command '{commandName}' reported errors{suffix}.";
    }

    /// <summary>
    /// Quarantines an interrupted host, starts a clean replacement, and replays
    /// the latest setup. The cancelled command is intentionally never retried.
    /// </summary>
    private async Task QuarantineAndReplaceHostAsync(OutOfProcessHost interruptedHost)
    {
        await _restartLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_disposed || !ReferenceEquals(_host, interruptedHost))
            {
                return;
            }

            _host = null;
            try
            {
                await interruptedHost.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Quarantined OOP host cleanup failed.");
            }

            var scriptPath = await ResolveHostScriptPathAsync().ConfigureAwait(false);
            var pwshPath = ResolvePwshPath();
            var hostLogger = _loggerFactory.CreateLogger<OutOfProcessHost>();

            var newHost = new OutOfProcessHost(pwshPath, scriptPath, hostLogger, _requestTimeout);
            try
            {
                await newHost.StartAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                await newHost.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            _host = newHost;

            if (_lastSetupConfig is not null)
            {
                _logger.LogInformation("Replaying cached environment setup on restarted OOP subprocess.");
                try
                {
                    await SendSetupAsync(
                        newHost,
                        _lastSetupConfig,
                        _lastSetupConfigFilePath,
                        _lastSetupTimeout,
                        _lastDiscoveryModules,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Replaying environment setup on restarted OOP subprocess failed.");
                    throw;
                }
            }

        }
        finally
        {
            try { _restartLock.Release(); } catch (ObjectDisposedException) { /* shutting down */ }
        }
    }

    private static bool IsSubprocessDead(OutOfProcessHost host, InvalidOperationException ex) =>
        !host.IsRunning
        || ex.Message.Contains("OOP subprocess is not running", StringComparison.Ordinal);

    private async Task<OutOfProcessHost> RestartHostAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _restartLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_host is { IsRunning: true })
                return _host;

            if (_host is not null)
            {
                try { await _host.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogDebug(ex, "Disposing dead OOP host before restart failed."); }
                _host = null;
            }

            var scriptPath = await ResolveHostScriptPathAsync().ConfigureAwait(false);
            var newHost = new OutOfProcessHost(
                ResolvePwshPath(), scriptPath,
                _loggerFactory.CreateLogger<OutOfProcessHost>(), _requestTimeout);
            try
            {
                await newHost.StartAsync(cancellationToken).ConfigureAwait(false);
                _host = newHost;

                if (_lastSetupConfig is not null)
                {
                    await SendSetupAsync(newHost, _lastSetupConfig, _lastSetupConfigFilePath,
                        _lastSetupTimeout, _lastDiscoveryModules, cancellationToken).ConfigureAwait(false);
                }

                return newHost;
            }
            catch
            {
                await newHost.DisposeAsync().ConfigureAwait(false);
                _host = null;
                throw;
            }
        }
        finally
        {
            try { _restartLock.Release(); } catch (ObjectDisposedException) { }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogDebug("Disposing OOP command executor.");

        if (_host is not null)
        {
            await _host.DisposeAsync().ConfigureAwait(false);
            _host = null;
        }

        _restartLock.Dispose();
    }

    private OutOfProcessHost RequireHost()
    {
        return _host ?? throw new InvalidOperationException(
            "OOP subprocess is not running. Call StartAsync first.");
    }

    internal static string[] ResolveModulePaths(IEnumerable<string?>? configuredModulePaths, string baseDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDir);

        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var configuredPath in configuredModulePaths ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                continue;
            }

            var trimmedPath = configuredPath.Trim();
            var absolutePath = Path.IsPathRooted(trimmedPath)
                ? Path.GetFullPath(trimmedPath)
                : Path.GetFullPath(Path.Combine(baseDir, trimmedPath));

            resolved.Add(absolutePath);
        }

        return resolved.ToArray();
    }

    /// <summary>
    /// Resolves the path to the host script for the configured <see cref="HostMode"/>.
    /// Priority: environment variable override → embedded resource extraction → build output fallback.
    /// </summary>
    internal async Task<string> ResolveHostScriptPathAsync()
    {
        var scriptName = _hostMode == SubprocessHostMode.Pool
            ? "oop-host-pool.ps1"
            : "oop-host.ps1";

        var overridePath = Environment.GetEnvironmentVariable("POSHMCP_OOP_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (File.Exists(overridePath))
            {
                _logger.LogInformation("Using override OOP host script from POSHMCP_OOP_HOST_PATH: {Path}", overridePath);
                return overridePath;
            }

            _logger.LogWarning(
                "POSHMCP_OOP_HOST_PATH is set to '{Path}' but the file does not exist. Falling back to embedded resource.",
                overridePath);
        }

        var extractedPath = await ExtractHostScriptAsync(scriptName).ConfigureAwait(false);
        if (extractedPath is not null)
        {
            _logger.LogInformation("Using embedded {Script} extracted to: {Path}", scriptName, extractedPath);
            return extractedPath;
        }

        var basePath = Path.Combine(AppContext.BaseDirectory, "PowerShell", "OutOfProcess", scriptName);
        if (File.Exists(basePath))
        {
            _logger.LogInformation("Using {Script} from build output: {Path}", scriptName, basePath);
            return basePath;
        }

        var domainPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PowerShell", "OutOfProcess", scriptName);
        if (File.Exists(domainPath))
        {
            _logger.LogInformation("Using {Script} from domain base: {Path}", scriptName, domainPath);
            return domainPath;
        }

        throw new FileNotFoundException(
            $"Could not locate {scriptName}. Searched:\n" +
            "  POSHMCP_OOP_HOST_PATH environment variable\n" +
            "  Embedded assembly resource\n" +
            $"  {basePath}\n" +
            $"  {domainPath}");
    }

    /// <summary>
    /// Extracts the embedded host script resource (oop-host.ps1 or oop-host-pool.ps1)
    /// to a temp directory. Uses a SHA256 content hash to avoid unnecessary rewrites.
    /// Returns the extracted path, or null if the embedded resource is not found.
    /// </summary>
    internal async Task<string?> ExtractHostScriptAsync(string scriptName = "oop-host.ps1")
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(scriptName, StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            _logger.LogDebug("Embedded {Script} resource not found in assembly.", scriptName);
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            _logger.LogDebug("Unable to open embedded resource stream for '{ResourceName}'.", resourceName);
            return null;
        }

        var resourceBytes = new byte[stream.Length];
        await stream.ReadExactlyAsync(resourceBytes).ConfigureAwait(false);

        var hash = Convert.ToHexStringLower(SHA256.HashData(resourceBytes));

        var extractDir = Path.Combine(Path.GetTempPath(), "poshmcp");
        var extractPath = Path.Combine(extractDir, scriptName);
        var hashPath = Path.Combine(extractDir, scriptName + ".sha256");

        if (File.Exists(extractPath) && File.Exists(hashPath))
        {
            var existingHash = await File.ReadAllTextAsync(hashPath).ConfigureAwait(false);
            if (string.Equals(existingHash.Trim(), hash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Embedded {Script} already extracted with matching hash.", scriptName);
                return extractPath;
            }
        }

        Directory.CreateDirectory(extractDir);
        await File.WriteAllBytesAsync(extractPath, resourceBytes).ConfigureAwait(false);
        await File.WriteAllTextAsync(hashPath, hash).ConfigureAwait(false);

        _logger.LogDebug("Extracted {Script} to {Path} (hash: {Hash}).", scriptName, extractPath, hash);
        return extractPath;
    }

    /// <summary>
    /// Resolves the path to the pwsh executable.
    /// </summary>
    internal static string ResolvePwshPath()
    {
        var pwshName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";

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
