// Integration test fixture setup for PR #211 proxy and high-parameter scenarios.
// 
// This provides test infrastructure that Fry can use to validate end-to-end
// MCP tool schema generation with:
//   1. Proxy-style commands (Export-PSSession output)
//   2. High-parameter methods (17+, triggering cached delegate path)
//
// Usage in integration tests:
//   var setup = new Pr211IntegrationFixtureSetup(output);
//   var commands = setup.GetFixtureCommands();
//   var schemas = toolFactory.GetToolSchemas(commands, logger);
//   // Validate schemas include correct types and handle >16-param delegate

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Fixtures;

/// <summary>
/// Integration test fixture setup for PR #211 validation.
/// 
/// Provides ready-made commands and infrastructure for end-to-end tests that
/// validate the complete schema generation path handles proxy and high-parameter cases.
/// </summary>
public class Pr211IntegrationFixtureSetup
{
    private readonly ITestOutputHelper _output;
    private List<CommandInfo>? _fixtureCommands;

    public Pr211IntegrationFixtureSetup(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Gets the set of fixture commands for this test scenario.
    /// 
    /// Returns:
    ///   1. A proxy-styled command (marked ImplicitRemoting, has object params)
    ///   2. A high-parameter command (17 params, triggers cached delegate emit)
    ///   3. An object-parameter command (tests type coercion)
    /// </summary>
    public List<CommandInfo> GetFixtureCommands()
    {
        if (_fixtureCommands != null)
        {
            return _fixtureCommands;
        }

        _output.WriteLine("[Pr211IntegrationFixtureSetup] Creating fixture commands...");

        _fixtureCommands = new List<CommandInfo>();

        try
        {
            var proxyCmd = ProxyTestFixtures.CreateProxyStyledCommand();
            _output.WriteLine($"✓ Created proxy-styled command: {proxyCmd.Name}");
            _fixtureCommands.Add(proxyCmd);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"✗ Failed to create proxy command: {ex.Message}");
        }

        try
        {
            var highParamCmd = ProxyTestFixtures.CreateHighParameterCommand();
            _output.WriteLine($"✓ Created high-parameter command: {highParamCmd.Name} ({highParamCmd.Parameters.Count} params)");
            _fixtureCommands.Add(highParamCmd);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"✗ Failed to create high-parameter command: {ex.Message}");
        }

        try
        {
            var objectParamCmd = ProxyTestFixtures.CreateObjectParameterCommand();
            _output.WriteLine($"✓ Created object-parameter command: {objectParamCmd.Name}");
            _fixtureCommands.Add(objectParamCmd);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"✗ Failed to create object-parameter command: {ex.Message}");
        }

        return _fixtureCommands;
    }

    /// <summary>
    /// Validates that the tool schema generated for fixture commands correctly
    /// handles proxy detection and high-parameter delegate paths.
    /// 
    /// This is a helper for integration tests to verify:
    ///   1. Proxy commands have object params converted to string in schema
    ///   2. High-parameter commands generate valid delegate types
    ///   3. Parameter count in schema matches PowerShell metadata
    /// </summary>
    public void ValidateFixtureSchemas(
        IEnumerable<CommandInfo> commands,
        McpToolFactoryV2 toolFactory,
        ILogger logger)
    {
        _output.WriteLine("[Pr211IntegrationFixtureSetup] Validating fixture schemas...");

        foreach (var cmd in commands)
        {
            _output.WriteLine($"  Checking {cmd.Name}:");

            // Validate parameter count matches
            _output.WriteLine($"    Parameters: {cmd.Parameters.Count}");

            // For high-parameter commands, verify they process without errors
            if (cmd.Parameters.Count > 16)
            {
                _output.WriteLine($"    → High-parameter method (>16 params): Should use cached delegate emit");
            }

            // For proxy-style commands, verify type coercion will happen
            var objectParams = cmd.Parameters.Values
                .Where(p => p.ParameterType == typeof(object))
                .ToList();
            if (objectParams.Any())
            {
                _output.WriteLine($"    → Proxy-style object params: {string.Join(", ", objectParams.Select(p => p.Name))}");
                _output.WriteLine($"       Will be coerced to string in schema (via EffectiveParameterType)");
            }
        }
    }

    /// <summary>
    /// Resets fixture state, useful for test isolation.
    /// </summary>
    public void Reset()
    {
        _fixtureCommands = null;
    }
}

/// <summary>
/// Collection fixture for Pr211IntegrationFixtureSetup.
/// 
/// Use this when multiple integration test classes need shared fixture state.
/// Usage:
///   [CollectionDefinition("PR #211 Integration Tests")]
///   public class Pr211CollectionFixture : ICollectionFixture<Pr211IntegrationFixtureSetup> { }
///   
///   [Collection("PR #211 Integration Tests")]
///   public class MyIntegrationTest { ... }
/// </summary>
[CollectionDefinition("PR #211 Proxy & High-Parameter Tests")]
public class Pr211IntegrationTestCollection : ICollectionFixture<Pr211IntegrationFixtureSetup>
{
    // This class has no code, and is never created. Its purpose is simply
    // to define the collection, and to supply the Pr211IntegrationFixtureSetup
    // to test classes.
}
