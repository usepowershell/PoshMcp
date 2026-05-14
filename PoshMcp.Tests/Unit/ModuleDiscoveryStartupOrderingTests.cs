using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using PoshMcp.Server.PowerShell;
using PoshMcp.Tests.Shared;
using Xunit;

namespace PoshMcp.Tests.Unit;

public sealed class ModuleDiscoveryStartupOrderingTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly IsolatedPowerShellRunspace _runspace;
    private readonly McpToolFactoryV2 _toolFactory;
    private readonly PowerShellEnvironmentSetup _environmentSetup;
    private readonly TempDirectory _tempDir;
    private string _tempDirectory => _tempDir.Path;

    public ModuleDiscoveryStartupOrderingTests()
    {
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        _logger = _loggerFactory.CreateLogger<ModuleDiscoveryStartupOrderingTests>();
        _runspace = new IsolatedPowerShellRunspace();
        _toolFactory = new McpToolFactoryV2(_runspace);
        _environmentSetup = new PowerShellEnvironmentSetup(_loggerFactory.CreateLogger<PowerShellEnvironmentSetup>());
        _tempDir = new TempDirectory("module-order");
    }

    [Fact]
    public async Task GetToolsList_WithImportedModuleBeforeDiscovery_ShouldDiscoverModuleFunction()
    {
        var uniqueSuffix = Guid.NewGuid().ToString("N");
        var functionName = $"Get-FryModule{uniqueSuffix}";
        var modulePath = CreateModuleFile(functionName);
        var discoveryConfig = CreateDiscoveryConfig(functionName);

        var toolsBeforeImport = _toolFactory.GetToolsList(discoveryConfig, _logger);
        Assert.Empty(toolsBeforeImport);
        Assert.False(IsFunctionDiscoverable(functionName));

        var setupResult = await _environmentSetup.ApplyEnvironmentConfiguration(
            _runspace.Instance,
            new EnvironmentConfiguration
            {
                ImportModules = new List<string> { modulePath }
            });

        Assert.True(setupResult.Success, $"Environment setup failed: {string.Join("; ", setupResult.Errors)}");
        Assert.True(IsFunctionDiscoverable(functionName));

        _toolFactory.ClearCache();
        var toolsAfterImport = _toolFactory.GetToolsList(discoveryConfig, _logger);

        Assert.NotEmpty(toolsAfterImport);
        Assert.Contains(toolsAfterImport, tool => IsExpectedFryTool(tool, uniqueSuffix));
    }

    [Fact]
    public async Task GetToolsList_WithStartupScriptBeforeDiscovery_ShouldDiscoverScriptFunction()
    {
        var uniqueSuffix = Guid.NewGuid().ToString("N");
        var functionName = $"Get-FryStartup{uniqueSuffix}";
        var scriptPath = CreateStartupScriptFile(functionName);
        var discoveryConfig = CreateDiscoveryConfig(functionName);

        var toolsBeforeScript = _toolFactory.GetToolsList(discoveryConfig, _logger);
        Assert.Empty(toolsBeforeScript);
        Assert.False(IsFunctionDiscoverable(functionName));

        var setupResult = await _environmentSetup.ApplyEnvironmentConfiguration(
            _runspace.Instance,
            new EnvironmentConfiguration
            {
                StartupScriptPath = scriptPath
            });

        Assert.True(setupResult.Success, $"Environment setup failed: {string.Join("; ", setupResult.Errors)}");
        Assert.True(setupResult.StartupScriptExecuted);
        Assert.True(IsFunctionDiscoverable(functionName));

        _toolFactory.ClearCache();
        var toolsAfterScript = _toolFactory.GetToolsList(discoveryConfig, _logger);

        Assert.NotEmpty(toolsAfterScript);
        Assert.Contains(toolsAfterScript, tool => IsExpectedFryTool(tool, uniqueSuffix));
    }

    private static bool IsExpectedFryTool(McpServerTool tool, string uniqueSuffix)
    {
        return tool.ProtocolTool.Name.StartsWith("get_fry_", StringComparison.OrdinalIgnoreCase)
            && tool.ProtocolTool.Name.Contains(uniqueSuffix, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsFunctionDiscoverable(string functionName)
    {
        return _runspace.ExecuteThreadSafe(ps =>
        {
            ps.Commands.Clear();
            ps.AddCommand("Get-Command")
                .AddParameter("Name", functionName)
                .AddParameter("ErrorAction", "SilentlyContinue");
            var commandInfo = ps.Invoke<CommandInfo>().FirstOrDefault();
            ps.Commands.Clear();
            return commandInfo != null;
        });
    }

    private static PowerShellConfiguration CreateDiscoveryConfig(string functionName)
    {
        return new PowerShellConfiguration
        {
            FunctionNames = new List<string> { functionName },
            Modules = new List<string>(),
            IncludePatterns = new List<string>(),
            ExcludePatterns = new List<string>()
        };
    }

    private string CreateModuleFile(string functionName)
    {
        var modulePath = Path.Combine(_tempDirectory, $"{functionName}.psm1");
        var moduleContent = $@"
function {functionName} {{
    [CmdletBinding()]
    param()

    return '{functionName}-ok'
}}

Export-ModuleMember -Function {functionName}
";

        File.WriteAllText(modulePath, moduleContent);
        return modulePath;
    }

    private string CreateStartupScriptFile(string functionName)
    {
        var scriptPath = Path.Combine(_tempDirectory, $"{functionName}.ps1");
        var scriptContent = $@"
function {functionName} {{
    [CmdletBinding()]
    param()

    return '{functionName}-ok'
}}
";

        File.WriteAllText(scriptPath, scriptContent);
        return scriptPath;
    }

    public void Dispose()
    {
        _toolFactory.ClearCache();
        _runspace.Dispose();
        _loggerFactory.Dispose();
        _tempDir.Dispose();
    }
}