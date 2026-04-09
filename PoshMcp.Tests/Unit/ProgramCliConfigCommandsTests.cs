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
    ""FunctionNames"": [""Get-Process""],
    ""Modules"": [],
    ""IncludePatterns"": [],
    ""ExcludePatterns"": []
  }
}");

        var result = await Program.Main(new[]
        {
            "update-config",
            "--non-interactive",
            "--add-function", "Get-Date",
            "--remove-function", "Get-Process",
            "--add-module", "Pester",
            "--enable-dynamic-reload-tools", "true",
            "--format", "json"
        });

        Assert.Equal(0, result);

        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        Assert.True(payload!["changed"]?.GetValue<bool>());
        Assert.Equal(1, payload["addedFunctions"]?.GetValue<int>());
        Assert.Equal(1, payload["removedFunctions"]?.GetValue<int>());

        var updatedRoot = JsonNode.Parse(await File.ReadAllTextAsync(configPath))?.AsObject();
        Assert.NotNull(updatedRoot);

        var powerShellConfiguration = updatedRoot!["PowerShellConfiguration"]?.AsObject();
        Assert.NotNull(powerShellConfiguration);

        var functionNames = powerShellConfiguration!["FunctionNames"]?.AsArray();
        Assert.NotNull(functionNames);
        Assert.Contains(functionNames!, item => string.Equals(item?.GetValue<string>(), "Get-Date", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(functionNames!, item => string.Equals(item?.GetValue<string>(), "Get-Process", StringComparison.OrdinalIgnoreCase));

        var modules = powerShellConfiguration["Modules"]?.AsArray();
        Assert.NotNull(modules);
        Assert.Contains(modules!, item => string.Equals(item?.GetValue<string>(), "Pester", StringComparison.OrdinalIgnoreCase));

        Assert.True(powerShellConfiguration["EnableDynamicReloadTools"]?.GetValue<bool>());
    }

    [Fact]
    public async Task UpdateConfigCommand_WhenAddingFunction_InteractivePromptsCanSetAdvancedOverrides()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var currentDirectoryScope = new CurrentDirectoryScope(tempDirectory.Path);
        using var capture = new ConsoleCapture("y\ntrue\nfalse\nId,Name\n");

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
            "--add-function", "Get-Process",
            "--format", "json"
        });

        Assert.Equal(0, result);

        var updatedRoot = JsonNode.Parse(await File.ReadAllTextAsync(configPath))?.AsObject();
        Assert.NotNull(updatedRoot);

        var functionOverride = updatedRoot!["PowerShellConfiguration"]?["FunctionOverrides"]?["Get-Process"]?.AsObject();
        Assert.NotNull(functionOverride);
        Assert.True(functionOverride!["EnableResultCaching"]?.GetValue<bool>());
        Assert.False(functionOverride["UseDefaultDisplayProperties"]?.GetValue<bool>());

        var defaultProperties = functionOverride["DefaultProperties"]?.AsArray();
        Assert.NotNull(defaultProperties);
        Assert.Equal(2, defaultProperties!.Count);
        Assert.Equal("Id", defaultProperties[0]?.GetValue<string>());
        Assert.Equal("Name", defaultProperties[1]?.GetValue<string>());
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
