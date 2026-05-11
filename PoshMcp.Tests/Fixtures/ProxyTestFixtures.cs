// Fixture definitions for PR #211 end-to-end integration testing
// 
// These fixtures validate that PR #211's changes to:
//   * PowerShellParameterUtils (proxy detection, type coercion)
//   * PowerShellAssemblyGenerator (proxy-aware method generation)
//   * McpToolFactoryV2 (cached delegate emission for >16-param methods)
// 
// produce correct tool schema output through the full MCP startup path.
//
// The fixtures include:
//   1. A synthetic high-parameter command (17+ params) to exercise cached delegate path
//   2. A synthetic proxy-like command to test object→string type coercion
//
// Fixtures are registered via normal PowerShell discovery flow (CreateConfigurationTroubleshootingToolInstance,
// McpToolSetupService, etc.) allowing the integration test to verify end-to-end schema generation.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using Xunit;

namespace PoshMcp.Tests.Fixtures;

/// <summary>
/// Test fixture generators for proxy and high-parameter method scenarios.
/// 
/// These are used by integration tests to validate that the complete MCP tool schema
/// generation path correctly handles:
///   1. Proxy-style commands (Export-PSSession output)
///   2. Methods with >16 parameters (triggering cached delegate emit in McpToolFactoryV2)
/// </summary>
public static class ProxyTestFixtures
{
    /// <summary>
    /// Creates a synthetic proxy-style command using PowerShell module generation.
    /// 
    /// The command appears to have been created by Export-PSSession:
    ///   - Module is marked with ImplicitRemoting=true in PrivateData
    ///   - Has an Object-typed parameter (typical of proxy collapse)
    ///   - Parameters lack Mandatory flags (typical of proxy rewrite)
    /// 
    /// Returns the CommandInfo so integration tests can validate schema generation.
    /// </summary>
    /// <returns>CommandInfo for a proxy-style command ready for tool schema generation</returns>
    public static CommandInfo CreateProxyStyledCommand()
    {
        var module = CreateSyntheticProxyModule(
            moduleName: "FakeProxyModule_PR211",
            description: "Implicit remoting for http://localhost:5985/wsman",
            rootModule: "remoteIpMoProxy_TestCommand_1.0.0.0_localhost_guid.psm1");

        return CreateCommandInModule(module, "Test-ProxyStyleCommand");
    }

    /// <summary>
    /// Creates a synthetic command with 17 parameters to exercise the cached
    /// delegate emission path in McpToolFactoryV2.GetDelegateTypeForMethod().
    /// 
    /// The method will:
    ///   - Bypass System.Func<> fast path (only goes to Func`17, 16 params)
    ///   - Force dynamic delegate type emission
    ///   - Validate that the cache correctly reuses emitted types
    /// </summary>
    /// <returns>CommandInfo for a high-parameter command ready for schema validation</returns>
    public static CommandInfo CreateHighParameterCommand()
    {
        // Create a module that exports a function with 17 parameters
        using var ps = System.Management.Automation.PowerShell.Create();
        var script = $@"
$module = New-Module -Name 'HighParamModule_PR211' -ScriptBlock {{
    function Invoke-HighParamCommand {{
        [CmdletBinding()]
        param(
            [Parameter(Mandatory=$false)]
            [string]$Param01,
            [Parameter(Mandatory=$false)]
            [string]$Param02,
            [Parameter(Mandatory=$false)]
            [string]$Param03,
            [Parameter(Mandatory=$false)]
            [string]$Param04,
            [Parameter(Mandatory=$false)]
            [string]$Param05,
            [Parameter(Mandatory=$false)]
            [string]$Param06,
            [Parameter(Mandatory=$false)]
            [string]$Param07,
            [Parameter(Mandatory=$false)]
            [string]$Param08,
            [Parameter(Mandatory=$false)]
            [string]$Param09,
            [Parameter(Mandatory=$false)]
            [string]$Param10,
            [Parameter(Mandatory=$false)]
            [string]$Param11,
            [Parameter(Mandatory=$false)]
            [string]$Param12,
            [Parameter(Mandatory=$false)]
            [string]$Param13,
            [Parameter(Mandatory=$false)]
            [string]$Param14,
            [Parameter(Mandatory=$false)]
            [string]$Param15,
            [Parameter(Mandatory=$false)]
            [string]$Param16,
            [Parameter(Mandatory=$false)]
            [string]$Param17
        )
        return ""High-param function invoked with $($PSBoundParameters.Count) parameters""
    }}
    Export-ModuleMember -Function Invoke-HighParamCommand
}}
Get-Command -Module 'HighParamModule_PR211' -Name 'Invoke-HighParamCommand'
";

        ps.AddScript(script);
        var results = ps.Invoke();
        var cmd = results.Select(r => r?.BaseObject).OfType<CommandInfo>().FirstOrDefault();

        Assert.NotNull(cmd);
        Assert.Equal(17, cmd!.Parameters.Count);

        return cmd;
    }

