using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell.Pool;
using PoshMcp.Tests.Shared;
using Xunit;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Startup-integration tests verifying that <see cref="HttpServerHost.RegisterResolvedMcpConfiguration"/>
/// is correctly wired into the real configuration path: resolver called exactly once, DI singletons
/// registered, legacy fallback applied, warnings emitted once, and invalid settings fail fast.
/// </summary>
/// <remarks>
/// Complements the pure-resolver unit tests in <see cref="McpServerConfigurationResolverTests"/>.
/// These tests verify the production startup call path and DI registration, not just the resolver logic.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class McpServerConfigurationStartupTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static IConfiguration BuildUserConfig(TempDirectory dir, string json, string filename = "appsettings.json")
    {
        var path = Path.Combine(dir.Path, filename);
        File.WriteAllText(path, json);
        return ConfigurationLoader.BuildRootConfiguration(path, reloadOnChange: false);
    }

    // ── Registration: new-only config ─────────────────────────────────────────────

    [Fact]
    public void RegisterResolvedMcpConfiguration_EmptyUserConfig_RegistersDefaultsNoWarnings()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        var logger = new CollectingLogger();

        HttpServerHost.RegisterResolvedMcpConfiguration(services, config, logger);
        var provider = services.BuildServiceProvider();
        var poolOpts = provider.GetRequiredService<RunspacePoolOptions>();

        Assert.Equal(2, poolOpts.MinPoolSize);
        Assert.Equal(16, poolOpts.MaxPoolSize);
        Assert.Equal(2, poolOpts.EagerWarmCount);
        Assert.Equal(TimeSpan.FromSeconds(15), poolOpts.AcquisitionTimeout);
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void RegisterResolvedMcpConfiguration_NewOnlyConfig_RegistersCorrectValuesNoWarnings()
    {
        using var dir = new TempDirectory("cfg-startup-new");
        var config = BuildUserConfig(dir, """
            {
              "McpServer": {
                "RunspacePool": { "MinPoolSize": 3, "MaxPoolSize": 20, "EagerWarmCount": 4 }
              }
            }
            """);
        var services = new ServiceCollection();
        var logger = new CollectingLogger();

        HttpServerHost.RegisterResolvedMcpConfiguration(services, config, logger);
        var provider = services.BuildServiceProvider();
        var poolOpts = provider.GetRequiredService<RunspacePoolOptions>();

        Assert.Equal(3, poolOpts.MinPoolSize);
        Assert.Equal(20, poolOpts.MaxPoolSize);
        Assert.Equal(4, poolOpts.EagerWarmCount);
        Assert.Empty(logger.Warnings);
    }

    // ── Registration: legacy-only config ─────────────────────────────────────────

    [Fact]
    public void RegisterResolvedMcpConfiguration_LegacyOnlyConfig_RegistersLegacyMappedValues_EmitsFiveWarnings()
    {
        using var dir = new TempDirectory("cfg-startup-legacy");
        var config = BuildUserConfig(dir, """
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
        var services = new ServiceCollection();
        var logger = new CollectingLogger();

        HttpServerHost.RegisterResolvedMcpConfiguration(services, config, logger);
        var provider = services.BuildServiceProvider();
        var poolOpts = provider.GetRequiredService<RunspacePoolOptions>();

        Assert.Equal(10, poolOpts.MaxPoolSize);
        Assert.Equal(TimeSpan.FromSeconds(120), poolOpts.IdleTtl);
        Assert.Equal(TimeSpan.FromSeconds(15), poolOpts.SweepInterval);
        Assert.Equal(3, poolOpts.MinPoolSize);
        Assert.Equal(3, poolOpts.EagerWarmCount);
        Assert.Equal(TimeSpan.FromSeconds(10), poolOpts.AcquisitionTimeout);
        Assert.Equal(5, logger.Warnings.Count);
    }

    // ── Registration: invalid config fails fast ───────────────────────────────────

    [Fact]
    public void RegisterResolvedMcpConfiguration_InvalidPoolConfig_ThrowsInvalidOperationException()
    {
        using var dir = new TempDirectory("cfg-startup-invalid");
        var config = BuildUserConfig(dir, """
            { "McpServer": { "RunspacePool": { "MinPoolSize": 20, "MaxPoolSize": 5 } } }
            """);
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            HttpServerHost.RegisterResolvedMcpConfiguration(services, config, new CollectingLogger()));

        Assert.Contains("MaxPoolSize", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Nothing registered after throw
        var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<RunspacePoolOptions>());
    }

    // ── Registration: singleton contract and DI identity ─────────────────────────

    [Fact]
    public void RegisterResolvedMcpConfiguration_ReturnedInstanceIsSameAsDiRegistration()
    {
        using var dir = new TempDirectory("cfg-startup-ref");
        var config = BuildUserConfig(dir, """{ "McpServer": { "RunspacePool": { "MaxPoolSize": 8 } } }""");
        var services = new ServiceCollection();

        var returned = HttpServerHost.RegisterResolvedMcpConfiguration(services, config, new CollectingLogger());
        var provider = services.BuildServiceProvider();
        var diResolved = provider.GetRequiredService<McpServerConfiguration>();

        Assert.Same(returned, diResolved);
    }

    [Fact]
    public void RegisterResolvedMcpConfiguration_MultipleGetService_ReturnsSameInstanceWarningEmittedOnce()
    {
        // Gap 8: resolver called once at startup; DI singleton reuse never re-runs the resolver.
        using var dir = new TempDirectory("cfg-startup-singleton");
        var config = BuildUserConfig(dir, """{ "McpServer": { "SessionRunspaceCapacity": 8 } }""");
        var services = new ServiceCollection();
        var logger = new CollectingLogger();

        HttpServerHost.RegisterResolvedMcpConfiguration(services, config, logger);
        var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<RunspacePoolOptions>();
        var second = provider.GetRequiredService<RunspacePoolOptions>();

        Assert.Same(first, second);
        Assert.Single(logger.Warnings); // resolver ran once; DI reuse does not re-emit warnings
    }

    // ── Registration: HttpTransportMode resolved and stored ───────────────────────

    [Fact]
    public void RegisterResolvedMcpConfiguration_StatefulTransportMode_RegisteredInMcpConfig()
    {
        using var dir = new TempDirectory("cfg-startup-mode");
        var config = BuildUserConfig(dir, """{ "McpServer": { "HttpTransportMode": "Stateful" } }""");
        var services = new ServiceCollection();

        var returned = HttpServerHost.RegisterResolvedMcpConfiguration(services, config, new CollectingLogger());

        Assert.Equal(HttpTransportMode.Stateful, returned.HttpTransportMode);
        var provider = services.BuildServiceProvider();
        Assert.Equal(HttpTransportMode.Stateful, provider.GetRequiredService<McpServerConfiguration>().HttpTransportMode);
    }

    // ── Registration: legacy properties preserved for existing session runspace ───

    [Fact]
    public void RegisterResolvedMcpConfiguration_LegacyConfig_LegacyPropertiesAccessibleForSessionRunspace()
    {
        // The session-affine runspace construction reads mcpServerConfig.SessionRunspaceCapacity etc.
        // These raw bound values must still be accessible after resolver integration.
        using var dir = new TempDirectory("cfg-startup-session");
        var config = BuildUserConfig(dir, """
            { "McpServer": { "SessionRunspaceCapacity": 24, "SessionRunspaceIdleTtlSeconds": 600 } }
            """);
        var services = new ServiceCollection();

        var returned = HttpServerHost.RegisterResolvedMcpConfiguration(services, config, new CollectingLogger());

        Assert.Equal(24, returned.SessionRunspaceCapacity);
        Assert.Equal(600, returned.SessionRunspaceIdleTtlSeconds);
        // Pool options also reflect resolved legacy mapping
        Assert.Equal(24, returned.RunspacePool.MaxPoolSize);
        Assert.Equal(TimeSpan.FromSeconds(600), returned.RunspacePool.IdleTtl);
    }

    // ── Registration: mixed config per-key precedence ─────────────────────────────

    [Fact]
    public void RegisterResolvedMcpConfiguration_MixedConfig_NewWinsPerField_WarningEmitted()
    {
        using var dir = new TempDirectory("cfg-startup-mixed");
        var config = BuildUserConfig(dir, """
            {
              "McpServer": {
                "SessionRunspaceCapacity": 99,
                "RunspacePool": { "MaxPoolSize": 32 }
              }
            }
            """);
        var services = new ServiceCollection();
        var logger = new CollectingLogger();

        var returned = HttpServerHost.RegisterResolvedMcpConfiguration(services, config, logger);

        Assert.Equal(32, returned.RunspacePool.MaxPoolSize);  // new key wins
        Assert.Single(logger.Warnings);                        // warning for legacy key presence
        Assert.Contains(McpServerConfigurationResolver.LegacySessionRunspaceCapacityKey,
            logger.Warnings[0], StringComparison.Ordinal);
    }

    // ── Numeric enum validation (Gap 4) ──────────────────────────────────────────

    [Fact]
    public void ResolveHttpTransportMode_NumericUndefinedValue_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("McpServer:HttpTransportMode", "99")])
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            McpServerConfigurationResolver.Resolve(config, new CollectingLogger()));

        Assert.Contains("99", ex.Message, StringComparison.Ordinal);
        Assert.Contains(McpServerConfigurationResolver.HttpTransportModeKey, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveHttpTransportMode_NumericTwoUndefined_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("McpServer:HttpTransportMode", "2")])
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            McpServerConfigurationResolver.Resolve(config, new CollectingLogger()));
    }

    [Fact]
    public void ResolveHttpTransportMode_NumericZero_AcceptedAsStateless()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("McpServer:HttpTransportMode", "0")])
            .Build();

        var (mode, _) = McpServerConfigurationResolver.Resolve(config, new CollectingLogger());

        Assert.Equal(HttpTransportMode.Stateless, mode);
    }

    [Fact]
    public void ResolveHttpTransportMode_NumericOne_AcceptedAsStateful()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("McpServer:HttpTransportMode", "1")])
            .Build();

        var (mode, _) = McpServerConfigurationResolver.Resolve(config, new CollectingLogger());

        Assert.Equal(HttpTransportMode.Stateful, mode);
    }

    // ── Provider ordering (Gap 5 — no env-var mutation, uses InMemoryCollection) ──

    [Fact]
    public void Resolve_ProviderOrdering_HigherPriorityNewKeyOverridesLowerPriorityNewKey()
    {
        // Simulates: user JSON file (lower priority) vs env var override (higher priority).
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("McpServer:RunspacePool:MaxPoolSize", "10")])
            .AddInMemoryCollection([new KeyValuePair<string, string?>("McpServer:RunspacePool:MaxPoolSize", "30")])
            .Build();
        var logger = new CollectingLogger();

        var (_, poolOpts) = McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Equal(30, poolOpts.MaxPoolSize);
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void Resolve_ProviderOrdering_HigherPriorityNewKeyOverridesLowerPriorityLegacyKey()
    {
        // Lower priority: legacy key. Higher priority: new key for same field.
        // Both keys present → new key wins; legacy warning still emitted because the key is present.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("McpServer:SessionRunspaceCapacity", "10")])
            .AddInMemoryCollection([new KeyValuePair<string, string?>("McpServer:RunspacePool:MaxPoolSize", "30")])
            .Build();
        var logger = new CollectingLogger();

        var (_, poolOpts) = McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Equal(30, poolOpts.MaxPoolSize);  // new key wins
        Assert.Single(logger.Warnings);           // legacy key present → warning
    }

    [Fact]
    public void Resolve_ProviderOrdering_LegacyKeyOnly_FallbackApplied_NoNewKeyPresent()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("McpServer:SessionRunspaceCapacity", "12")])
            .Build();
        var logger = new CollectingLogger();

        var (_, poolOpts) = McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Equal(12, poolOpts.MaxPoolSize);
        Assert.Single(logger.Warnings);
    }

    [Fact]
    public void Resolve_ProviderOrdering_MixedJsonPlusInMemoryOverride_CorrectPrecedence()
    {
        // Simulates: user JSON file has legacy key; environment variable sets new key.
        // New key (higher-priority source) wins; legacy key emits warning because it is present.
        using var dir = new TempDirectory("cfg-ordering-mixed");
        var config = new ConfigurationBuilder()
            .AddJsonFile(WriteJson(dir, """{ "McpServer": { "SessionRunspaceCapacity": 10 } }"""),
                optional: false, reloadOnChange: false)                // lower priority: JSON (legacy)
            .AddInMemoryCollection([new KeyValuePair<string, string?>(
                "McpServer:RunspacePool:MaxPoolSize", "40")])           // higher priority: override (new key)
            .Build();
        var logger = new CollectingLogger();

        var (_, poolOpts) = McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Equal(40, poolOpts.MaxPoolSize);  // high-priority new key wins
        Assert.Single(logger.Warnings);           // legacy key present → warning
    }

    // ── Layering regression: bundled defaults must not suppress legacy fallback ───

    [Fact]
    public void RegisterResolvedMcpConfiguration_BundledDefaultsNotInUserConfig_LegacyFallbackApplies()
    {
        // Production scenario: the user's appsettings file contains only a legacy key.
        // The server's bundled appsettings.json (McpServer:RunspacePool:MaxPoolSize: 16) lives in
        // builder.Configuration — not in authRootConfig (built from user file + env vars only).
        // Resolver receives authRootConfig → sees no new key → legacy fallback applies.
        using var dir = new TempDirectory("cfg-layering-isolation");
        var config = BuildUserConfig(dir, """{ "McpServer": { "SessionRunspaceCapacity": 7 } }""");
        var services = new ServiceCollection();
        var logger = new CollectingLogger();

        HttpServerHost.RegisterResolvedMcpConfiguration(services, config, logger);
        var provider = services.BuildServiceProvider();
        var poolOpts = provider.GetRequiredService<RunspacePoolOptions>();

        // Legacy fallback: MaxPoolSize = 7, not the bundled default 16.
        Assert.Equal(7, poolOpts.MaxPoolSize);
        Assert.Single(logger.Warnings);
    }

    [Fact]
    public void RegisterResolvedMcpConfiguration_UserConfigNewKeyPresent_BundledDefaultsIrrelevant()
    {
        // User config explicitly sets new key. No legacy fallback needed. No warnings.
        using var dir = new TempDirectory("cfg-layering-new");
        var config = BuildUserConfig(dir, """{ "McpServer": { "RunspacePool": { "MaxPoolSize": 24 } } }""");
        var services = new ServiceCollection();
        var logger = new CollectingLogger();

        HttpServerHost.RegisterResolvedMcpConfiguration(services, config, logger);
        var provider = services.BuildServiceProvider();

        Assert.Equal(24, provider.GetRequiredService<RunspacePoolOptions>().MaxPoolSize);
        Assert.Empty(logger.Warnings);
    }

    // ── Stdio isolation (Gap 9) ───────────────────────────────────────────────────

    [Fact]
    public void StdioServerHost_DoesNotExposeRegistrationHelper_StdioUnaffected()
    {
        // RegisterResolvedMcpConfiguration belongs to HttpServerHost only.
        // StdioServerHost must not have this method — stdio execution is outside the pool.
        var stdioMethods = typeof(StdioServerHost)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.DoesNotContain(stdioMethods,
            m => m.Name == nameof(HttpServerHost.RegisterResolvedMcpConfiguration));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static string WriteJson(TempDirectory dir, string json)
    {
        var path = Path.Combine(dir.Path, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    private sealed class CollectingLogger : ILogger
    {
        private readonly List<string> _warnings = [];
        public IReadOnlyList<string> Warnings => _warnings;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                _warnings.Add(formatter(state, exception));
        }
    }
}

// ── Environment variable provider tests (Gap 5, actual AddEnvironmentVariables() pipeline) ────

/// <summary>
/// Provider-realistic tests for environment variables using the same <see cref="ConfigurationBuilder"/>
/// pipeline as production (<see cref="ConfigurationLoader.BuildRootConfiguration"/>).
/// Env var names follow the .NET convention: <c>McpServer__Key__SubKey</c> = <c>McpServer:Key:SubKey</c>.
/// Isolated in <c>TransportSelectionTests</c> collection to prevent parallel env-var interference.
/// </summary>
[Collection("TransportSelectionTests")]
[Trait("Category", "Unit")]
public sealed class McpServerConfigurationEnvVarProviderTests
{
    private const string EnvTransportMode = "McpServer__HttpTransportMode";
    private const string EnvMaxPoolSize = "McpServer__RunspacePool__MaxPoolSize";
    private const string EnvLegacyCapacity = "McpServer__SessionRunspaceCapacity";

    [Fact]
    public void Resolve_EnvironmentVariable_HttpTransportModeStateful_CorrectlyResolved()
    {
        using var scope = new EnvironmentVariableScope(EnvTransportMode, "Stateful");
        var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();

        var (mode, _) = McpServerConfigurationResolver.Resolve(config, new CollectingLogger());

        Assert.Equal(HttpTransportMode.Stateful, mode);
    }

    [Fact]
    public void Resolve_EnvironmentVariable_RunspacePoolMaxPoolSize_OverridesDefault_NoWarning()
    {
        using var scope = new EnvironmentVariableScope(EnvMaxPoolSize, "30");
        var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var logger = new CollectingLogger();

        var (_, poolOpts) = McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Equal(30, poolOpts.MaxPoolSize);
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void Resolve_EnvironmentVariable_LegacyCapacity_EmitsWarning_FallbackApplied()
    {
        using var scope = new EnvironmentVariableScope(EnvLegacyCapacity, "11");
        var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var logger = new CollectingLogger();

        var (_, poolOpts) = McpServerConfigurationResolver.Resolve(config, logger);

        Assert.Equal(11, poolOpts.MaxPoolSize);
        Assert.Single(logger.Warnings);
        Assert.Contains(McpServerConfigurationResolver.LegacySessionRunspaceCapacityKey,
            logger.Warnings[0], StringComparison.Ordinal);
    }

    private sealed class CollectingLogger : ILogger
    {
        private readonly List<string> _warnings = [];
        public IReadOnlyList<string> Warnings => _warnings;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                _warnings.Add(formatter(state, exception));
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;
        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }
        public void Dispose() => Environment.SetEnvironmentVariable(_name, _originalValue);
    }
}
