using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;
using PoshMcp.Tests.Shared;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public class UpdateConfigurationFileAsyncTests
{
    [Fact]
    public async Task UpdateConfigurationFileAsync_AddCommands_AddsCommandsToEmptyArray()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot(commandNames: Array.Empty<string>()));

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(
            configPath,
            CreateRequest(addCommands: new[] { "Get-Process", "Get-Date" }));

        Assert.True(result.Changed);
        Assert.Equal(2, result.AddedCommands);
        Assert.Equal(0, result.RemovedCommands);

        var commandNames = GetStringValues((await ReadRootAsync(configPath))["PowerShellConfiguration"]?["CommandNames"]);
        Assert.Equal(new[] { "Get-Process", "Get-Date" }, commandNames);
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_AddCommands_DeduplicatesCaseInsensitively()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot(commandNames: new[] { "Get-Process" }));

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(
            configPath,
            CreateRequest(addCommands: new[] { "get-process", "Get-Date" }));

        Assert.True(result.Changed);
        Assert.Equal(1, result.AddedCommands);

        var commandNames = GetStringValues((await ReadRootAsync(configPath))["PowerShellConfiguration"]?["CommandNames"]);
        Assert.Equal(new[] { "Get-Process", "Get-Date" }, commandNames);
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_RemoveCommands_RemovesExistingCommands()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot(commandNames: new[] { "Get-Process", "Get-Date" }));

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(
            configPath,
            CreateRequest(removeCommands: new[] { "get-process" }));

        Assert.True(result.Changed);
        Assert.Equal(1, result.RemovedCommands);

        var commandNames = GetStringValues((await ReadRootAsync(configPath))["PowerShellConfiguration"]?["CommandNames"]);
        Assert.Equal(new[] { "Get-Date" }, commandNames);
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_RemoveCommands_WhenCommandDoesNotExist_ReturnsUnchanged()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot(commandNames: new[] { "Get-Process" }));

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(
            configPath,
            CreateRequest(removeCommands: new[] { "Get-Date" }));

        Assert.False(result.Changed);
        Assert.Equal(0, result.RemovedCommands);

        var commandNames = GetStringValues((await ReadRootAsync(configPath))["PowerShellConfiguration"]?["CommandNames"]);
        Assert.Equal(new[] { "Get-Process" }, commandNames);
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_AddFunctions_AddsLegacyFunctionNames()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot(functionNames: Array.Empty<string>()));

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(
            configPath,
            CreateRequest(addFunctions: new[] { "Get-Process" }));

        Assert.True(result.Changed);
        Assert.Equal(1, result.AddedFunctions);

        var functionNames = GetStringValues((await ReadRootAsync(configPath))["PowerShellConfiguration"]?["FunctionNames"]);
        Assert.Equal(new[] { "Get-Process" }, functionNames);
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_RemoveFunctions_RemovesLegacyFunctionNames()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot(functionNames: new[] { "Get-Process", "Get-Date" }));

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(
            configPath,
            CreateRequest(removeFunctions: new[] { "get-process" }));

        Assert.True(result.Changed);
        Assert.Equal(1, result.RemovedFunctions);

        var functionNames = GetStringValues((await ReadRootAsync(configPath))["PowerShellConfiguration"]?["FunctionNames"]);
        Assert.Equal(new[] { "Get-Date" }, functionNames);
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_WhenNoLegacyFunctionOperations_DoesNotCreateFunctionNames()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot(commandNames: new[] { "Get-Process" }));

        await ConfigurationFileManager.UpdateConfigurationFileAsync(
            configPath,
            CreateRequest(setRuntimeMode: "OutOfProcess"));

        var powerShellConfiguration = (await ReadRootAsync(configPath))["PowerShellConfiguration"]?.AsObject();
        Assert.NotNull(powerShellConfiguration);
        Assert.Null(powerShellConfiguration!["FunctionNames"]);
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_AddModules_AddsModules()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot(modules: Array.Empty<string>()));

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(
            configPath,
            CreateRequest(addModules: new[] { "Pester" }));

        Assert.True(result.Changed);

        var modules = GetStringValues((await ReadRootAsync(configPath))["PowerShellConfiguration"]?["Modules"]);
        Assert.Equal(new[] { "Pester" }, modules);
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_RemoveModules_RemovesModules()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot(modules: new[] { "Pester", "Az.Accounts" }));

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(
            configPath,
            CreateRequest(removeModules: new[] { "pester" }));

        Assert.True(result.Changed);

        var modules = GetStringValues((await ReadRootAsync(configPath))["PowerShellConfiguration"]?["Modules"]);
        Assert.Equal(new[] { "Az.Accounts" }, modules);
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_AddIncludePatterns_AddsPatterns()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot(includePatterns: Array.Empty<string>()));

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(
            configPath,
            CreateRequest(addIncludePatterns: new[] { "Get-*" }));

        Assert.True(result.Changed);

        var includePatterns = GetStringValues((await ReadRootAsync(configPath))["PowerShellConfiguration"]?["IncludePatterns"]);
        Assert.Equal(new[] { "Get-*" }, includePatterns);
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_RemoveIncludePatterns_RemovesPatterns()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot(includePatterns: new[] { "Get-*", "Set-*" }));

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(
            configPath,
            CreateRequest(removeIncludePatterns: new[] { "get-*" }));

        Assert.True(result.Changed);

        var includePatterns = GetStringValues((await ReadRootAsync(configPath))["PowerShellConfiguration"]?["IncludePatterns"]);
        Assert.Equal(new[] { "Set-*" }, includePatterns);
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_AddExcludePatterns_AddsPatterns()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot(excludePatterns: Array.Empty<string>()));

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(
            configPath,
            CreateRequest(addExcludePatterns: new[] { "*-Internal" }));

        Assert.True(result.Changed);

        var excludePatterns = GetStringValues((await ReadRootAsync(configPath))["PowerShellConfiguration"]?["ExcludePatterns"]);
        Assert.Equal(new[] { "*-Internal" }, excludePatterns);
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_RemoveExcludePatterns_RemovesPatterns()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot(excludePatterns: new[] { "*-Internal", "*-Private" }));

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(
            configPath,
            CreateRequest(removeExcludePatterns: new[] { "*-internal" }));

        Assert.True(result.Changed);

        var excludePatterns = GetStringValues((await ReadRootAsync(configPath))["PowerShellConfiguration"]?["ExcludePatterns"]);
        Assert.Equal(new[] { "*-Private" }, excludePatterns);
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_EnableDynamicReloadTools_WritesBoolean()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot());

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(
            configPath,
            CreateRequest(enableDynamicReloadTools: true));

        Assert.True(result.Changed);
        Assert.Equal(1, result.SettingsChanged);
        Assert.True((await ReadRootAsync(configPath))["PowerShellConfiguration"]?["EnableDynamicReloadTools"]?.GetValue<bool>());
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_EnableConfigurationTroubleshootingTool_WritesBoolean()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot());

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(
            configPath,
            CreateRequest(enableConfigurationTroubleshootingTool: false));

        Assert.True(result.Changed);
        Assert.Equal(1, result.SettingsChanged);
        Assert.False((await ReadRootAsync(configPath))["PowerShellConfiguration"]?["EnableConfigurationTroubleshootingTool"]?.GetValue<bool>());
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_EnableResultCaching_WritesUnderPerformance()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot());

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(
            configPath,
            CreateRequest(enableResultCaching: true));

        Assert.True(result.Changed);
        Assert.Equal(1, result.SettingsChanged);
        Assert.True((await ReadRootAsync(configPath))["PowerShellConfiguration"]?["Performance"]?["EnableResultCaching"]?.GetValue<bool>());
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_UseDefaultDisplayProperties_WritesUnderPerformance()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot());

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(
            configPath,
            CreateRequest(useDefaultDisplayProperties: false));

        Assert.True(result.Changed);
        Assert.Equal(1, result.SettingsChanged);
        Assert.False((await ReadRootAsync(configPath))["PowerShellConfiguration"]?["Performance"]?["UseDefaultDisplayProperties"]?.GetValue<bool>());
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_SetRuntimeMode_WritesRuntimeMode()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot());

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(
            configPath,
            CreateRequest(setRuntimeMode: "OutOfProcess"));

        Assert.True(result.Changed);
        Assert.Equal(1, result.SettingsChanged);
        Assert.Equal("OutOfProcess", (await ReadRootAsync(configPath))["PowerShellConfiguration"]?["RuntimeMode"]?.GetValue<string>());
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_SetAuthEnabled_WritesAuthenticationEnabled()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot());

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(
            configPath,
            CreateRequest(setAuthEnabled: true));

        Assert.True(result.Changed);
        Assert.Equal(1, result.SettingsChanged);
        Assert.True((await ReadRootAsync(configPath))["Authentication"]?["Enabled"]?.GetValue<bool>());
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_WhenRequestHasNoChanges_ReturnsChangedFalse()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot(
            commandNames: new[] { "Get-Process" },
            modules: new[] { "Pester" },
            includePatterns: Array.Empty<string>(),
            excludePatterns: Array.Empty<string>()));

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(configPath, CreateRequest());

        Assert.False(result.Changed);
        Assert.Equal(0, result.AddedCommands);
        Assert.Equal(0, result.RemovedCommands);
        Assert.Equal(0, result.AddedFunctions);
        Assert.Equal(0, result.RemovedFunctions);
        Assert.Equal(0, result.SettingsChanged);
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_WhenRequestHasNoChanges_DoesNotRewriteFile()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot(
            commandNames: new[] { "Get-Process" },
            modules: new[] { "Pester" },
            includePatterns: Array.Empty<string>(),
            excludePatterns: Array.Empty<string>()));

        var originalContent = await File.ReadAllTextAsync(configPath);
        var originalLastWriteTimeUtc = File.GetLastWriteTimeUtc(configPath);

        await Task.Delay(TimeSpan.FromSeconds(1.1));

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(configPath, CreateRequest());

        var updatedContent = await File.ReadAllTextAsync(configPath);
        var updatedLastWriteTimeUtc = File.GetLastWriteTimeUtc(configPath);

        Assert.False(result.Changed);
        Assert.Equal(originalContent, updatedContent);
        Assert.Equal(originalLastWriteTimeUtc, updatedLastWriteTimeUtc);
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_WithInvalidJson_ThrowsJsonException()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await File.WriteAllTextAsync(configPath, "not json");

        await Assert.ThrowsAnyAsync<JsonException>(() => ConfigurationFileManager.UpdateConfigurationFileAsync(configPath, CreateRequest()));
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_WithNonObjectJsonRoot_ThrowsInvalidOperationException()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await File.WriteAllTextAsync(configPath, "[]");

        await Assert.ThrowsAsync<InvalidOperationException>(() => ConfigurationFileManager.UpdateConfigurationFileAsync(configPath, CreateRequest()));
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_WithMultipleOperations_AppliesAllChanges()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot(
            commandNames: new[] { "Get-Process" },
            modules: new[] { "Pester", "Az.Accounts" },
            includePatterns: Array.Empty<string>(),
            excludePatterns: Array.Empty<string>()));

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(
            configPath,
            CreateRequest(
                addCommands: new[] { "Get-Date" },
                removeModules: new[] { "Pester" },
                enableDynamicReloadTools: true));

        Assert.True(result.Changed);
        Assert.Equal(1, result.AddedCommands);
        Assert.Equal(1, result.SettingsChanged);

        var powerShellConfiguration = (await ReadRootAsync(configPath))["PowerShellConfiguration"]?.AsObject();
        Assert.NotNull(powerShellConfiguration);
        Assert.Equal(new[] { "Get-Process", "Get-Date" }, GetStringValues(powerShellConfiguration!["CommandNames"]));
        Assert.Equal(new[] { "Az.Accounts" }, GetStringValues(powerShellConfiguration["Modules"]));
        Assert.True(powerShellConfiguration["EnableDynamicReloadTools"]?.GetValue<bool>());
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_SkipsWhitespaceAndEmptyCollectionValues()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot(commandNames: Array.Empty<string>()));

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(
            configPath,
            CreateRequest(addCommands: new[] { string.Empty, "   ", "Get-Date" }));

        Assert.True(result.Changed);
        Assert.Equal(1, result.AddedCommands);
        Assert.Equal(new[] { "Get-Date" }, GetStringValues((await ReadRootAsync(configPath))["PowerShellConfiguration"]?["CommandNames"]));
    }

    [Fact]
    public async Task UpdateConfigurationFileAsync_DeduplicatesCaseInsensitivelyAcrossAllArrays()
    {
        using var tempDirectory = new TempDirectory();
        var configPath = Path.Combine(tempDirectory.Path, "appsettings.json");
        await WriteConfigAsync(configPath, CreateConfigRoot(
            commandNames: new[] { "Get-Process" },
            functionNames: new[] { "Get-Service" },
            modules: new[] { "Pester" },
            includePatterns: new[] { "Get-*" },
            excludePatterns: new[] { "*-Internal" }));

        var result = await ConfigurationFileManager.UpdateConfigurationFileAsync(
            configPath,
            CreateRequest(
                addCommands: new[] { "get-process" },
                addFunctions: new[] { "get-service" },
                addModules: new[] { "pester" },
                addIncludePatterns: new[] { "get-*" },
                addExcludePatterns: new[] { "*-internal" }));

        Assert.False(result.Changed);
        Assert.Equal(0, result.AddedCommands);
        Assert.Equal(0, result.AddedFunctions);

        var powerShellConfiguration = (await ReadRootAsync(configPath))["PowerShellConfiguration"]?.AsObject();
        Assert.NotNull(powerShellConfiguration);
        Assert.Equal(new[] { "Get-Process" }, GetStringValues(powerShellConfiguration!["CommandNames"]));
        Assert.Equal(new[] { "Get-Service" }, GetStringValues(powerShellConfiguration["FunctionNames"]));
        Assert.Equal(new[] { "Pester" }, GetStringValues(powerShellConfiguration["Modules"]));
        Assert.Equal(new[] { "Get-*" }, GetStringValues(powerShellConfiguration["IncludePatterns"]));
        Assert.Equal(new[] { "*-Internal" }, GetStringValues(powerShellConfiguration["ExcludePatterns"]));
    }

    private static ConfigUpdateRequest CreateRequest(
        IEnumerable<string>? addFunctions = null,
        IEnumerable<string>? removeFunctions = null,
        IEnumerable<string>? addCommands = null,
        IEnumerable<string>? removeCommands = null,
        IEnumerable<string>? addModules = null,
        IEnumerable<string>? removeModules = null,
        IEnumerable<string>? addIncludePatterns = null,
        IEnumerable<string>? removeIncludePatterns = null,
        IEnumerable<string>? addExcludePatterns = null,
        IEnumerable<string>? removeExcludePatterns = null,
        bool? enableDynamicReloadTools = null,
        bool? enableConfigurationTroubleshootingTool = null,
        bool? enableResultCaching = null,
        bool? useDefaultDisplayProperties = null,
        bool? setAuthEnabled = null,
        string? setRuntimeMode = null,
        bool nonInteractive = true)
    {
        return new ConfigUpdateRequest(
            addFunctions ?? Array.Empty<string>(),
            removeFunctions ?? Array.Empty<string>(),
            addCommands ?? Array.Empty<string>(),
            removeCommands ?? Array.Empty<string>(),
            addModules ?? Array.Empty<string>(),
            removeModules ?? Array.Empty<string>(),
            addIncludePatterns ?? Array.Empty<string>(),
            removeIncludePatterns ?? Array.Empty<string>(),
            addExcludePatterns ?? Array.Empty<string>(),
            removeExcludePatterns ?? Array.Empty<string>(),
            enableDynamicReloadTools,
            enableConfigurationTroubleshootingTool,
            enableResultCaching,
            useDefaultDisplayProperties,
            setAuthEnabled,
            setRuntimeMode,
            nonInteractive);
    }

    private static JsonObject CreateConfigRoot(
        IEnumerable<string>? commandNames = null,
        IEnumerable<string>? functionNames = null,
        IEnumerable<string>? modules = null,
        IEnumerable<string>? includePatterns = null,
        IEnumerable<string>? excludePatterns = null)
    {
        var powerShellConfiguration = new JsonObject();

        AddArrayIfPresent(powerShellConfiguration, "CommandNames", commandNames);
        AddArrayIfPresent(powerShellConfiguration, "FunctionNames", functionNames);
        AddArrayIfPresent(powerShellConfiguration, "Modules", modules);
        AddArrayIfPresent(powerShellConfiguration, "IncludePatterns", includePatterns);
        AddArrayIfPresent(powerShellConfiguration, "ExcludePatterns", excludePatterns);

        return new JsonObject
        {
            ["PowerShellConfiguration"] = powerShellConfiguration
        };
    }

    private static void AddArrayIfPresent(JsonObject parent, string propertyName, IEnumerable<string>? values)
    {
        if (values is null)
        {
            return;
        }

        parent[propertyName] = new JsonArray(values.Select(value => (JsonNode?)value).ToArray());
    }

    private static async Task WriteConfigAsync(string configPath, JsonObject root)
    {
        await File.WriteAllTextAsync(configPath, root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        }) + Environment.NewLine);
    }

    private static async Task<JsonObject> ReadRootAsync(string configPath)
    {
        return JsonNode.Parse(await File.ReadAllTextAsync(configPath))?.AsObject()
            ?? throw new InvalidOperationException("Expected configuration JSON object.");
    }

    private static string[] GetStringValues(JsonNode? node)
    {
        return node?.AsArray().Select(value => value?.GetValue<string>() ?? string.Empty).ToArray()
            ?? Array.Empty<string>();
    }

    private sealed class ConsoleCapture : IDisposable
    {
        private readonly TextWriter _originalOut;
        private readonly TextWriter _originalError;
        private readonly TextReader _originalIn;
        private readonly StringWriter _capturedOut;
        private readonly StringWriter _capturedError;

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
}
