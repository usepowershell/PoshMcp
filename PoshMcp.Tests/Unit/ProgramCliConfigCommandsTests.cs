using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Collection("TransportSelectionTests")]
public class ProgramCliConfigCommandsTests
{
    [Fact]
    public async Task CreateConfigCommand_CreatesDefaultConfigurationInCurrentDirectory()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var currentDirectoryScope = new CurrentDirectoryScope(tempDirectory.Path);
        using var capture = new ConsoleCapture();

        var result = await Program.Main(new[] { "create-config", "--format", "json" });

        Assert.Equal(0, result);

        var configPath = System.IO.Path.Combine(tempDirectory.Path, "appsettings.json");
        Assert.True(File.Exists(configPath));

        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        Assert.True(payload!["success"]?.GetValue<bool>());

        var configJson = JsonNode.Parse(await File.ReadAllTextAsync(configPath))?.AsObject();
        Assert.NotNull(configJson);
        Assert.NotNull(configJson!["PowerShellConfiguration"]);
    }

    [Fact]
    public async Task UpdateConfigCommand_WithNonInteractive_UpdatesResolvedCurrentDirectoryConfig()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var currentDirectoryScope = new CurrentDirectoryScope(tempDirectory.Path);
        using var capture = new ConsoleCapture();

        var configPath = System.IO.Path.Combine(tempDirectory.Path, "appsettings.json");
        await File.WriteAllTextAsync(configPath, @"{
  ""PowerShellConfiguration"": {
    ""CommandNames"": [""Get-Process""],
    ""Modules"": [],
    ""IncludePatterns"": [],
    ""ExcludePatterns"": []
  }
}");

        var result = await Program.Main(new[]
        {
            "update-config",
            "--non-interactive",
            "--add-command", "Get-Date",
            "--remove-command", "Get-Process",
            "--add-module", "Pester",
            "--enable-dynamic-reload-tools", "true",
            "--format", "json"
        });

        Assert.Equal(0, result);

        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        Assert.True(payload!["changed"]?.GetValue<bool>());
        Assert.Equal(1, payload["addedCommands"]?.GetValue<int>());
        Assert.Equal(1, payload["removedCommands"]?.GetValue<int>());

        var updatedRoot = JsonNode.Parse(await File.ReadAllTextAsync(configPath))?.AsObject();
        Assert.NotNull(updatedRoot);

        var powerShellConfiguration = updatedRoot!["PowerShellConfiguration"]?.AsObject();
        Assert.NotNull(powerShellConfiguration);

        var commandNames = powerShellConfiguration!["CommandNames"]?.AsArray();
        Assert.NotNull(commandNames);
        Assert.Contains(commandNames!, item => string.Equals(item?.GetValue<string>(), "Get-Date", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(commandNames!, item => string.Equals(item?.GetValue<string>(), "Get-Process", StringComparison.OrdinalIgnoreCase));

        var modules = powerShellConfiguration["Modules"]?.AsArray();
        Assert.NotNull(modules);
        Assert.Contains(modules!, item => string.Equals(item?.GetValue<string>(), "Pester", StringComparison.OrdinalIgnoreCase));

        Assert.True(powerShellConfiguration["EnableDynamicReloadTools"]?.GetValue<bool>());
    }

    [Fact]
    public async Task UpdateConfigCommand_WhenAddingCommand_InteractivePromptsCanSetAdvancedOverrides()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var currentDirectoryScope = new CurrentDirectoryScope(tempDirectory.Path);
        using var capture = new ConsoleCapture("y\ntrue\nfalse\nId,Name\n\n\n");

        var configPath = System.IO.Path.Combine(tempDirectory.Path, "appsettings.json");
        await File.WriteAllTextAsync(configPath, @"{
  ""PowerShellConfiguration"": {
    ""FunctionNames"": [],
    ""Modules"": [],
    ""IncludePatterns"": [],
    ""ExcludePatterns"": []
  }
}");

        var result = await Program.Main(new[]
        {
            "update-config",
            "--add-command", "Get-Process",
            "--format", "json"
        });

        Assert.Equal(0, result);

        var updatedRoot = JsonNode.Parse(await File.ReadAllTextAsync(configPath))?.AsObject();
        Assert.NotNull(updatedRoot);

        var functionOverride = updatedRoot!["PowerShellConfiguration"]?["CommandOverrides"]?["Get-Process"]?.AsObject();
        Assert.NotNull(functionOverride);
        Assert.True(functionOverride!["EnableResultCaching"]?.GetValue<bool>());
        Assert.False(functionOverride["UseDefaultDisplayProperties"]?.GetValue<bool>());

        var defaultProperties = functionOverride["DefaultProperties"]?.AsArray();
        Assert.NotNull(defaultProperties);
        Assert.Equal(2, defaultProperties!.Count);
        Assert.Equal("Id", defaultProperties[0]?.GetValue<string>());
        Assert.Equal("Name", defaultProperties[1]?.GetValue<string>());
    }

    [Theory]
    [InlineData("in-process", "InProcess")]
    [InlineData("out-of-process", "OutOfProcess")]
    public async Task UpdateConfigCommand_WithRuntimeMode_SetsRuntimeModeInPowerShellConfiguration(string cliValue, string expectedJsonValue)
    {
        using var tempDirectory = new TemporaryDirectory();
        using var currentDirectoryScope = new CurrentDirectoryScope(tempDirectory.Path);
        using var capture = new ConsoleCapture();

        var configPath = System.IO.Path.Combine(tempDirectory.Path, "appsettings.json");
        await File.WriteAllTextAsync(configPath, @"{
  ""PowerShellConfiguration"": {
    ""FunctionNames"": [],
    ""Modules"": [],
    ""IncludePatterns"": [],
    ""ExcludePatterns"": []
  }
}");

        var result = await Program.Main(new[]
        {
            "update-config",
            "--non-interactive",
            "--runtime-mode", cliValue,
            "--format", "json"
        });

        Assert.Equal(0, result);

        var updatedRoot = JsonNode.Parse(await File.ReadAllTextAsync(configPath))?.AsObject();
        var powerShellConfig = updatedRoot!["PowerShellConfiguration"]?.AsObject();
        Assert.Equal(expectedJsonValue, powerShellConfig!["RuntimeMode"]?.GetValue<string>());
    }

    [Fact]
    public async Task UpdateConfigCommand_WithRuntimeMode_DoesNotAddLegacyFunctionNamesWhenAbsent()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var currentDirectoryScope = new CurrentDirectoryScope(tempDirectory.Path);
        using var capture = new ConsoleCapture();

        var configPath = System.IO.Path.Combine(tempDirectory.Path, "appsettings.json");
        await File.WriteAllTextAsync(configPath, @"{
  ""PowerShellConfiguration"": {
    ""CommandNames"": [""Get-Date""],
    ""Modules"": [],
    ""IncludePatterns"": [],
    ""ExcludePatterns"": []
  }
}");

        var result = await Program.Main(new[]
        {
            "update-config",
            "--non-interactive",
            "--runtime-mode", "out-of-process",
            "--format", "json"
        });

        Assert.Equal(0, result);

        var updatedRoot = JsonNode.Parse(await File.ReadAllTextAsync(configPath))?.AsObject();
        Assert.NotNull(updatedRoot);

        var powerShellConfig = updatedRoot!["PowerShellConfiguration"]?.AsObject();
        Assert.NotNull(powerShellConfig);
        Assert.Equal("OutOfProcess", powerShellConfig!["RuntimeMode"]?.GetValue<string>());
        Assert.Null(powerShellConfig["FunctionNames"]);
    }

    [Fact]
    public async Task UpdateConfigCommand_WithInvalidRuntimeMode_ReportsErrorAndDoesNotModifyConfig()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var currentDirectoryScope = new CurrentDirectoryScope(tempDirectory.Path);
        using var capture = new ConsoleCapture();

        var configPath = System.IO.Path.Combine(tempDirectory.Path, "appsettings.json");
        await File.WriteAllTextAsync(configPath, @"{
  ""PowerShellConfiguration"": {
    ""FunctionNames"": [],
    ""Modules"": [],
    ""IncludePatterns"": [],
    ""ExcludePatterns"": []
  }
}");

        await Program.Main(new[]
        {
            "update-config",
            "--non-interactive",
            "--runtime-mode", "invalid-value",
            "--format", "json"
        });

        // Invalid runtime mode should produce an error message
        Assert.Contains("invalid-value", capture.StandardError, StringComparison.OrdinalIgnoreCase);

        // RuntimeMode should not have been written to the config
        var configAfter = JsonNode.Parse(await File.ReadAllTextAsync(configPath))?.AsObject();
        var powerShellConfigAfter = configAfter!["PowerShellConfiguration"]?.AsObject();
        Assert.Null(powerShellConfigAfter!["RuntimeMode"]);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task UpdateConfigCommand_WithEnableResultCaching_SetsPerformanceEnableResultCaching(string cliValue, bool expectedValue)
    {
        using var tempDirectory = new TemporaryDirectory();
        using var currentDirectoryScope = new CurrentDirectoryScope(tempDirectory.Path);
        using var capture = new ConsoleCapture();

        var configPath = System.IO.Path.Combine(tempDirectory.Path, "appsettings.json");
        await File.WriteAllTextAsync(configPath, @"{
  ""PowerShellConfiguration"": {
    ""FunctionNames"": [],
    ""Modules"": [],
    ""IncludePatterns"": [],
    ""ExcludePatterns"": []
  }
}");

        var result = await Program.Main(new[]
        {
            "update-config",
            "--non-interactive",
            "--enable-result-caching", cliValue,
            "--format", "json"
        });

        Assert.Equal(0, result);

        var updatedRoot = JsonNode.Parse(await File.ReadAllTextAsync(configPath))?.AsObject();
        var performance = updatedRoot!["PowerShellConfiguration"]?["Performance"]?.AsObject();
        Assert.NotNull(performance);
        Assert.Equal(expectedValue, performance!["EnableResultCaching"]?.GetValue<bool>());
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task UpdateConfigCommand_WithEnableConfigurationTroubleshootingTool_SetsFieldInPowerShellConfiguration(string cliValue, bool expectedValue)
    {
        using var tempDirectory = new TemporaryDirectory();
        using var currentDirectoryScope = new CurrentDirectoryScope(tempDirectory.Path);
        using var capture = new ConsoleCapture();

        var configPath = System.IO.Path.Combine(tempDirectory.Path, "appsettings.json");
        await File.WriteAllTextAsync(configPath, @"{
  ""PowerShellConfiguration"": {
    ""FunctionNames"": [],
    ""Modules"": [],
    ""IncludePatterns"": [],
    ""ExcludePatterns"": []
  }
}");

        var result = await Program.Main(new[]
        {
            "update-config",
            "--non-interactive",
            "--enable-configuration-troubleshooting-tool", cliValue,
            "--format", "json"
        });

        Assert.Equal(0, result);

        var updatedRoot = JsonNode.Parse(await File.ReadAllTextAsync(configPath))?.AsObject();
        var powerShellConfig = updatedRoot!["PowerShellConfiguration"]?.AsObject();
        Assert.Equal(expectedValue, powerShellConfig!["EnableConfigurationTroubleshootingTool"]?.GetValue<bool>());
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task UpdateConfigCommand_WithSetAuthEnabled_SetsAuthenticationEnabledAtRoot(string cliValue, bool expectedValue)
    {
        using var tempDirectory = new TemporaryDirectory();
        using var currentDirectoryScope = new CurrentDirectoryScope(tempDirectory.Path);
        using var capture = new ConsoleCapture();

        var configPath = System.IO.Path.Combine(tempDirectory.Path, "appsettings.json");
        await File.WriteAllTextAsync(configPath, @"{
  ""PowerShellConfiguration"": {
    ""FunctionNames"": [],
    ""Modules"": [],
    ""IncludePatterns"": [],
    ""ExcludePatterns"": []
  }
}");

        var result = await Program.Main(new[]
        {
            "update-config",
            "--non-interactive",
            "--set-auth-enabled", cliValue,
            "--format", "json"
        });

        Assert.Equal(0, result);

        var updatedRoot = JsonNode.Parse(await File.ReadAllTextAsync(configPath))?.AsObject();

        // Authentication.Enabled lives at root, not under PowerShellConfiguration
        var authentication = updatedRoot!["Authentication"]?.AsObject();
        Assert.NotNull(authentication);
        Assert.Equal(expectedValue, authentication!["Enabled"]?.GetValue<bool>());

        // Verify it was NOT placed under PowerShellConfiguration
        var powerShellConfig = updatedRoot["PowerShellConfiguration"]?.AsObject();
        Assert.Null(powerShellConfig!["Authentication"]);
    }

    [Fact]
    public async Task UpdateConfigCommand_WithSingleNewFlag_ReportsSettingsChangedOfOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var currentDirectoryScope = new CurrentDirectoryScope(tempDirectory.Path);
        using var capture = new ConsoleCapture();

        var configPath = System.IO.Path.Combine(tempDirectory.Path, "appsettings.json");
        await File.WriteAllTextAsync(configPath, @"{
  ""PowerShellConfiguration"": {
    ""FunctionNames"": [],
    ""Modules"": [],
    ""IncludePatterns"": [],
    ""ExcludePatterns"": []
  }
}");

        var result = await Program.Main(new[]
        {
            "update-config",
            "--non-interactive",
            "--enable-result-caching", "true",
            "--format", "json"
        });

        Assert.Equal(0, result);

        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!["settingsChanged"]?.GetValue<int>());
    }

    [Fact]
    public async Task UpdateConfigCommand_WithMultipleNewFlags_AccumulatesSettingsChangedCount()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var currentDirectoryScope = new CurrentDirectoryScope(tempDirectory.Path);
        using var capture = new ConsoleCapture();

        var configPath = System.IO.Path.Combine(tempDirectory.Path, "appsettings.json");
        await File.WriteAllTextAsync(configPath, @"{
  ""PowerShellConfiguration"": {
    ""FunctionNames"": [],
    ""Modules"": [],
    ""IncludePatterns"": [],
    ""ExcludePatterns"": []
  }
}");

        // Three independent flag-based settings in a single invocation
        var result = await Program.Main(new[]
        {
            "update-config",
            "--non-interactive",
            "--runtime-mode", "out-of-process",
            "--enable-result-caching", "true",
            "--set-auth-enabled", "false",
            "--format", "json"
        });

        Assert.Equal(0, result);

        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        Assert.Equal(3, payload!["settingsChanged"]?.GetValue<int>());
    }

    [Fact]
    public async Task UpdateConfigCommand_WhenAddingCommand_InteractivePromptsCanSetAllowAnonymousRequiredScopesAndRoles()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var currentDirectoryScope = new CurrentDirectoryScope(tempDirectory.Path);

        // Prompt answers in order:
        // 1. Configure advanced settings for 'Get-Service'? -> y
        // 2. Override EnableResultCaching? -> skip (empty)
        // 3. Override UseDefaultDisplayProperties? -> skip (empty)
        // 4. Set DefaultProperties? -> skip (empty)
        // 5. Set AllowAnonymous? -> true
        // 6. Set RequiredScopes? -> read:users write:config
        // 7. Set RequiredRoles? -> admin operator
        using var capture = new ConsoleCapture("y\n\n\n\ntrue\nread:users write:config\nadmin operator\n");

        var configPath = System.IO.Path.Combine(tempDirectory.Path, "appsettings.json");
        await File.WriteAllTextAsync(configPath, @"{
  ""PowerShellConfiguration"": {
    ""FunctionNames"": [],
    ""Modules"": [],
    ""IncludePatterns"": [],
    ""ExcludePatterns"": []
  }
}");

        var result = await Program.Main(new[]
        {
            "update-config",
            "--add-command", "Get-Service",
            "--format", "json"
        });

        Assert.Equal(0, result);

        var updatedRoot = JsonNode.Parse(await File.ReadAllTextAsync(configPath))?.AsObject();
        var functionOverride = updatedRoot!["PowerShellConfiguration"]?["CommandOverrides"]?["Get-Service"]?.AsObject();
        Assert.NotNull(functionOverride);

        Assert.True(functionOverride!["AllowAnonymous"]?.GetValue<bool>());

        var requiredScopes = functionOverride["RequiredScopes"]?.AsArray();
        Assert.NotNull(requiredScopes);
        Assert.Equal(2, requiredScopes!.Count);
        Assert.Contains(requiredScopes, s => s?.GetValue<string>() == "read:users");
        Assert.Contains(requiredScopes, s => s?.GetValue<string>() == "write:config");

        var requiredRoles = functionOverride["RequiredRoles"]?.AsArray();
        Assert.NotNull(requiredRoles);
        Assert.Equal(2, requiredRoles!.Count);
        Assert.Contains(requiredRoles, r => r?.GetValue<string>() == "admin");
        Assert.Contains(requiredRoles, r => r?.GetValue<string>() == "operator");
    }

        [Fact]
        public async Task UpdateConfigCommand_WhenAdvancedPromptsRun_MigratesLegacyFunctionOverridesToCommandOverrides()
        {
                using var tempDirectory = new TemporaryDirectory();
                using var currentDirectoryScope = new CurrentDirectoryScope(tempDirectory.Path);
                using var capture = new ConsoleCapture("y\n\n\n\n\n\n\n");

                var configPath = System.IO.Path.Combine(tempDirectory.Path, "appsettings.json");
                await File.WriteAllTextAsync(configPath, @"{
    ""PowerShellConfiguration"": {
        ""CommandNames"": [],
        ""Modules"": [],
        ""IncludePatterns"": [],
        ""ExcludePatterns"": [],
        ""FunctionOverrides"": {
            ""Get-Date"": {
                ""RequiredRoles"": [""reader""]
            }
        }
    }
}");

                var result = await Program.Main(new[]
                {
                        "update-config",
                        "--add-command", "Get-Service",
                        "--format", "json"
                });

                Assert.Equal(0, result);

                var updatedRoot = JsonNode.Parse(await File.ReadAllTextAsync(configPath))?.AsObject();
                var psConfig = updatedRoot!["PowerShellConfiguration"]?.AsObject();
                Assert.NotNull(psConfig);
                Assert.Null(psConfig!["FunctionOverrides"]);

                var commandOverrides = psConfig["CommandOverrides"]?.AsObject();
                Assert.NotNull(commandOverrides);
                Assert.NotNull(commandOverrides!["Get-Date"]);
                Assert.NotNull(commandOverrides["Get-Service"]);
        }

    private sealed class ConsoleCapture : IDisposable
    {
        private readonly TextWriter _originalOut;
        private readonly TextWriter _originalError;
        private readonly TextReader _originalIn;
        private readonly StringWriter _capturedOut;
        private readonly StringWriter _capturedError;

        public string StandardOutput => _capturedOut.ToString();
        public string StandardError => _capturedError.ToString();

        public ConsoleCapture(string? input = null)
        {
            _originalOut = Console.Out;
            _originalError = Console.Error;
            _originalIn = Console.In;
            _capturedOut = new StringWriter();
            _capturedError = new StringWriter();

            Console.SetOut(_capturedOut);
            Console.SetError(_capturedError);
            Console.SetIn(new StringReader(input ?? string.Empty));
        }

        public void Dispose()
        {
            Console.SetOut(_originalOut);
            Console.SetError(_originalError);
            Console.SetIn(_originalIn);
            _capturedOut.Dispose();
            _capturedError.Dispose();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; }

        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"poshmcp-cli-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }

    private sealed class CurrentDirectoryScope : IDisposable
    {
        private readonly string _originalDirectory;

        public CurrentDirectoryScope(string newDirectory)
        {
            _originalDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(newDirectory);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalDirectory);
        }
    }
}
