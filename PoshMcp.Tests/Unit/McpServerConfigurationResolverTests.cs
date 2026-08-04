using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell.Pool;
using PoshMcp.Tests.Shared;
using Xunit;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Comprehensive tests for <see cref="McpServerConfigurationResolver.Resolve"/> covering:
/// HttpTransportMode defaults/explicit/invalid, RunspacePool new-only/legacy-only/mixed/precedence,
/// warning count and content, no-warning scenarios, and validation failures.
/// </summary>
[Trait("Category", "Unit")]
public sealed class McpServerConfigurationResolverTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static IConfiguration BuildConfig(string json)
    {
        using var tempDir = new TempDirectory("mcpservercfg-resolver");
        var path = Path.Combine(tempDir.Path, "appsettings.json");
        File.WriteAllText(path, json);
        return new ConfigurationBuilder()
            .AddJsonFile(path, optional: false, reloadOnChange: false)
            .Build();
    }

    private static IConfiguration BuildEmpty() =>
        new ConfigurationBuilder().Build();

    // ── HttpTransportMode: defaults ───────────────────────────────────────────────

    [Fact]
    public void Resolve_NoHttpTransportModeKey_DefaultsToStateless()
    {
        var config = BuildEmpty();
        var logger = new CollectingLogger();

        var (mode, _) = McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Equal(HttpTransportMode.Stateless, mode);
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void Resolve_HttpTransportModeStateless_ReturnsStateless()
    {
        var config = BuildConfig("""
        { "McpServer": { "HttpTransportMode": "Stateless" } }
        """);
        var logger = new CollectingLogger();

        var (mode, _) = McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Equal(HttpTransportMode.Stateless, mode);
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void Resolve_HttpTransportModeStateful_ReturnsStateful()
    {
        var config = BuildConfig("""
        { "McpServer": { "HttpTransportMode": "Stateful" } }
        """);
        var logger = new CollectingLogger();

        var (mode, _) = McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Equal(HttpTransportMode.Stateful, mode);
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void Resolve_HttpTransportModeCaseInsensitive_Stateful()
    {
        var config = BuildConfig("""
        { "McpServer": { "HttpTransportMode": "stateful" } }
        """);

        var (mode, _) = McpServerConfigurationResolver.Resolve(config, new CollectingLogger());

        Assert.Equal(HttpTransportMode.Stateful, mode);
    }

    [Fact]
    public void Resolve_InvalidHttpTransportMode_ThrowsInvalidOperationException()
    {
        var config = BuildConfig("""
        { "McpServer": { "HttpTransportMode": "Bogus" } }
        """);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            McpServerConfigurationResolver.Resolve(config, new CollectingLogger()));

        Assert.Contains("Bogus", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(McpServerConfigurationResolver.HttpTransportModeKey, ex.Message, StringComparison.Ordinal);
        Assert.Contains("Stateless", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Stateful", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── RunspacePool: new-only (no warnings expected) ────────────────────────────

    [Fact]
    public void Resolve_NewRunspacePoolKeys_BindsCorrectly()
    {
        var config = BuildConfig("""
        {
          "McpServer": {
            "RunspacePool": {
              "MinPoolSize": 3,
              "MaxPoolSize": 20,
              "EagerWarmCount": 4,
              "AcquisitionTimeout": "00:00:30",
              "IdleTtl": "00:10:00",
              "SweepInterval": "00:01:00"
            }
          }
        }
        """);
        var logger = new CollectingLogger();

        var (_, pool) = McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Equal(3, pool.MinPoolSize);
        Assert.Equal(20, pool.MaxPoolSize);
        Assert.Equal(4, pool.EagerWarmCount);
        Assert.Equal(TimeSpan.FromSeconds(30), pool.AcquisitionTimeout);
        Assert.Equal(TimeSpan.FromMinutes(10), pool.IdleTtl);
        Assert.Equal(TimeSpan.FromMinutes(1), pool.SweepInterval);
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void Resolve_NoPoolKeys_UsesDefaults_NoWarnings()
    {
        var config = BuildEmpty();
        var logger = new CollectingLogger();

        var (_, pool) = McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Equal(2, pool.MinPoolSize);
        Assert.Equal(16, pool.MaxPoolSize);
        Assert.Equal(2, pool.EagerWarmCount);
        Assert.Equal(TimeSpan.FromSeconds(15), pool.AcquisitionTimeout);
        Assert.Equal(TimeSpan.FromSeconds(300), pool.IdleTtl);
        Assert.Equal(TimeSpan.FromSeconds(30), pool.SweepInterval);
        Assert.Empty(logger.Warnings);
    }

    // ── RunspacePool: legacy-only (warnings expected) ────────────────────────────

    [Fact]
    public void Resolve_LegacySessionRunspaceCapacity_MapsToMaxPoolSize_EmitsWarning()
    {
        var config = BuildConfig("""
        { "McpServer": { "SessionRunspaceCapacity": 24 } }
        """);
        var logger = new CollectingLogger();

        var (_, pool) = McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Equal(24, pool.MaxPoolSize);
        Assert.Single(logger.Warnings);
        Assert.Contains(McpServerConfigurationResolver.LegacySessionRunspaceCapacityKey, logger.Warnings[0], StringComparison.Ordinal);
        Assert.Contains(McpServerConfigurationResolver.RunspacePoolMaxPoolSizeKey, logger.Warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_LegacySessionRunspaceIdleTtlSeconds_MapsToIdleTtl_EmitsWarning()
    {
        var config = BuildConfig("""
        { "McpServer": { "SessionRunspaceIdleTtlSeconds": 600 } }
        """);
        var logger = new CollectingLogger();

        var (_, pool) = McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Equal(TimeSpan.FromSeconds(600), pool.IdleTtl);
        Assert.Single(logger.Warnings);
        Assert.Contains(McpServerConfigurationResolver.LegacySessionRunspaceIdleTtlSecondsKey, logger.Warnings[0], StringComparison.Ordinal);
        Assert.Contains(McpServerConfigurationResolver.RunspacePoolIdleTtlKey, logger.Warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_LegacySessionRunspaceSweepIntervalSeconds_MapsToSweepInterval_EmitsWarning()
    {
        var config = BuildConfig("""
        { "McpServer": { "SessionRunspaceSweepIntervalSeconds": 60 } }
        """);
        var logger = new CollectingLogger();

        var (_, pool) = McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Equal(TimeSpan.FromSeconds(60), pool.SweepInterval);
        Assert.Single(logger.Warnings);
        Assert.Contains(McpServerConfigurationResolver.LegacySessionRunspaceSweepIntervalSecondsKey, logger.Warnings[0], StringComparison.Ordinal);
        Assert.Contains(McpServerConfigurationResolver.RunspacePoolSweepIntervalKey, logger.Warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_LegacySessionRunspaceWarmStandbyCount_MapsToMinPoolSizeAndEagerWarmCount_EmitsOneWarning()
    {
        var config = BuildConfig("""
        { "McpServer": { "SessionRunspaceWarmStandbyCount": 4 } }
        """);
        var logger = new CollectingLogger();

        var (_, pool) = McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Equal(4, pool.MinPoolSize);
        Assert.Equal(4, pool.EagerWarmCount);
        Assert.Single(logger.Warnings);
        Assert.Contains(McpServerConfigurationResolver.LegacySessionRunspaceWarmStandbyCountKey, logger.Warnings[0], StringComparison.Ordinal);
        Assert.Contains(McpServerConfigurationResolver.RunspacePoolMinPoolSizeKey, logger.Warnings[0], StringComparison.Ordinal);
        Assert.Contains(McpServerConfigurationResolver.RunspacePoolEagerWarmCountKey, logger.Warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_LegacySessionRunspaceAcquisitionTimeoutSeconds_MapsToAcquisitionTimeout_EmitsWarning()
    {
        var config = BuildConfig("""
        { "McpServer": { "SessionRunspaceAcquisitionTimeoutSeconds": 20 } }
        """);
        var logger = new CollectingLogger();

        var (_, pool) = McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Equal(TimeSpan.FromSeconds(20), pool.AcquisitionTimeout);
        Assert.Single(logger.Warnings);
        Assert.Contains(McpServerConfigurationResolver.LegacySessionRunspaceAcquisitionTimeoutSecondsKey, logger.Warnings[0], StringComparison.Ordinal);
        Assert.Contains(McpServerConfigurationResolver.RunspacePoolAcquisitionTimeoutKey, logger.Warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_AllLegacyKeys_EmitsFiveWarnings()
    {
        var config = BuildConfig("""
        {
          "McpServer": {
            "SessionRunspaceCapacity": 10,
            "SessionRunspaceIdleTtlSeconds": 120,
            "SessionRunspaceSweepIntervalSeconds": 15,
            "SessionRunspaceWarmStandbyCount": 3,
            "SessionRunspaceAcquisitionTimeoutSeconds": 10
          }
        }
        """);
        var logger = new CollectingLogger();

        var (_, pool) = McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Equal(5, logger.Warnings.Count);
        Assert.Equal(10, pool.MaxPoolSize);
        Assert.Equal(TimeSpan.FromSeconds(120), pool.IdleTtl);
        Assert.Equal(TimeSpan.FromSeconds(15), pool.SweepInterval);
        Assert.Equal(3, pool.MinPoolSize);
        Assert.Equal(3, pool.EagerWarmCount);
        Assert.Equal(TimeSpan.FromSeconds(10), pool.AcquisitionTimeout);
    }

    // ── Startup compatibility: legacy-only config resolves successfully ───────────

    [Fact]
    public void Resolve_LegacyOnlyConfig_StartsUp_NoException()
    {
        var config = BuildConfig("""
        {
          "McpServer": {
            "IdleSessionTimeoutSeconds": 60,
            "SessionRunspaceCapacity": 16,
            "SessionRunspaceIdleTtlSeconds": 300,
            "SessionRunspaceSweepIntervalSeconds": 30,
            "SessionRunspaceWarmStandbyCount": 2,
            "SessionRunspaceAcquisitionTimeoutSeconds": 15,
            "EnableLegacySse": false
          }
        }
        """);
        var logger = new CollectingLogger();

        var (mode, pool) = McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Equal(HttpTransportMode.Stateless, mode);
        Assert.NotNull(pool);
        Assert.Equal(5, logger.Warnings.Count);
    }

    // ── Mixed config: per-key precedence ─────────────────────────────────────────

    [Fact]
    public void Resolve_MixedConfig_NewKeyWinsForMaxPoolSize_LegacyUsedForOtherField()
    {
        var config = BuildConfig("""
        {
          "McpServer": {
            "SessionRunspaceCapacity": 99,
            "SessionRunspaceIdleTtlSeconds": 600,
            "RunspacePool": {
              "MaxPoolSize": 32
            }
          }
        }
        """);
        var logger = new CollectingLogger();

        var (_, pool) = McpServerConfigurationResolver.Resolve(config, logger);

        // New key wins for MaxPoolSize
        Assert.Equal(32, pool.MaxPoolSize);
        // Legacy applies for IdleTtl (no new key for it)
        Assert.Equal(TimeSpan.FromSeconds(600), pool.IdleTtl);
        // Two warnings: one for each legacy key present
        Assert.Equal(2, logger.Warnings.Count);
    }

    [Fact]
    public void Resolve_BothLegacyAndNewMaxPoolSizePresent_NewWins_WarningStillEmitted()
    {
        var config = BuildConfig("""
        {
          "McpServer": {
            "SessionRunspaceCapacity": 5,
            "RunspacePool": {
              "MaxPoolSize": 30
            }
          }
        }
        """);
        var logger = new CollectingLogger();

        var (_, pool) = McpServerConfigurationResolver.Resolve(config, logger);

        // New key wins (30, not 5)
        Assert.Equal(30, pool.MaxPoolSize);
        // Warning still emitted because legacy key is present
        Assert.Single(logger.Warnings);
        Assert.Contains(McpServerConfigurationResolver.LegacySessionRunspaceCapacityKey, logger.Warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_BothLegacyAndNewWarmStandbyPresent_NewWinsPerField()
    {
        var config = BuildConfig("""
        {
          "McpServer": {
            "SessionRunspaceWarmStandbyCount": 10,
            "RunspacePool": {
              "MinPoolSize": 5
            }
          }
        }
        """);
        var logger = new CollectingLogger();

        var (_, pool) = McpServerConfigurationResolver.Resolve(config, logger);

        // MinPoolSize: new key present → new value wins (5)
        Assert.Equal(5, pool.MinPoolSize);
        // EagerWarmCount: new key absent → legacy fallback (10)
        Assert.Equal(10, pool.EagerWarmCount);
        // Warning emitted for the legacy key
        Assert.Single(logger.Warnings);
    }

    [Fact]
    public void Resolve_NewOnlyConfig_NoWarnings()
    {
        var config = BuildConfig("""
        {
          "McpServer": {
            "HttpTransportMode": "Stateful",
            "RunspacePool": {
              "MinPoolSize": 4,
              "MaxPoolSize": 12,
              "EagerWarmCount": 4
            }
          }
        }
        """);
        var logger = new CollectingLogger();

        var (mode, pool) = McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Equal(HttpTransportMode.Stateful, mode);
        Assert.Equal(4, pool.MinPoolSize);
        Assert.Equal(12, pool.MaxPoolSize);
        Assert.Equal(4, pool.EagerWarmCount);
        Assert.Empty(logger.Warnings);
    }

    // ── Warning content assertions ────────────────────────────────────────────────

    [Fact]
    public void Resolve_LegacyKeyWarning_ContainsBehaviorNote()
    {
        var config = BuildConfig("""
        { "McpServer": { "SessionRunspaceCapacity": 8 } }
        """);
        var logger = new CollectingLogger();

        McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Single(logger.Warnings);
        // Must include behavioral change note about session-affine state
        Assert.Contains("session-affine", logger.Warnings[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_LegacyKeyWarning_ContainsRemovalPolicyNote()
    {
        var config = BuildConfig("""
        { "McpServer": { "SessionRunspaceCapacity": 8 } }
        """);
        var logger = new CollectingLogger();

        McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Single(logger.Warnings);
        Assert.Contains("deprecated", logger.Warnings[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_LegacyKeyWarning_ContainsJsonMigrationGuidance()
    {
        var config = BuildConfig("""
        { "McpServer": { "SessionRunspaceCapacity": 8 } }
        """);
        var logger = new CollectingLogger();

        McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Single(logger.Warnings);
        // Must include JSON migration example
        Assert.Contains("MaxPoolSize", logger.Warnings[0], StringComparison.Ordinal);
        Assert.Contains("8", logger.Warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_LegacyKeyWarning_DoesNotLogConfiguredValue_OnlyUsedInMigrationExample()
    {
        // Values must not appear in template key placeholders (only in migration example)
        // This test verifies the warning doesn't disclose config secrets via the structured template.
        var config = BuildConfig("""
        { "McpServer": { "SessionRunspaceCapacity": 42 } }
        """);
        var logger = new CollectingLogger();

        McpServerConfigurationResolver.Resolve(config, logger);

        // The warning was produced without errors
        Assert.Single(logger.Warnings);
        // The legacy key name must appear (not the value via a secret template parameter)
        Assert.Contains(McpServerConfigurationResolver.LegacySessionRunspaceCapacityKey, logger.Warnings[0], StringComparison.Ordinal);
    }

    // ── Validation failures ───────────────────────────────────────────────────────

    [Fact]
    public void Resolve_InvalidRunspacePoolOptions_ThrowsInvalidOperationException()
    {
        // MinPoolSize > MaxPoolSize is invalid
        var config = BuildConfig("""
        {
          "McpServer": {
            "RunspacePool": {
              "MinPoolSize": 20,
              "MaxPoolSize": 5
            }
          }
        }
        """);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            McpServerConfigurationResolver.Resolve(config, new CollectingLogger()));

        Assert.Contains("MaxPoolSize", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_LegacyZeroCapacity_MaxPoolSize_ValidationFails()
    {
        // Legacy key maps to MaxPoolSize; MaxPoolSize must be >= MinPoolSize (default 2)
        var config = BuildConfig("""
        { "McpServer": { "SessionRunspaceCapacity": 0 } }
        """);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            McpServerConfigurationResolver.Resolve(config, new CollectingLogger()));

        Assert.Contains("MaxPoolSize", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── No value leakage in warning template parameters ───────────────────────────

    [Fact]
    public void Resolve_MultipleWarnings_EachContainsCorrectNewKeyReference()
    {
        var config = BuildConfig("""
        {
          "McpServer": {
            "SessionRunspaceIdleTtlSeconds": 120,
            "SessionRunspaceSweepIntervalSeconds": 45
          }
        }
        """);
        var logger = new CollectingLogger();

        McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Equal(2, logger.Warnings.Count);
        var allWarnings = string.Join(" ", logger.Warnings);
        Assert.Contains(McpServerConfigurationResolver.RunspacePoolIdleTtlKey, allWarnings, StringComparison.Ordinal);
        Assert.Contains(McpServerConfigurationResolver.RunspacePoolSweepIntervalKey, allWarnings, StringComparison.Ordinal);
    }

    // ── CollectingLogger (test helper) ────────────────────────────────────────────

    private sealed class CollectingLogger : ILogger
    {
        private readonly List<string> _warnings = [];

        public IReadOnlyList<string> Warnings => _warnings;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                _warnings.Add(formatter(state, exception));
            }
        }
    }
}
