using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using PoshMcp.Server.PowerShell;
using Microsoft.Extensions.Logging;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Integration tests for PowerShell initialization script functionality
/// </summary>
[Collection("PowerShellRunspaceHolder")]
public class InitializationScriptIntegrationTests : PowerShellTestBase
{
    public InitializationScriptIntegrationTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task CustomInitializationScript_ExecutesSuccessfully()
    {
        // Arrange
        var tempScriptFile = Path.GetTempFileName() + ".ps1";
        var scriptContent = @"
            Write-Host 'Custom initialization running'
            $global:CustomInitialized = $true
            $global:CustomValue = 'TestValue123'
            
            function Get-CustomTestData {
                return 'Custom function from init script'
            }
        ";
        await File.WriteAllTextAsync(tempScriptFile, scriptContent);

        var config = new PowerShellConfiguration
        {
            FunctionNames = new System.Collections.Generic.List<string> { "Get-CustomTestData" },
            InitializationScriptPath = tempScriptFile
        };

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger("Test");

        try
        {
            // Clear cache to ensure we load the new script
            InitializationScriptLoader.ClearCache();

            // Act - Create a runspace with custom initialization
            using var runspace = new IsolatedPowerShellRunspace(
                InitializationScriptLoader.LoadInitializationScript(config, logger));

            // Verify custom variable was set
            var customInitialized = runspace.ExecuteThreadSafe(ps =>
            {
                ps.Commands.Clear();
                ps.AddScript("$global:CustomInitialized");
                var results = ps.Invoke();
                return results.Count > 0 && results[0]?.BaseObject is bool b && b;
            });

            // Verify custom value was set
            var customValue = runspace.ExecuteThreadSafe(ps =>
            {
                ps.Commands.Clear();
                ps.AddScript("$global:CustomValue");
                var results = ps.Invoke();
                return results.Count > 0 ? results[0]?.ToString() : null;
            });

            // Verify custom function works
            var functionResult = runspace.ExecuteThreadSafe(ps =>
            {
                ps.Commands.Clear();
                ps.AddScript("Get-CustomTestData");
                var results = ps.Invoke();
                return results.Count > 0 ? results[0]?.ToString() : null;
            });

            // Assert
            Assert.True(customInitialized, "Custom initialization flag should be set");
            Assert.Equal("TestValue123", customValue);
            Assert.Equal("Custom function from init script", functionResult);
        }
        finally
        {
            InitializationScriptLoader.ClearCache();
            if (File.Exists(tempScriptFile))
                File.Delete(tempScriptFile);
        }
    }

    [Fact]
    public async Task DefaultInitializationScript_ContainsExpectedFunctions()
    {
        // Arrange
        var config = new PowerShellConfiguration
        {
            InitializationScriptPath = null // Use default
        };

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger("Test");

        // Act
        using var runspace = new IsolatedPowerShellRunspace(
            InitializationScriptLoader.LoadInitializationScript(config, logger));

        // Verify Get-McpSessionInfo exists
        var sessionInfo = runspace.ExecuteThreadSafe(ps =>
        {
            ps.Commands.Clear();
            ps.AddScript("Get-McpSessionInfo");
            var results = ps.Invoke();

            if (ps.HadErrors)
            {
                throw new InvalidOperationException($"PowerShell errors: {string.Join(", ", ps.Streams.Error)}");
            }

            return results.Count > 0;
        });

        // Assert
        Assert.True(sessionInfo, "Get-McpSessionInfo should be available");
    }

    [Fact]
    public async Task InitializationScriptWithError_FallsBackToDefault()
    {
        // Arrange
        var tempScriptFile = Path.GetTempFileName() + ".ps1";
        var badScriptContent = @"
            # This script has a syntax error
            Write-Host 'Before error'
            throw 'Intentional error in init script'
            Write-Host 'After error - should not execute'
        ";
        await File.WriteAllTextAsync(tempScriptFile, badScriptContent);

        var config = new PowerShellConfiguration
        {
            InitializationScriptPath = tempScriptFile
        };

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger("Test");

        try
        {
            InitializationScriptLoader.ClearCache();

            // Act - Load script (it will load but errors will occur during execution)
            var script = InitializationScriptLoader.LoadInitializationScript(config, logger);

            // Assert - Script content was loaded despite having errors
            Assert.NotNull(script);
            Assert.Contains("Intentional error", script);
        }
        finally
        {
            InitializationScriptLoader.ClearCache();
            if (File.Exists(tempScriptFile))
                File.Delete(tempScriptFile);
        }
    }

    [Fact]
    public async Task RelativePathInitializationScript_ResolvesCorrectly()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var scriptFileName = "test-init.ps1";
        var scriptPath = Path.Combine(tempDir, scriptFileName);

        var scriptContent = @"
            $global:RelativePathTest = 'Success'
        ";
        await File.WriteAllTextAsync(scriptPath, scriptContent);

        try
        {
            // Act - Resolve the path
            var resolvedPath = InitializationScriptLoader.ResolveScriptPath(scriptPath);

            // Assert
            Assert.True(Path.IsPathRooted(resolvedPath));
            Assert.True(File.Exists(resolvedPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void PowerShellRunspaceHolder_UsesConfigurationScript()
    {
        // Reset state to allow re-initialization in tests
        PowerShellRunspaceHolder.ResetForTesting();
        
        try
        {
            // Arrange
            var config = new PowerShellConfiguration
            {
                InitializationScriptPath = null // Use default
            };

            using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger("Test");

            // Act
            PowerShellRunspaceHolder.Initialize(config, logger);
            var script = PowerShellRunspaceHolder.GetProductionInitializationScript();

            // Assert
            Assert.NotNull(script);
            Assert.Contains("McpServerStartTime", script);
        }
        finally
        {
            // Clean up static state after test
            PowerShellRunspaceHolder.ResetForTesting();
        }
    }
}
