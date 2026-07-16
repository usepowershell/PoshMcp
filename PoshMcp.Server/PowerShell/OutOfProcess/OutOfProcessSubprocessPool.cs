using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PoshMcp.Server.PowerShell;

namespace PoshMcp.Server.PowerShell.OutOfProcess;

/// <summary>
/// Option B (issue #192) implementation of <see cref="ICommandExecutor"/>: a pool of N
/// independent <see cref="OutOfProcessHost"/> instances. Each request leases one host,
/// runs against it, and returns it to the pool when finished. Hosts that crash, time out,
/// or otherwise become unhealthy are reconciled out of rotation and replaced by a
/// background reconciler.
/// </summary>
/// <remarks>
/// <para>
/// State model — there are two collaborating data structures, both required:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       A <see cref="Channel{T}"/> of <see cref="HostSlot"/> instances representing the
///       <em>available</em> queue. Lease is <c>await Reader.ReadAsync</c>; return is
///       <c>Writer.TryWrite</c>. The channel alone is insufficient because a host that
///       crashes mid-lease is not in the channel at the moment of the crash, and a naive
///       channel-only implementation would have no way to discover that a slot is dead
///       and needs replacement.
///     </description>
///   </item>
///   <item>
///     <description>
///       A <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by a stable
///       <em>slot index</em> (0..N-1) holding <see cref="HostSlot"/> records. The
///       dictionary is the source of truth for "what hosts exist" and "what is the
///       current status of slot <c>i</c>". When a host process is replaced the slot
///       index stays constant; the underlying <see cref="OutOfProcessHost"/> instance
///       on the slot is swapped. We deliberately key by slot index rather than process
///       id so reconciliation is unambiguous (process ids are reused by the OS on Linux
///       and the slot-to-host mapping survives restarts).
///     </description>
///   </item>
/// </list>
/// <para>
/// <b>Discovery cache key.</b> <see cref="DiscoverCommandsAsync"/> runs once against
/// any healthy host and caches the schemas. The cache is keyed by a SHA-256 fingerprint
/// of the <see cref="EnvironmentConfiguration"/> applied via <see cref="SetupAsync"/>:
/// install/import module list, module paths, startup script path/content, and the
/// effective discovery module list (top-level <c>Modules</c>). When a host is replaced
/// the reconciler reapplies the same setup, so the fingerprint — and therefore the
/// cached schemas — remain valid. If <see cref="SetupAsync"/> is later invoked with
/// a different fingerprint the cache is cleared.
/// </para>
/// <para>
/// <b>Startup policy.</b> The first host (slot 0) is started fail-fast — if it cannot
/// reach a healthy ping, <see cref="StartAsync"/> throws. Hosts 1..N-1 are started in
/// parallel with bounded retry + exponential backoff. As long as
/// <see cref="OutOfProcessSubprocessPoolOptions.MinHealthyForStartup"/> hosts are
/// healthy at the end of startup the pool starts (degraded if any failed); otherwise
/// it throws.
/// </para>
/// <para>
/// <b>Per-request kill on timeout.</b> When <see cref="InvokeAsync"/> observes a
/// <see cref="TimeoutException"/> from the underlying host (or any failure that
/// indicates the host is no longer trustworthy), the pool kills that specific host
/// process, marks the slot dead, and lets the reconciler start a replacement. The
/// caller still observes the timeout. This is Option B's structural advantage over
/// the single-host executor: a runaway request kills 1 of N, not the only host.
/// </para>
/// </remarks>
public sealed class OutOfProcessSubprocessPool : ICommandExecutor
{
    private readonly ILogger<OutOfProcessSubprocessPool> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly OutOfProcessSubprocessPoolOptions _options;
    private readonly TimeSpan _requestTimeout;
    private readonly string _pwshPath;
    private readonly string _hostScriptPath;

