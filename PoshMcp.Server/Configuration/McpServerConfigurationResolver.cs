using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell.Pool;

namespace PoshMcp;

/// <summary>
/// Resolves <see cref="HttpTransportMode"/> and <see cref="RunspacePoolOptions"/> from
/// <see cref="IConfiguration"/>, applying per-key legacy alias fallback with startup warnings.
/// </summary>
/// <remarks>
/// <para>
/// Precedence rules (per key):
/// <list type="number">
///   <item>New key explicitly present in configuration → use new value.</item>
///   <item>Legacy alias explicitly present, new key absent → use legacy value and emit one warning.</item>
///   <item>Neither present → use <see cref="RunspacePoolOptions"/> default.</item>
/// </list>
/// </para>
/// <para>
/// A warning is emitted for every deprecated key that is <em>present</em> in configuration,
/// regardless of whether the new key also overrides it.
/// </para>
/// </remarks>
internal static class McpServerConfigurationResolver
{
    // ── New configuration key paths (relative to the IConfiguration root) ──────────

    /// <summary>Full path for the HTTP transport mode setting.</summary>
    internal const string HttpTransportModeKey = "McpServer:HttpTransportMode";

    /// <summary>Full path for the new pool section's MaxPoolSize key.</summary>
    internal const string RunspacePoolMaxPoolSizeKey = "McpServer:RunspacePool:MaxPoolSize";

    /// <summary>Full path for the new pool section's MinPoolSize key.</summary>
    internal const string RunspacePoolMinPoolSizeKey = "McpServer:RunspacePool:MinPoolSize";

    /// <summary>Full path for the new pool section's EagerWarmCount key.</summary>
    internal const string RunspacePoolEagerWarmCountKey = "McpServer:RunspacePool:EagerWarmCount";

    /// <summary>Full path for the new pool section's AcquisitionTimeout key.</summary>
    internal const string RunspacePoolAcquisitionTimeoutKey = "McpServer:RunspacePool:AcquisitionTimeout";

    /// <summary>Full path for the new pool section's IdleTtl key.</summary>
    internal const string RunspacePoolIdleTtlKey = "McpServer:RunspacePool:IdleTtl";

    /// <summary>Full path for the new pool section's SweepInterval key.</summary>
    internal const string RunspacePoolSweepIntervalKey = "McpServer:RunspacePool:SweepInterval";

    // ── Legacy configuration key paths ────────────────────────────────────────────

    /// <summary>Deprecated. Maps to <see cref="RunspacePoolMaxPoolSizeKey"/>.</summary>
    internal const string LegacySessionRunspaceCapacityKey = "McpServer:SessionRunspaceCapacity";

    /// <summary>Deprecated (int, seconds). Maps to <see cref="RunspacePoolIdleTtlKey"/>.</summary>
    internal const string LegacySessionRunspaceIdleTtlSecondsKey = "McpServer:SessionRunspaceIdleTtlSeconds";

    /// <summary>Deprecated (int, seconds). Maps to <see cref="RunspacePoolSweepIntervalKey"/>.</summary>
    internal const string LegacySessionRunspaceSweepIntervalSecondsKey = "McpServer:SessionRunspaceSweepIntervalSeconds";

    /// <summary>Deprecated. Maps to <see cref="RunspacePoolMinPoolSizeKey"/> and <see cref="RunspacePoolEagerWarmCountKey"/>.</summary>
    internal const string LegacySessionRunspaceWarmStandbyCountKey = "McpServer:SessionRunspaceWarmStandbyCount";

    /// <summary>Deprecated (int, seconds). Maps to <see cref="RunspacePoolAcquisitionTimeoutKey"/>.</summary>
    internal const string LegacySessionRunspaceAcquisitionTimeoutSecondsKey = "McpServer:SessionRunspaceAcquisitionTimeoutSeconds";

    private const string RemovalPolicy =
        " This key is deprecated and will be removed in a future major version of PoshMcp.";

    private const string BehaviorNote =
        " Note: HTTP sessions no longer imply session-affine PowerShell state;" +
        " both Stateful and Stateless HTTP transport modes use the same warm-worker pool for PowerShell execution.";

