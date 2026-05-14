// Unit tests for the WinPSCompatSession proxy detection and dynamic delegate
// emission code in PoshMcp.Server.
//
// Coverage:
//   * PowerShellParameterUtils.IsImplicitRemotingProxy
//   * PowerShellParameterUtils.EffectiveParameterType
//   * McpToolFactoryV2.GetDelegateTypeForMethod (>16-param dynamic emit path)
//
// These tests do NOT depend on the WinPSCompatSession bridge actually being
// available (which would require a Windows host with a Desktop-only module
// installed). Instead, the proxy-detection tests construct a synthetic
// PSModuleInfo with the same PrivateData/Description/RootModule shape that
// Export-PSSession produces, and call the helpers directly.

using System;
using System.Collections;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Threading.Tasks;
using PoshMcp.Server.PowerShell;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public class WinPsCompatProxyTests
{
    private readonly ITestOutputHelper _output;

    public WinPsCompatProxyTests(ITestOutputHelper output) => _output = output;

    // ── IsImplicitRemotingProxy ─────────────────────────────────────────────

    [Fact]
    public void IsImplicitRemotingProxy_ReturnsTrue_WhenPrivateDataMarkerIsBoolTrue()
    {
        var cmd = BuildCommandInfo(
            moduleName: "FakeProxyModule",
            moduleType: ModuleType.Script,
            description: "anything",
            rootModule: "anything.psm1",
            privateData: new Hashtable { ["ImplicitRemoting"] = true });

        Assert.True(PowerShellParameterUtils.IsImplicitRemotingProxy(cmd));
    }

    [Fact]
    public void IsImplicitRemotingProxy_ReturnsTrue_WhenPrivateDataMarkerIsStringTrue()
    {
        // Some hosts persist hashtable values via PSSerializer and the bool
        // round-trips as the literal string "True".
        var cmd = BuildCommandInfo(
            moduleName: "FakeProxyModule",
            moduleType: ModuleType.Script,
            privateData: new Hashtable { ["ImplicitRemoting"] = "True" });

        Assert.True(PowerShellParameterUtils.IsImplicitRemotingProxy(cmd));
    }

    [Fact]
    public void IsImplicitRemotingProxy_ReturnsTrue_WhenDescriptionStartsWithImplicitRemotingFor()
    {
        // Fallback signal — used when PrivateData is empty or doesn't contain the marker.
        var cmd = BuildCommandInfo(
            moduleName: "FakeProxyModule",
            moduleType: ModuleType.Script,
            description: "Implicit remoting for http://localhost:1234/wsman",
            privateData: null);

        Assert.True(PowerShellParameterUtils.IsImplicitRemotingProxy(cmd));
    }

    [Fact]
    public void IsImplicitRemotingProxy_ReturnsTrue_WhenRootModuleHasRemoteIpMoProxyPrefix()
    {
        // Real WinPSCompat proxy modules have RootModule names of the form
        // remoteIpMoProxy_<OriginalModule>_<Version>_<Host>_<Guid>.psm1.
        var cmd = BuildCommandInfo(
            moduleName: "FakeProxyModule",
            moduleType: ModuleType.Script,
            rootModule: "remoteIpMoProxy_OriginalModule_1.0.0.0_localhost_xxxx.psm1",
            privateData: null);

        Assert.True(PowerShellParameterUtils.IsImplicitRemotingProxy(cmd));
    }

    [Fact]
    public void IsImplicitRemotingProxy_ReturnsFalse_ForNativePs7Cmdlet()
    {
        // A real native cmdlet from a manifest module — none of the proxy signals fire.
        using var ps = System.Management.Automation.PowerShell.Create();
        ps.AddCommand("Get-Command").AddParameter("Name", "Get-Date");
        var cmd = ps.Invoke<CommandInfo>().FirstOrDefault();
        Assert.NotNull(cmd);

        Assert.False(PowerShellParameterUtils.IsImplicitRemotingProxy(cmd!));
    }

    [Fact]
    public void IsImplicitRemotingProxy_ReturnsFalse_ForCommandWithNullModule()
    {
        // Some CommandInfo objects (synthetic / scriptblock-backed) have no Module.
        var cmd = BuildCommandInfoNoModule();

        Assert.False(PowerShellParameterUtils.IsImplicitRemotingProxy(cmd));
    }

    [Fact]
    public void IsImplicitRemotingProxy_ReturnsFalse_WhenPrivateDataMarkerIsFalse()
    {
        // The marker exists but is explicitly false — must not be treated as a proxy.
        var cmd = BuildCommandInfo(
            moduleName: "MyScriptModule",
            moduleType: ModuleType.Script,
            privateData: new Hashtable { ["ImplicitRemoting"] = false });

        Assert.False(PowerShellParameterUtils.IsImplicitRemotingProxy(cmd));
    }

    // ── EffectiveParameterType ──────────────────────────────────────────────

    [Fact]
    public void EffectiveParameterType_ReturnsString_ForObjectParameter_OnProxyCmdlet()
    {
        var cmd = BuildCommandInfo(
            moduleName: "FakeProxyModule",
            moduleType: ModuleType.Script,
            privateData: new Hashtable { ["ImplicitRemoting"] = true });
        var pm = new ParameterMetadata("ApplicationName", typeof(object));

        var effective = PowerShellParameterUtils.EffectiveParameterType(cmd, pm);

        Assert.Equal(typeof(string), effective);
    }

    [Fact]
    public void EffectiveParameterType_PreservesDeclaredType_ForObjectParameter_OnNativeCmdlet()
    {
        // Same Object-typed param but on a non-proxy cmdlet — must NOT substitute.
        // The existing IsUnserializableType filter will then drop it (correct behavior
        // for a genuine [Object] parameter on a native cmdlet, where Object usually
        // means "I have no idea what type this is").
        using var ps = System.Management.Automation.PowerShell.Create();
        ps.AddCommand("Get-Command").AddParameter("Name", "Get-Date");
        var cmd = ps.Invoke<CommandInfo>().FirstOrDefault();
        Assert.NotNull(cmd);

        var pm = new ParameterMetadata("Whatever", typeof(object));

        var effective = PowerShellParameterUtils.EffectiveParameterType(cmd!, pm);

        Assert.Equal(typeof(object), effective);
    }

    [Fact]
    public void EffectiveParameterType_PreservesDeclaredType_ForNonObjectParameter_OnProxyCmdlet()
    {
        // SwitchParameter and other typed params on proxies are preserved as-is —
        // the proxy already kept their type, so we only substitute when the proxy
        // collapsed everything to [Object].
        var cmd = BuildCommandInfo(
            moduleName: "FakeProxyModule",
            moduleType: ModuleType.Script,
            privateData: new Hashtable { ["ImplicitRemoting"] = true });
        var pm = new ParameterMetadata("Adhoc", typeof(SwitchParameter));

        var effective = PowerShellParameterUtils.EffectiveParameterType(cmd, pm);

        Assert.Equal(typeof(SwitchParameter), effective);
    }

    // ── GetDelegateTypeForMethod / dynamic delegate emit ────────────────────

    [Fact]
    public void GetDelegateTypeForMethod_UsesBclFunc_For16OrFewerParameters()
    {
        // Sanity: pre-existing fast path is preserved for the common case.
        // Build a method with 16 string parameters returning Task<string>.
        var method = typeof(WinPsCompatProxyTests)
            .GetMethod(nameof(SixteenParamMethod), BindingFlags.Static | BindingFlags.Public)!;

        var factory = new McpToolFactoryV2();
        var delegateType = InvokeGetDelegateTypeForMethod(factory, method);

        Assert.True(delegateType.FullName!.StartsWith("System.Func`17"),
            $"Expected System.Func`17 for 16-param method; got {delegateType.FullName}");
    }

    [Fact]
    public void GetDelegateTypeForMethod_EmitsDynamicDelegate_ForMoreThan16Parameters()
    {
        // Anything beyond Func`17 (the largest BCL Func arity) must use the
        // runtime-emitted delegate path.
        var method = typeof(WinPsCompatProxyTests)
            .GetMethod(nameof(SeventeenParamMethod), BindingFlags.Static | BindingFlags.Public)!;

        var factory = new McpToolFactoryV2();
        var delegateType = InvokeGetDelegateTypeForMethod(factory, method);

        Assert.True(typeof(MulticastDelegate).IsAssignableFrom(delegateType));
        Assert.False(delegateType.FullName!.StartsWith("System.Func"),
            $"Expected emitted delegate for 17-param method; got {delegateType.FullName}");

        // Invoke method must have the same arity + return type as the original.
        var invoke = delegateType.GetMethod("Invoke")!;
        Assert.Equal(17, invoke.GetParameters().Length);
        Assert.Equal(typeof(Task<string>), invoke.ReturnType);
    }

    [Fact]
    public void GetDelegateTypeForMethod_PreservesParameterNames_OnEmittedDelegate()
    {
        // The MCP schema generator uses parameter names from the delegate's Invoke
        // method. If we lose names in the emit step, every property in the JSON
        // Schema becomes empty-string-keyed and tools fail.
        var method = typeof(WinPsCompatProxyTests)
            .GetMethod(nameof(SeventeenParamMethod), BindingFlags.Static | BindingFlags.Public)!;

        var factory = new McpToolFactoryV2();
        var delegateType = InvokeGetDelegateTypeForMethod(factory, method);

        var invoke = delegateType.GetMethod("Invoke")!;
        var emittedNames = invoke.GetParameters().Select(p => p.Name).ToArray();
        var originalNames = method.GetParameters().Select(p => p.Name).ToArray();

        Assert.Equal(originalNames, emittedNames);
    }

    [Fact]
    public void GetDelegateTypeForMethod_CachesBySignature_ReturnsSameTypeForRepeatedCalls()
    {
        // The dynamic-delegate cache must return the same Type for the same
        // signature so we don't leak Type instances into the non-collectible
        // AssemblyBuilderAccess.Run module on every tool registration.
        var method = typeof(WinPsCompatProxyTests)
            .GetMethod(nameof(SeventeenParamMethod), BindingFlags.Static | BindingFlags.Public)!;

        var factory = new McpToolFactoryV2();
        var first = InvokeGetDelegateTypeForMethod(factory, method);
        var second = InvokeGetDelegateTypeForMethod(factory, method);

        Assert.Same(first, second);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a synthetic CommandInfo whose Module exposes the given proxy-shape
    /// signals (Description, RootModule, PrivateData). Uses PowerShell to load a
    /// throwaway in-memory module so we get a real PSModuleInfo without depending
    /// on WinPSCompatSession actually being available.
    /// </summary>
    private static CommandInfo BuildCommandInfo(
        string moduleName,
        ModuleType moduleType,
        string? description = null,
        string? rootModule = null,
        Hashtable? privateData = null)
    {
        // We construct a real PSModuleInfo by loading a tiny dynamic module that
        // exports a single dummy function, then mutate its writable properties
        // to mirror the proxy shape we want to test.
        using var ps = System.Management.Automation.PowerShell.Create();
        var script = $@"
$module = New-Module -Name '{moduleName}' -ScriptBlock {{
    function script:DummyCommand {{ }}
    Export-ModuleMember -Function DummyCommand
}}
$module
Get-Command -Module '{moduleName}' -Name 'DummyCommand'
";
        ps.AddScript(script);
        var results = ps.Invoke();
        // PowerShell.Invoke() returns PSObject wrappers; unwrap via BaseObject.
        // (OfType<PSModuleInfo>() on the raw PSObject collection is always empty —
        // see CA2021 — because PSModuleInfo is the BaseObject, not the PSObject.)
        var module = results.Select(r => r?.BaseObject).OfType<PSModuleInfo>().FirstOrDefault();
        var cmd = results.Select(r => r?.BaseObject).OfType<CommandInfo>().FirstOrDefault();

        Assert.NotNull(module);
        Assert.NotNull(cmd);

        // PSModuleInfo properties Description / PrivateData are writable; ModuleType
        // and RootModule are reflectively settable via internal setters or backing
        // fields. We use reflection because public setters are limited.
        if (description != null) module!.Description = description;
        if (privateData != null) module!.PrivateData = privateData;
        if (rootModule != null) SetPropertyOrField(module!, nameof(PSModuleInfo.RootModule), rootModule);
        SetPropertyOrField(module!, nameof(PSModuleInfo.ModuleType), moduleType);

        return cmd!;
    }

    /// <summary>
    /// Build a CommandInfo with no Module (some scriptblock-backed function infos
    /// have this shape). Verifies the helper doesn't NRE.
    /// </summary>
    private static CommandInfo BuildCommandInfoNoModule()
    {
        using var ps = System.Management.Automation.PowerShell.Create();
        ps.AddScript("function script:NoModuleCmd { } ; Get-Command NoModuleCmd");
        var cmd = ps.Invoke<CommandInfo>().FirstOrDefault();
        Assert.NotNull(cmd);
        // No Assert.Null on Module — different PowerShell versions may attach an
        // anonymous module. The point is just that the helper handles whatever
        // it gets without throwing.
        return cmd!;
    }

    private static void SetPropertyOrField(object target, string name, object value)
    {
        var t = target.GetType();
        var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (prop != null && prop.CanWrite) { prop.SetValue(target, value); return; }
        var field = t.GetField($"_{char.ToLowerInvariant(name[0])}{name.Substring(1)}",
                               BindingFlags.NonPublic | BindingFlags.Instance)
                  ?? t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        field?.SetValue(target, value);
    }

    private static Type InvokeGetDelegateTypeForMethod(McpToolFactoryV2 factory, MethodInfo method)
    {
        var helper = typeof(McpToolFactoryV2).GetMethod(
            "GetDelegateTypeForMethod",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(helper);
        return (Type)helper!.Invoke(factory, new object[] { method })!;
    }

    // 16 parameters — should still use the BCL Func<>17 fast path.
    public static Task<string> SixteenParamMethod(
        string p1, string p2, string p3, string p4, string p5, string p6,
        string p7, string p8, string p9, string p10, string p11, string p12,
        string p13, string p14, string p15, string p16) => Task.FromResult(string.Empty);

    // 17 parameters — beyond Func`17, must trigger the dynamic delegate path.
    public static Task<string> SeventeenParamMethod(
        string p1, string p2, string p3, string p4, string p5, string p6,
        string p7, string p8, string p9, string p10, string p11, string p12,
        string p13, string p14, string p15, string p16, string p17) => Task.FromResult(string.Empty);
}
