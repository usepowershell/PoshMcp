using Microsoft.Extensions.Logging;
using System;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Unit.PowerShell;

/// <summary>
/// Unit tests for InitializationScriptLoader
/// </summary>
public class InitializationScriptLoaderTests : PowerShellTestBase
{
    public InitializationScriptLoaderTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void LoadInitializationScript_WithNullConfig_ReturnsDefaultScript()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger("Test");

        // Act
        var result = Server.PowerShell.InitializationScriptLoader.LoadInitializationScript(null!, logger);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("McpServerStartTime", result);
        Assert.Contains("Get-McpSessionInfo", result);
    }

    [Fact]
    public void LoadInitializationScript_WithNoScriptPath_ReturnsDefaultScript()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger("Test");
        var config = new Server.PowerShell.PowerShellConfiguration
        {
            InitializationScriptPath = null
        };

        // Act
        var result = Server.PowerShell.InitializationScriptLoader.LoadInitializationScript(config, logger);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("McpServerStartTime", result);
    }

    [Fact]
    public void LoadInitializationScript_WithValidScriptPath_LoadsScriptContent()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var scriptContent = @"
            Write-Host 'Custom initialization script'
            function Get-CustomData { return 'custom' }
        ";
        File.WriteAllText(tempFile, scriptContent);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger("Test");
        var config = new Server.PowerShell.PowerShellConfiguration
        {
            InitializationScriptPath = tempFile
        };

        try
        {
            // Act
            var result = Server.PowerShell.InitializationScriptLoader.LoadInitializationScript(config, logger);

            // Assert
            Assert.Equal(scriptContent, result);
            Assert.Contains("Custom initialization script", result);
            Assert.Contains("Get-CustomData", result);
        }
        finally
        {
            Server.PowerShell.InitializationScriptLoader.ClearCache();
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadInitializationScript_WithNonExistentFile_ReturnsDefaultScript()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger("Test");
        var config = new Server.PowerShell.PowerShellConfiguration
        {
            InitializationScriptPath = "/nonexistent/path/script.ps1"
        };

        // Act
        var result = Server.PowerShell.InitializationScriptLoader.LoadInitializationScript(config, logger);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("McpServerStartTime", result); // Default script content
    }

    [Fact]
    public void LoadInitializationScript_WithEmptyScriptFile_ReturnsDefaultScript()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "   "); // Whitespace only

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger("Test");
        var config = new Server.PowerShell.PowerShellConfiguration
        {
            InitializationScriptPath = tempFile
        };

        try
        {
            // Act
            var result = Server.PowerShell.InitializationScriptLoader.LoadInitializationScript(config, logger);

            // Assert
            Assert.NotNull(result);
            Assert.Contains("McpServerStartTime", result); // Default script content
        }
        finally
        {
            Server.PowerShell.InitializationScriptLoader.ClearCache();
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadInitializationScript_CachesScriptContent()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var scriptContent = "Write-Host 'Test'";
        File.WriteAllText(tempFile, scriptContent);

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger("Test");
        var config = new Server.PowerShell.PowerShellConfiguration
        {
            InitializationScriptPath = tempFile
        };

        try
        {
            // Act - Load first time
            var result1 = Server.PowerShell.InitializationScriptLoader.LoadInitializationScript(config, logger);

            // Modify file
            File.WriteAllText(tempFile, "Write-Host 'Modified'");

            // Load second time - should get cached version
            var result2 = Server.PowerShell.InitializationScriptLoader.LoadInitializationScript(config, logger);

            // Assert
            Assert.Equal(result1, result2);
            Assert.Equal(scriptContent, result2); // Should still be original content
        }
        finally
        {
            Server.PowerShell.InitializationScriptLoader.ClearCache();
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void ResolveScriptPath_WithAbsolutePath_ReturnsUnchanged()
    {
        // Arrange
        var absolutePath = "/absolute/path/to/script.ps1";

        // Act
        var result = Server.PowerShell.InitializationScriptLoader.ResolveScriptPath(absolutePath);

        // Assert
        Assert.Equal(absolutePath, result);
    }

    [Fact]
    public void ResolveScriptPath_WithRelativePath_ReturnsAbsolutePath()
    {
        // Arrange
        var relativePath = "scripts/init.ps1";

        // Act
        var result = Server.PowerShell.InitializationScriptLoader.ResolveScriptPath(relativePath);

        // Assert
        Assert.True(Path.IsPathRooted(result));
        Assert.EndsWith("scripts/init.ps1", result.Replace('\\', '/'));
    }

    [Fact]
    public void ResolveScriptPath_WithNullPath_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            Server.PowerShell.InitializationScriptLoader.ResolveScriptPath(null!));
    }

    [Fact]
    public void ResolveScriptPath_WithEmptyPath_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            Server.PowerShell.InitializationScriptLoader.ResolveScriptPath(""));
    }

    [Fact]
    public void GetDefaultInitializationScript_ReturnsValidScript()
    {
        // Act
        var result = Server.PowerShell.InitializationScriptLoader.GetDefaultInitializationScript();

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains("McpServerStartTime", result);
        Assert.Contains("Get-McpSessionInfo", result);
        Assert.Contains("Get-SomeData", result);
    }

    [Fact]
    public void ClearCache_AllowsReloadingScript()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "Write-Host 'Original'");

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger("Test");
        var config = new Server.PowerShell.PowerShellConfiguration
        {
            InitializationScriptPath = tempFile
        };

        try
        {
            // Load first time
            var result1 = Server.PowerShell.InitializationScriptLoader.LoadInitializationScript(config, logger);

            // Modify file and clear cache
            File.WriteAllText(tempFile, "Write-Host 'Modified'");
            Server.PowerShell.InitializationScriptLoader.ClearCache();

            // Load again - should get new content
            var result2 = Server.PowerShell.InitializationScriptLoader.LoadInitializationScript(config, logger);

            // Assert
            Assert.NotEqual(result1, result2);
            Assert.Contains("Original", result1);
            Assert.Contains("Modified", result2);
        }
        finally
        {
            Server.PowerShell.InitializationScriptLoader.ClearCache();
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