    // ── Public entry point ────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves <see cref="HttpTransportMode"/> and <see cref="RunspacePoolOptions"/> from
    /// <paramref name="configuration"/> with legacy alias fallback.
    /// Emits one <see cref="LogLevel.Warning"/> per deprecated key that is present.
    /// Throws <see cref="InvalidOperationException"/> for invalid enum values or invalid pool options.
    /// </summary>
    /// <param name="configuration">The root configuration (typically the application's IConfiguration).</param>
    /// <param name="logger">Logger used for deprecation warnings.</param>
    /// <returns>
    /// A tuple of the resolved <see cref="HttpTransportMode"/> and the resolved
    /// <see cref="RunspacePoolOptions"/> (validated).
    /// </returns>
    internal static (HttpTransportMode TransportMode, RunspacePoolOptions PoolOptions) Resolve(
        IConfiguration configuration,
        ILogger logger)
    {
        var transportMode = ResolveHttpTransportMode(configuration);
        var poolOptions = ResolveRunspacePoolOptions(configuration, logger);

        var errors = poolOptions.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid RunspacePool configuration: {string.Join("; ", errors)}");
        }

        return (transportMode, poolOptions);
    }

    // ── HttpTransportMode resolution ──────────────────────────────────────────────

    private static HttpTransportMode ResolveHttpTransportMode(IConfiguration configuration)
    {
        var rawValue = configuration[HttpTransportModeKey];
        if (rawValue is null)
        {
            return HttpTransportMode.Stateless;
        }

        if (Enum.TryParse<HttpTransportMode>(rawValue, ignoreCase: true, out var mode) && Enum.IsDefined(mode))
        {
            return mode;
        }

        var validValues = string.Join(", ", Enum.GetNames<HttpTransportMode>());
        throw new InvalidOperationException(
            $"Invalid HttpTransportMode value '{rawValue}' at configuration key '{HttpTransportModeKey}'. " +
            $"Supported values: {validValues}.");
    }

    // ── RunspacePoolOptions resolution ────────────────────────────────────────────

    private static RunspacePoolOptions ResolveRunspacePoolOptions(IConfiguration configuration, ILogger logger)
    {
        // Start by binding the new McpServer:RunspacePool section. This handles all new keys
        // including those without legacy equivalents (StopTimeout, ShutdownDrainTimeout,
        // ReplenishCheckInterval). Properties absent from the section retain their defaults.
        var options = new RunspacePoolOptions();
        configuration.GetSection("McpServer:RunspacePool").Bind(options);

        // Per-key legacy fallback: emit warning for each deprecated key that is present,
        // and override only when the corresponding new key is absent.
        ApplyLegacyMaxPoolSize(configuration, logger, options);
        ApplyLegacyIdleTtl(configuration, logger, options);
        ApplyLegacySweepInterval(configuration, logger, options);
        ApplyLegacyWarmStandbyCount(configuration, logger, options);
        ApplyLegacyAcquisitionTimeout(configuration, logger, options);

        return options;
    }

    private static void ApplyLegacyMaxPoolSize(IConfiguration cfg, ILogger log, RunspacePoolOptions opts)
    {
        var legacyRaw = cfg[LegacySessionRunspaceCapacityKey];
        if (legacyRaw is null) return;

        if (!int.TryParse(legacyRaw, out var legacyValue)) return;

        WarnLegacyKey(log,
            legacyKey: LegacySessionRunspaceCapacityKey,
            newKey: RunspacePoolMaxPoolSizeKey,
            jsonExample: $"{{ \"McpServer\": {{ \"RunspacePool\": {{ \"MaxPoolSize\": {legacyValue} }} }} }}");

        if (cfg[RunspacePoolMaxPoolSizeKey] is null)
        {
            opts.MaxPoolSize = legacyValue;
        }
    }

    private static void ApplyLegacyIdleTtl(IConfiguration cfg, ILogger log, RunspacePoolOptions opts)
    {
        var legacyRaw = cfg[LegacySessionRunspaceIdleTtlSecondsKey];
        if (legacyRaw is null) return;

        if (!int.TryParse(legacyRaw, out var legacySeconds)) return;

        var legacySpan = TimeSpan.FromSeconds(legacySeconds);
        WarnLegacyKey(log,
            legacyKey: LegacySessionRunspaceIdleTtlSecondsKey,
            newKey: RunspacePoolIdleTtlKey,
            jsonExample: $"{{ \"McpServer\": {{ \"RunspacePool\": {{ \"IdleTtl\": \"{legacySpan:c}\" }} }} }}");

        if (cfg[RunspacePoolIdleTtlKey] is null)
        {
            opts.IdleTtl = legacySpan;
        }
    }

    private static void ApplyLegacySweepInterval(IConfiguration cfg, ILogger log, RunspacePoolOptions opts)
    {
        var legacyRaw = cfg[LegacySessionRunspaceSweepIntervalSecondsKey];
        if (legacyRaw is null) return;

        if (!int.TryParse(legacyRaw, out var legacySeconds)) return;

        var legacySpan = TimeSpan.FromSeconds(legacySeconds);
        WarnLegacyKey(log,
            legacyKey: LegacySessionRunspaceSweepIntervalSecondsKey,
            newKey: RunspacePoolSweepIntervalKey,
            jsonExample: $"{{ \"McpServer\": {{ \"RunspacePool\": {{ \"SweepInterval\": \"{legacySpan:c}\" }} }} }}");

        if (cfg[RunspacePoolSweepIntervalKey] is null)
        {
            opts.SweepInterval = legacySpan;
        }
    }

    private static void ApplyLegacyWarmStandbyCount(IConfiguration cfg, ILogger log, RunspacePoolOptions opts)
    {
        var legacyRaw = cfg[LegacySessionRunspaceWarmStandbyCountKey];
        if (legacyRaw is null) return;

        if (!int.TryParse(legacyRaw, out var legacyValue)) return;

        WarnLegacyKey(log,
            legacyKey: LegacySessionRunspaceWarmStandbyCountKey,
            newKey: $"{RunspacePoolMinPoolSizeKey} and {RunspacePoolEagerWarmCountKey}",
            jsonExample: $"{{ \"McpServer\": {{ \"RunspacePool\": {{ \"MinPoolSize\": {legacyValue}, \"EagerWarmCount\": {legacyValue} }} }} }}");

        if (cfg[RunspacePoolMinPoolSizeKey] is null)
        {
            opts.MinPoolSize = legacyValue;
        }

        if (cfg[RunspacePoolEagerWarmCountKey] is null)
        {
            opts.EagerWarmCount = legacyValue;
        }
    }

    private static void ApplyLegacyAcquisitionTimeout(IConfiguration cfg, ILogger log, RunspacePoolOptions opts)
    {
        var legacyRaw = cfg[LegacySessionRunspaceAcquisitionTimeoutSecondsKey];
        if (legacyRaw is null) return;

        if (!int.TryParse(legacyRaw, out var legacySeconds)) return;

        var legacySpan = TimeSpan.FromSeconds(legacySeconds);
        WarnLegacyKey(log,
            legacyKey: LegacySessionRunspaceAcquisitionTimeoutSecondsKey,
            newKey: RunspacePoolAcquisitionTimeoutKey,
            jsonExample: $"{{ \"McpServer\": {{ \"RunspacePool\": {{ \"AcquisitionTimeout\": \"{legacySpan:c}\" }} }} }}");

        if (cfg[RunspacePoolAcquisitionTimeoutKey] is null)
        {
            opts.AcquisitionTimeout = legacySpan;
        }
    }

    // ── Warning helper ────────────────────────────────────────────────────────────

    private static void WarnLegacyKey(ILogger logger, string legacyKey, string newKey, string jsonExample)
    {
        logger.LogWarning(
            "Deprecated configuration key '{LegacyKey}' is present. " +
            "Replace it with '{NewKey}'. " +
            "JSON migration: {JsonExample}." +
            RemovalPolicy +
            BehaviorNote,
            legacyKey,
            newKey,
            jsonExample);
    }
}