    /// <summary>
    /// Creates a synthetic command with object-typed parameters, simulating
    /// the proxy collapse behavior where Export-PSSession types everything as [Object].
    /// 
    /// This tests the EffectiveParameterType() coercion that converts Object→String
    /// for proxy commands.
    /// </summary>
    /// <returns>CommandInfo with object-typed parameters</returns>
    public static CommandInfo CreateObjectParameterCommand()
    {
        using var ps = System.Management.Automation.PowerShell.Create();
        var script = $@"
$module = New-Module -Name 'ObjectParamModule_PR211' -ScriptBlock {{
    function Invoke-ObjectParamCommand {{
        [CmdletBinding()]
        param(
            [Parameter(Mandatory=$false)]
            [object]$Identity,
            [Parameter(Mandatory=$false)]
            [object]$Configuration,
            [Parameter(Mandatory=$false)]
            [object]$Settings
        )
        return ""Object-param function invoked""
    }}
    Export-ModuleMember -Function Invoke-ObjectParamCommand
}}
# Mark the module as an implicit remoting proxy
`$module.PrivateData = @{{ ImplicitRemoting = `$true }}
`$module.Description = 'Implicit remoting for test proxy'
Get-Command -Module 'ObjectParamModule_PR211' -Name 'Invoke-ObjectParamCommand'
";

        ps.AddScript(script);
        var results = ps.Invoke();
        var cmd = results.Select(r => r?.BaseObject).OfType<CommandInfo>().FirstOrDefault();

        Assert.NotNull(cmd);
        var objectParams = cmd!.Parameters.Values.Where(p => p.ParameterType == typeof(object)).ToList();
        Assert.NotEmpty(objectParams);

        return cmd;
    }

    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a synthetic PSModuleInfo with proxy-like shape (used by CreateProxyStyledCommand).
    /// 
    /// This mimics the output of Export-PSSession by setting:
    ///   - PrivateData['ImplicitRemoting'] = true
    ///   - Description to "Implicit remoting for ..."
    ///   - RootModule to "remoteIpMoProxy_*" pattern
    /// </summary>
    private static PSModuleInfo CreateSyntheticProxyModule(
        string moduleName,
        string description,
        string rootModule)
    {
        using var ps = System.Management.Automation.PowerShell.Create();
        var script = $@"
$module = New-Module -Name '{moduleName}' -ScriptBlock {{
    function DummyProxyFunction {{ }}
    Export-ModuleMember -Function DummyProxyFunction
}}
$module
";

        ps.AddScript(script);
        var results = ps.Invoke();
        var module = results.Select(r => r?.BaseObject).OfType<PSModuleInfo>().FirstOrDefault();

        Assert.NotNull(module);

        // Set proxy-like properties using reflection (some are read-only)
        module!.Description = description;
        module.PrivateData = new Hashtable { ["ImplicitRemoting"] = true };
        SetPropertyOrField(module, nameof(PSModuleInfo.RootModule), rootModule);

        return module;
    }

    /// <summary>
    /// Creates a CommandInfo within a module, used as part of fixture setup.
    /// </summary>
    private static CommandInfo CreateCommandInModule(PSModuleInfo module, string commandName)
    {
        // Define a new function in the module that will trigger proxy detection
        using var ps = System.Management.Automation.PowerShell.Create();
        var script = $@"
# Import the module we created
Import-Module (New-Module -Name '{module.Name}' -ScriptBlock {{
    function {commandName} {{
        [CmdletBinding()]
        param(
            [Parameter(Mandatory=$false)]
            [object]$ComputerName,
            [Parameter(Mandatory=$false)]
            [object]$Credential
        )
        ""Proxy-style command executing""
    }}
    Export-ModuleMember -Function {commandName}
}})

Get-Command -Module '{module.Name}' -Name '{commandName}'
";

        ps.AddScript(script);
        var results = ps.Invoke();
        var cmd = results.Select(r => r?.BaseObject).OfType<CommandInfo>().FirstOrDefault();

        Assert.NotNull(cmd);
        return cmd!;
    }

    /// <summary>
    /// Helper to set a property or backing field using reflection.
    /// Used for setting read-only properties on PSModuleInfo like RootModule.
    /// </summary>
    private static void SetPropertyOrField(object target, string name, object value)
    {
        var t = target.GetType();

        // Try public/private property with setter
        var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(target, value);
            return;
        }

        // Try backing field (standard naming: _fieldName or _lowercaseFirstLetter)
        var fieldName = $"_{char.ToLowerInvariant(name[0])}{name.Substring(1)}";
        var field = t.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
                  ?? t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

        if (field != null)
        {
            field.SetValue(target, value);
        }
    }
}