    private readonly ConcurrentDictionary<int, HostSlot> _slots = new();
    private readonly Channel<HostSlot> _available =
        Channel.CreateUnbounded<HostSlot>(
            new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });

    private readonly object _envLock = new();
    // Serializes the point at which a caller obtains a host with the currently
    // configured environment. It is deliberately not held for command execution:
    // reloads let existing work finish, but prevent new work from leasing stale hosts.
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private EnvironmentConfiguration? _cachedEnv;
    private string? _cachedEnvFingerprint;
    private string? _cachedConfigFilePath;
    private TimeSpan? _cachedSetupTimeout;
    private string[]? _cachedDiscoveryModules;
    private IReadOnlyList<RemoteToolSchema>? _cachedSchemas;
    private string? _cachedSchemasFingerprint;
    private RemoteModuleImportsPayload? _cachedModuleImports;
    private long _generation;

    /// <inheritdoc />
    public RemoteModuleImportsPayload? LastModuleImports
    {
        get { lock (_envLock) { return _cachedModuleImports; } }
    }

    private CancellationTokenSource? _reconcilerCts;
    private Task? _reconcilerTask;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// Creates a new <see cref="OutOfProcessSubprocessPool"/>.
    /// </summary>
    public OutOfProcessSubprocessPool(
        string pwshPath,
        string hostScriptPath,
        OutOfProcessSubprocessPoolOptions options,
        ILoggerFactory? loggerFactory = null,
        TimeSpan? requestTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pwshPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostScriptPath);
        ArgumentNullException.ThrowIfNull(options);

        if (options.PoolSize < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "PoolSize must be >= 1.");
        if (options.MinHealthyForStartup < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MinHealthyForStartup must be >= 1.");
        if (options.MinHealthyForStartup > options.PoolSize)
            throw new ArgumentOutOfRangeException(nameof(options),
                "MinHealthyForStartup must be <= PoolSize.");

        _pwshPath = pwshPath;
        _hostScriptPath = hostScriptPath;
        _options = options;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<OutOfProcessSubprocessPool>();
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Number of slots currently in the dictionary whose status is
    /// <see cref="HostStatus.Healthy"/> or <see cref="HostStatus.Leased"/>.
    /// Useful for tests and diagnostics.
    /// </summary>
    public int HealthyCount
    {
        get
        {
            var count = 0;
            foreach (var kvp in _slots)
            {
                var status = kvp.Value.Status;
                if (status == HostStatus.Healthy || status == HostStatus.Leased)
                    count++;
            }
            return count;
        }
    }

    /// <summary>
    /// Configured pool size (constant).
    /// </summary>
    public int PoolSize => _options.PoolSize;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
            throw new InvalidOperationException("Pool has already been started.");

        _logger.LogInformation(
            "Starting subprocess pool: size={PoolSize}, minHealthy={MinHealthy}.",
            _options.PoolSize, _options.MinHealthyForStartup);

        // Slot 0: fail-fast smoke test.
        var slot0 = await StartSlotAsync(0, failFast: true, cancellationToken).ConfigureAwait(false);
        _available.Writer.TryWrite(slot0);

        // Slots 1..N-1: parallel with retry + backoff. Don't propagate individual failures —
        // the MinHealthyForStartup gate decides whether to throw.
        if (_options.PoolSize > 1)
        {
            var startupTasks = new List<Task>(_options.PoolSize - 1);
            for (var i = 1; i < _options.PoolSize; i++)
            {
                var index = i;
                startupTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var slot = await StartSlotWithRetryAsync(index, cancellationToken)
                            .ConfigureAwait(false);
                        if (slot is not null)
                            _available.Writer.TryWrite(slot);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Slot {Index} failed all startup retries; pool will start degraded.",
                            index);
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(startupTasks).ConfigureAwait(false);
        }

        var healthy = HealthyCount;
        if (healthy < _options.MinHealthyForStartup)
        {
            _logger.LogError(
                "Pool startup aborted: only {Healthy} of {PoolSize} hosts came up healthy " +
                "(minimum required: {MinHealthy}).",
                healthy, _options.PoolSize, _options.MinHealthyForStartup);

            await DisposeAsync().ConfigureAwait(false);

            throw new InvalidOperationException(
                $"OutOfProcessSubprocessPool failed startup: {healthy}/{_options.PoolSize} healthy, " +
                $"minimum required {_options.MinHealthyForStartup}.");
        }

        if (healthy < _options.PoolSize)
        {
            _logger.LogWarning(
                "Pool started degraded: {Healthy}/{PoolSize} hosts healthy.",
                healthy, _options.PoolSize);
        }
        else
        {
            _logger.LogInformation(
                "Pool started: {Healthy}/{PoolSize} hosts healthy.",
                healthy, _options.PoolSize);
        }

        _started = true;

        // Start the reconciler that watches for dead slots.
        _reconcilerCts = new CancellationTokenSource();
        _reconcilerTask = Task.Run(
            () => ReconcilerLoopAsync(_reconcilerCts.Token),
            CancellationToken.None);
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
        EnsureStarted();
        ArgumentNullException.ThrowIfNull(config);

        var discoveryModulesArr = discoveryModules?.ToArray();
        var fingerprint = ComputeEnvironmentFingerprint(config, discoveryModulesArr);

        await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var generation = Interlocked.Increment(ref _generation);
            lock (_envLock)
            {
                _cachedEnv = config;
                _cachedEnvFingerprint = fingerprint;
                _cachedConfigFilePath = configFilePath;
                _cachedSetupTimeout = setupRequestTimeout;
                _cachedDiscoveryModules = discoveryModulesArr;

                if (_cachedSchemasFingerprint is not null
                    && !string.Equals(_cachedSchemasFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    _logger.LogDebug(
                        "Environment fingerprint changed ({Old} -> {New}); invalidating discovery cache.",
                        _cachedSchemasFingerprint, fingerprint);
                    _cachedSchemas = null;
                    _cachedSchemasFingerprint = null;
                    _cachedModuleImports = null;
                }
            }

            // The gate prevents a Healthy slot becoming leased while setup is sent.
            // Existing leases are never reconfigured in-flight: they finish against
            // their original environment and are retired when returned.
            var snapshot = _slots.Values.ToArray();
            var idleSlots = snapshot.Where(slot => slot.Status == HostStatus.Healthy).ToArray();
            foreach (var leasedSlot in snapshot.Where(slot => slot.Status == HostStatus.Leased))
            {
                leasedSlot.RetireOnReturn = true;
            }

            _logger.LogInformation(
                "Applying environment generation {Generation} to {Count} idle hosts; {Retiring} active hosts will drain.",
                generation, idleSlots.Length, snapshot.Count(slot => slot.Status == HostStatus.Leased));

            var setupTasks = idleSlots.Select(async slot =>
            {
                try
                {
                    await ApplySetupToHostAsync(slot, config, configFilePath,
                        setupRequestTimeout, discoveryModulesArr, cancellationToken)
                        .ConfigureAwait(false);
                    slot.Generation = generation;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Setup failed on slot {Index}; marking dead for reconciliation.",
                        slot.Index);
                    MarkSlotDead(slot);
                    throw;
                }
            }).ToArray();

            await Task.WhenAll(setupTasks).ConfigureAwait(false);
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RemoteToolSchema>> DiscoverCommandsAsync(
        PowerShellConfiguration config,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureStarted();

        // Per-pool cache: discovery is identical across hosts because each runs the same
        // setup against the same environment fingerprint. Once cached, replacements
        // reuse the result without re-running discovery.
        lock (_envLock)
        {
            if (_cachedSchemas is not null
                && string.Equals(_cachedSchemasFingerprint, _cachedEnvFingerprint, StringComparison.Ordinal))
            {
                _logger.LogDebug(
                    "Discovery cache hit ({Count} schemas, fingerprint {Fingerprint}).",
                    _cachedSchemas.Count, _cachedSchemasFingerprint);
                return _cachedSchemas;
            }
        }

        var discoverParams = new
        {
            modules = config.Modules,
            functionNames = config.GetEffectiveCommandNames(),
            includePatterns = config.IncludePatterns,
            excludePatterns = config.ExcludePatterns
        };

        await using var lease = await LeaseCurrentGenerationAsync(cancellationToken).ConfigureAwait(false);
        var host = lease.Host;

        _logger.LogInformation(
            "Discovering commands via OOP pool (slot {Index}).", lease.SlotIndex);

        var result = await host.SendRequestAsync<JsonElement>("discover", discoverParams, cancellationToken)
            .ConfigureAwait(false);

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        List<RemoteToolSchema> schemas;
        if (result.TryGetProperty("commands", out var commandsElement))
        {
            schemas = JsonSerializer.Deserialize<List<RemoteToolSchema>>(
                commandsElement.GetRawText(), jsonOptions)
                ?? new List<RemoteToolSchema>();
        }
        else
        {
            _logger.LogWarning(
                "Discover response missing 'commands'. Raw: {Raw}", result.GetRawText());
            schemas = new List<RemoteToolSchema>();
        }

        // Spec 011 FR-263-2 / FR-263-10: optional moduleImports payload from
        // the OOP host. Older hosts omit this entirely; missing is fine and
        // signals to consumers (DoctorService) to fall back to the in-process
        // probe path with a one-time warning.
        RemoteModuleImportsPayload? moduleImports = null;
        if (result.TryGetProperty("moduleImports", out var moduleImportsElement)
            && moduleImportsElement.ValueKind != JsonValueKind.Null)
        {
            try
            {
                moduleImports = JsonSerializer.Deserialize<RemoteModuleImportsPayload>(
                    moduleImportsElement.GetRawText(), jsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize moduleImports payload; treating as absent.");
                moduleImports = null;
            }
        }

        lock (_envLock)
        {
            _cachedSchemas = schemas;
            _cachedSchemasFingerprint = _cachedEnvFingerprint;
            _cachedModuleImports = moduleImports;
        }

        _logger.LogInformation(
            "Discovered {Count} commands; cached under fingerprint {Fingerprint}.",
            schemas.Count, _cachedSchemasFingerprint ?? "(none)");

        return schemas;
    }

    /// <inheritdoc />
    public async Task<string> InvokeAsync(
        string commandName,
        IDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureStarted();

        await using var lease = await LeaseCurrentGenerationAsync(cancellationToken).ConfigureAwait(false);
        var host = lease.Host;
        var slotIndex = lease.SlotIndex;

        var invokeParams = new { command = commandName, parameters };

        _logger.LogInformation(
            "Invoking '{CommandName}' on pool slot {Index} (PID {Pid}).",
            commandName, slotIndex, host.ProcessId);

        try
        {
            var result = await host.SendRequestAsync<JsonElement>("invoke", invokeParams, cancellationToken)
                .ConfigureAwait(false);

            var output = string.Empty;
            if (result.TryGetProperty("output", out var outputElement))
                output = outputElement.GetString() ?? string.Empty;

            var hadErrors = result.TryGetProperty("hadErrors", out var hadErrorsElement)
                && hadErrorsElement.GetBoolean();
            var cancelled = result.TryGetProperty("cancelled", out var cancelledElement)
                && cancelledElement.GetBoolean();

            if (hadErrors && !cancelled)
            {
                // Surface non-terminating errors as a thrown exception so MCP
                // marks the tool result IsError=true. See the matching block
                // in OutOfProcessCommandExecutor.InvokeAsync for rationale.
                var errorMessage = ExtractInvokeErrorMessage(result, commandName, output);
                _logger.LogWarning(
                    "Command '{CommandName}' on slot {Index} reported errors. {Errors}",
                    commandName, slotIndex, errorMessage);
                throw new InvalidOperationException($"OOP error: {errorMessage}");
            }

            return output;
        }
        catch (TimeoutException tex)
        {
            // Per-request kill-on-timeout: this host is now suspect. Take it out of
            // rotation and let the reconciler start a replacement. Bonus over single-host
            // mode: only this slot dies; the rest of the pool keeps serving.
            _logger.LogWarning(tex,
                "Invoke '{CommandName}' timed out on slot {Index}; killing host.",
                commandName, slotIndex);
            lease.MarkBroken();
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A cancel frame is best-effort. Do not put a host whose pipeline was
            // interrupted back into rotation; replacement is safer than reusing
            // potentially contaminated runspace/process state.
            _logger.LogInformation(
                "Invoke '{CommandName}' was cancelled on slot {Index}; quarantining host.",
                commandName, slotIndex);
            lease.MarkBroken();
            throw;
        }
        catch (InvalidOperationException ex) when (!host.IsRunning)
        {
            // Host died mid-invoke; surface the error and let the reconciler restart it.
            _logger.LogWarning(ex,
                "Slot {Index} host died mid-invoke for '{CommandName}'.",
                slotIndex, commandName);
            lease.MarkBroken();
            throw;
        }
    }

    /// <summary>
    /// Builds a human-readable message from the invoke response's structured
    /// <c>errors</c> array. Mirrors the helper in
    /// <see cref="OutOfProcessCommandExecutor"/> so single-host and pool-host
    /// callers produce consistent failure messages.
    /// </summary>
    private static string ExtractInvokeErrorMessage(JsonElement result, string commandName, string output)
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

        var suffix = string.IsNullOrEmpty(output)
            ? string.Empty
            : $" (discarded {output.Length}-char output)";
        return $"command '{commandName}' reported errors{suffix}.";
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogDebug("Disposing OOP subprocess pool.");

        _available.Writer.TryComplete();

        if (_reconcilerCts is not null)
        {
            try { _reconcilerCts.Cancel(); } catch { /* ignore */ }
        }

        if (_reconcilerTask is not null)
        {
            try { await _reconcilerTask.ConfigureAwait(false); }
            catch { /* reconciler exits on cancel */ }
        }

        var disposeTasks = _slots
            .Select(kvp => kvp.Value)
            .Where(s => s.Host is not null)
            .Select(s => s.Host!.DisposeAsync().AsTask())
            .ToArray();

        try
        {
            await Task.WhenAll(disposeTasks).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort.
        }

        _slots.Clear();
        _reconcilerCts?.Dispose();
        _reloadGate.Dispose();
    }

    // ---- Internal helpers (visible to tests) ----

    /// <summary>
    /// Computes a stable SHA-256 fingerprint over the environment configuration
    /// fields that influence module visibility and discovery output. Used as the
    /// discovery cache key.
    /// </summary>
    internal static string ComputeEnvironmentFingerprint(
        EnvironmentConfiguration config,
        IEnumerable<string>? discoveryModules)
    {
        ArgumentNullException.ThrowIfNull(config);

        // Serialize a deterministic, ordered projection of the relevant fields.
        // Order matters: we sort lists so equivalent configurations with different
        // ordering produce the same fingerprint.
        var modulePaths = config.ModulePaths
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var importModules = config.ImportModules
            .Concat(discoveryModules ?? Enumerable.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var installModules = config.InstallModules
            .Select(m => new
            {
                name = m.Name?.Trim() ?? string.Empty,
                version = m.Version,
                minimum = m.MinimumVersion,
                maximum = m.MaximumVersion,
                repository = m.Repository,
                scope = m.Scope,
            })
            .OrderBy(m => m.name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.version ?? string.Empty, StringComparer.Ordinal)
            .ToArray();

        var projection = new
        {
            modulePaths,
            importModules,
            installModules,
            startupScript = config.StartupScript,
            startupScriptPath = config.StartupScriptPath,
            trustPSGallery = config.TrustPSGallery,
        };

        var json = JsonSerializer.Serialize(projection);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexStringLower(hash);
    }

    private async Task<HostSlot> StartSlotAsync(
        int index, bool failFast, CancellationToken cancellationToken)
    {
        var slot = new HostSlot(index) { Status = HostStatus.Starting };
        _slots[index] = slot;

        var hostLogger = _loggerFactory.CreateLogger<OutOfProcessHost>();
        var host = new OutOfProcessHost(_pwshPath, _hostScriptPath, hostLogger, _requestTimeout);

        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try { await host.DisposeAsync().ConfigureAwait(false); } catch { /* ignore */ }
            slot.Status = HostStatus.Dead;

            if (failFast)
            {
                _slots.TryRemove(index, out _);
                throw;
            }

            return slot; // caller will retry / leave dead for reconciler
        }

        slot.Host = host;
        slot.Status = HostStatus.Healthy;

        _logger.LogInformation(
            "Slot {Index} started (PID {Pid}).", index, host.ProcessId);

        // If we have cached env config, apply it to this freshly-started host.
        EnvironmentConfiguration? envSnapshot;
        string? configFilePathSnapshot;
        TimeSpan? setupTimeoutSnapshot;
        string[]? discoveryModulesSnapshot;
        lock (_envLock)
        {
            envSnapshot = _cachedEnv;
            configFilePathSnapshot = _cachedConfigFilePath;
            setupTimeoutSnapshot = _cachedSetupTimeout;
            discoveryModulesSnapshot = _cachedDiscoveryModules;
        }

        if (envSnapshot is not null)
        {
            try
            {
                await ApplySetupToHostAsync(slot, envSnapshot, configFilePathSnapshot,
                    setupTimeoutSnapshot, discoveryModulesSnapshot, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Initial setup failed on slot {Index}; marking dead.", index);
                MarkSlotDead(slot);

                if (failFast)
                {
                    _slots.TryRemove(index, out _);
                    throw;
                }
            }
        }

        slot.Generation = Volatile.Read(ref _generation);

        return slot;
    }

    private async Task<HostSlot?> StartSlotWithRetryAsync(
        int index, CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _options.StartupRetryCount);
        var delay = _options.StartupBackoffInitial;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var slot = await StartSlotAsync(index, failFast: false, cancellationToken)
                    .ConfigureAwait(false);
                if (slot.Status == HostStatus.Healthy)
                    return slot;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogDebug(ex,
                    "Slot {Index} startup attempt {Attempt} failed; retrying in {Delay}.",
                    index, attempt, delay);
            }

            if (attempt < maxAttempts)
            {
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                delay = TimeSpan.FromMilliseconds(
                    Math.Min(delay.TotalMilliseconds * 2, _options.StartupBackoffMax.TotalMilliseconds));
            }
        }

        return null;
    }

    private async Task ApplySetupToHostAsync(
        HostSlot slot,
        EnvironmentConfiguration config,
        string? configFilePath,
        TimeSpan? setupRequestTimeout,
        IEnumerable<string>? discoveryModules,
        CancellationToken cancellationToken)
    {
        var host = slot.Host
            ?? throw new InvalidOperationException(
                $"Cannot apply setup to slot {slot.Index}: no host attached.");

        var baseDir = !string.IsNullOrEmpty(configFilePath)
            ? Path.GetDirectoryName(Path.GetFullPath(configFilePath))!
            : Directory.GetCurrentDirectory();

        var resolvedModulePaths = config.ModulePaths
            .Select(p => Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(baseDir, p)))
            .ToArray();

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
        };

        var result = await host
            .SendRequestAsync<JsonElement>("setup", setupParams, cancellationToken, setupRequestTimeout)
            .ConfigureAwait(false);

        if (!result.TryGetProperty("success", out var successProp) || !successProp.GetBoolean())
        {
            var errors = new List<string>();
            if (result.TryGetProperty("errors", out var errorsProp))
            {
                foreach (var err in errorsProp.EnumerateArray())
                {
                    var errStr = err.GetString();
                    if (errStr is not null) errors.Add(errStr);
                }
            }

            var errorMessage = errors.Count > 0 ? string.Join("; ", errors) : result.GetRawText();
            throw new InvalidOperationException(
                $"OOP environment setup failed on slot {slot.Index}: {errorMessage}");
        }

        _logger.LogInformation(
            "Setup completed on slot {Index} (PID {Pid}).",
            slot.Index, host.ProcessId);
    }

    private async Task<PoolLease> LeaseAsync(CancellationToken cancellationToken)
    {
        // Loop because a slot we read out of the channel may have been killed
        // between Write and Read (e.g., a reconciler tick); skip dead slots.
        while (true)
        {
            HostSlot slot;
            try
            {
                slot = await _available.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ChannelClosedException ex)
            {
                throw new InvalidOperationException("Pool is shut down.", ex);
            }

            if (slot.Status != HostStatus.Healthy || slot.Host is null || !slot.Host.IsRunning
                || slot.RetireOnReturn || slot.Generation != Volatile.Read(ref _generation))
            {
                _logger.LogDebug(
                    "Skipping stale lease for slot {Index} (status={Status}, generation={Generation}).",
                    slot.Index, slot.Status, slot.Generation);
                MarkSlotDead(slot);
                continue;
            }

            slot.Status = HostStatus.Leased;
            return new PoolLease(this, slot);
        }
    }

    private async Task<PoolLease> LeaseCurrentGenerationAsync(CancellationToken cancellationToken)
    {
        await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LeaseAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private void ReturnLease(HostSlot slot, bool broken)
    {
        if (_disposed) return;

        if (broken || slot.RetireOnReturn || slot.Host is null || !slot.Host.IsRunning)
        {
            MarkSlotDead(slot);
            return;
        }

        slot.Status = HostStatus.Healthy;
        _available.Writer.TryWrite(slot);
    }

    private void MarkSlotDead(HostSlot slot)
    {
        if (slot.Status == HostStatus.Dead || slot.Status == HostStatus.Replacing)
            return;

        slot.Status = HostStatus.Dead;

        var host = slot.Host;
        if (host is not null)
        {
            // Fire-and-forget kill — synchronous Kill on Process is fine here since
            // OutOfProcessHost.DisposeAsync is the proper teardown path.
            _ = Task.Run(async () =>
            {
                try { await host.DisposeAsync().ConfigureAwait(false); }
                catch { /* best-effort */ }
            });
            slot.Host = null;
        }

        _logger.LogWarning("Slot {Index} marked dead; reconciler will replace.", slot.Index);
    }

    private async Task ReconcilerLoopAsync(CancellationToken cancellationToken)
    {
        var interval = _options.ReconcilerInterval;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_disposed) return;

            HostSlot[] dead;
            try
            {
                dead = _slots.Values
                    .Where(s => s.Status == HostStatus.Dead)
                    .ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var slot in dead)
            {
                if (cancellationToken.IsCancellationRequested) return;

                slot.Status = HostStatus.Replacing;
                slot.FailedReplacements++;

                try
                {
                    var hostLogger = _loggerFactory.CreateLogger<OutOfProcessHost>();
                    var host = new OutOfProcessHost(
                        _pwshPath, _hostScriptPath, hostLogger, _requestTimeout);

                    await host.StartAsync(cancellationToken).ConfigureAwait(false);

                    slot.Host = host;
                    slot.Status = HostStatus.Healthy;

                    EnvironmentConfiguration? envSnapshot;
                    string? configFilePathSnapshot;
                    TimeSpan? setupTimeoutSnapshot;
                    string[]? discoveryModulesSnapshot;
                    lock (_envLock)
                    {
                        envSnapshot = _cachedEnv;
                        configFilePathSnapshot = _cachedConfigFilePath;
                        setupTimeoutSnapshot = _cachedSetupTimeout;
                        discoveryModulesSnapshot = _cachedDiscoveryModules;
                    }

                    if (envSnapshot is not null)
                    {
                        await ApplySetupToHostAsync(slot, envSnapshot,
                            configFilePathSnapshot, setupTimeoutSnapshot,
                            discoveryModulesSnapshot, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    slot.Generation = Volatile.Read(ref _generation);
                    slot.RetireOnReturn = false;
                    slot.FailedReplacements = 0;
                    _available.Writer.TryWrite(slot);

                    _logger.LogInformation(
                        "Slot {Index} replacement healthy (PID {Pid}).",
                        slot.Index, host.ProcessId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Slot {Index} replacement failed (attempt {Attempt}); will retry.",
                        slot.Index, slot.FailedReplacements);

                    if (slot.Host is not null)
                    {
                        try { await slot.Host.DisposeAsync().ConfigureAwait(false); }
                        catch { /* ignore */ }
                        slot.Host = null;
                    }

                    slot.Status = HostStatus.Dead;
                }
            }
        }
    }

    private void EnsureStarted()
    {
        if (!_started)
            throw new InvalidOperationException("Pool has not been started. Call StartAsync first.");
    }

    /// <summary>
    /// Per-slot record. Mutable; protected by the channel/dictionary protocol —
    /// only one of {channel reader, reconciler, lease holder} touches a slot at a time
    /// for the fields that matter for correctness.
    /// </summary>
    internal sealed class HostSlot
    {
        public HostSlot(int index) { Index = index; }
        public int Index { get; }
        public volatile HostStatus Status;
        public OutOfProcessHost? Host;
        public int FailedReplacements;
        public long Generation;
        public volatile bool RetireOnReturn;
    }

    /// <summary>
    /// Lifecycle state for a slot.
    /// </summary>
    internal enum HostStatus
    {
        Starting,
        Healthy,
        Leased,
        Dead,
        Replacing,
    }

    /// <summary>
    /// IAsyncDisposable wrapper around a leased host. Disposing returns the host to the
    /// channel if still alive, or marks the slot dead for reconciliation if not.
    /// </summary>
    internal sealed class PoolLease : IAsyncDisposable
    {
        private readonly OutOfProcessSubprocessPool _pool;
        private readonly HostSlot _slot;
        private bool _broken;
        private bool _disposed;

        internal PoolLease(OutOfProcessSubprocessPool pool, HostSlot slot)
        {
            _pool = pool;
            _slot = slot;
        }

        public OutOfProcessHost Host => _slot.Host
            ?? throw new InvalidOperationException("Lease has no host attached.");

        public int SlotIndex => _slot.Index;

        public void MarkBroken() => _broken = true;

        public ValueTask DisposeAsync()
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            _pool.ReturnLease(_slot, _broken);
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// Tunable knobs for <see cref="OutOfProcessSubprocessPool"/>.
/// </summary>
public sealed class OutOfProcessSubprocessPoolOptions
{
    /// <summary>
    /// Total number of pwsh subprocess hosts to launch. Default: 4.
    /// </summary>
    public int PoolSize { get; init; } = 4;

    /// <summary>
    /// Minimum number of healthy hosts required for the pool to start. Default: 1.
    /// First host is always fail-fast; this gates the soft-failure tolerance for
    /// hosts 2..N.
    /// </summary>
    public int MinHealthyForStartup { get; init; } = 1;

    /// <summary>
    /// Number of startup attempts per non-first host. Default: 3.
    /// </summary>
    public int StartupRetryCount { get; init; } = 3;

    /// <summary>
    /// Initial backoff between startup attempts. Doubled on each retry up to
    /// <see cref="StartupBackoffMax"/>. Default: 250 ms.
    /// </summary>
    public TimeSpan StartupBackoffInitial { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Maximum backoff between startup attempts. Default: 5 seconds.
    /// </summary>
    public TimeSpan StartupBackoffMax { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Interval at which the background reconciler scans for dead slots and starts
    /// replacements. Default: 1 second.
    /// </summary>
    public TimeSpan ReconcilerInterval { get; init; } = TimeSpan.FromSeconds(1);
}
