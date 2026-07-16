using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// FR-521 — Path parity tests. Verifies that the InProcess and OutOfProcess
/// runtime paths produce byte-identical tool descriptions and per-parameter
/// descriptions for every command in the HelpParityFixture corpus.
///
/// Equality is exact string equality on the post-FR-540 sanitized form, scoped
/// to the MCP fields <c>tools[].description</c> and
/// <c>tools[].inputSchema.properties.&lt;name&gt;.description</c>. Tool names,
/// types, enum, mandatory, and array shape are NOT compared (out of scope per
/// FR-551 and the schema-generation Non-Goal).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Spec", "010")]
[Collection("Tool Description Fixtures")]
public sealed class ToolDescriptionParityTests : PowerShellTestBase
{
    private readonly ToolDescriptionFixture _fixture;

    public ToolDescriptionParityTests(ToolDescriptionFixture fixture, ITestOutputHelper output) : base(output)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// FR-521 — top-level fixture-command-count parity assertion. The two modes
    /// MUST expose the same set of fixture-command tool variants (matched by the
    /// PowerShell command name carried on the MCP <c>title</c> field). The total
    /// tool count between modes can differ (different built-in module surfaces),
    /// but the fixture corpus must round-trip identically.
    /// </summary>
    [PwshAvailableFact]
    public void FixtureCommands_AppearInBothModes_WithSameToolVariantCount()
    {
        if (!IsOopAvailable()) return;

        foreach (var commandName in HelpParityFixtureSession.FixtureCommands)
        {
            var inProcessVariants = _fixture.InProcessSession!.GetToolsForCommand(commandName);
            var outOfProcessVariants = _fixture.OutOfProcessSession!.GetToolsForCommand(commandName);

            Assert.True(
                inProcessVariants.Count > 0,
                $"InProcess mode produced no tool for fixture command '{commandName}'");
            Assert.True(
                outOfProcessVariants.Count > 0,
                $"OutOfProcess mode produced no tool for fixture command '{commandName}'");
            Assert.Equal(inProcessVariants.Count, outOfProcessVariants.Count);
        }
    }

    /// <summary>
    /// FR-521 — per-fixture-command tool description parity. For each command in
    /// the fixture corpus, every tool variant present in BOTH modes must carry
    /// identical <c>description</c> text. Per FR-501 all parameter sets of a
    /// command share the same description, so a single command-level comparison
    /// is sufficient.
    /// </summary>
    [PwshAvailableTheory]
    [InlineData("Get-FixtureSynopsisOnly")]
    [InlineData("Get-FixtureFullHelp")]
    [InlineData("Get-FixtureHelpMessageOnly")]
    [InlineData("Get-FixtureValidateSetScalar")]
    [InlineData("Get-FixtureValidateSetArray")]
    [InlineData("Get-FixtureBare")]
    public void ToolDescription_IsByteIdentical_AcrossRuntimeModes(string commandName)
    {
        if (!IsOopAvailable()) return;

        var inProcess = _fixture.InProcessSession!.GetToolsForCommand(commandName);
        var outOfProcess = _fixture.OutOfProcessSession!.GetToolsForCommand(commandName);

        Assert.NotEmpty(inProcess);
        Assert.NotEmpty(outOfProcess);

        // FR-501: all variants of one command share one description, so any
        // representative comparison suffices. Also assert intra-mode invariant.
        var inProcessDescriptions = inProcess
            .Select(t => t["description"]?.ToString() ?? string.Empty)
            .Distinct()
            .ToList();
        var outOfProcessDescriptions = outOfProcess
            .Select(t => t["description"]?.ToString() ?? string.Empty)
            .Distinct()
            .ToList();

        Assert.Single(inProcessDescriptions);
        Assert.Single(outOfProcessDescriptions);
        Assert.Equal(inProcessDescriptions[0], outOfProcessDescriptions[0]);
    }

    /// <summary>
    /// FR-521 — per-parameter description parity. For each parameter on each
    /// fixture-command tool variant present in BOTH modes, the
    /// <c>inputSchema.properties.&lt;name&gt;.description</c> field must be
    /// byte-identical between modes. A missing description and an empty-string
    /// description are normalized to the empty string for comparison.
    /// </summary>
    [PwshAvailableTheory]
    [InlineData("Get-FixtureSynopsisOnly")]
    [InlineData("Get-FixtureFullHelp")]
    [InlineData("Get-FixtureHelpMessageOnly")]
    [InlineData("Get-FixtureValidateSetScalar")]
    [InlineData("Get-FixtureValidateSetArray")]
    [InlineData("Get-FixtureBare")]
    public void ParameterDescriptions_AreByteIdentical_AcrossRuntimeModes(string commandName)
    {
        if (!IsOopAvailable()) return;

        var inProcessVariants = _fixture.InProcessSession!.GetToolsForCommand(commandName);
        var outOfProcessVariants = _fixture.OutOfProcessSession!.GetToolsForCommand(commandName);

        // Match variants by tool name (e.g., "get_fixturefullhelp") which is
        // derived deterministically from the source command + parameter set.
        var inByName = inProcessVariants.ToDictionary(t => t["name"]!.ToString());
        var outByName = outOfProcessVariants.ToDictionary(t => t["name"]!.ToString());
        var common = inByName.Keys.Intersect(outByName.Keys).ToList();

        Assert.True(
            common.Count > 0,
            $"No tool-variant names overlap between modes for command '{commandName}'.\n" +
            $"  InProcess names: [{string.Join(", ", inByName.Keys)}]\n" +
            $"  OutOfProcess names: [{string.Join(", ", outByName.Keys)}]");

        foreach (var toolName in common)
        {
            var inProps = ParameterDescriptionsOf(inByName[toolName]);
            var outProps = ParameterDescriptionsOf(outByName[toolName]);

            // Compare the union of parameter names so a missing description on
            // one side is surfaced as a parity failure rather than skipped.
            var allParams = inProps.Keys.Union(outProps.Keys).OrderBy(n => n).ToList();
            foreach (var paramName in allParams)
            {
                inProps.TryGetValue(paramName, out var inDesc);
                outProps.TryGetValue(paramName, out var outDesc);
                Assert.True(
                    string.Equals(inDesc ?? string.Empty, outDesc ?? string.Empty, System.StringComparison.Ordinal),
                    $"Parity mismatch on tool '{toolName}', parameter '{paramName}':\n" +
                    $"  InProcess  : '{inDesc ?? "<missing>"}'\n" +
                    $"  OutOfProcess: '{outDesc ?? "<missing>"}'");
            }
        }
    }

    /// <summary>
    /// FR-510 / Cubert's PR #241 finding — bug-surface assertion. Commands with
    /// authored help text MUST produce non-empty parameter descriptions. This
    /// test will FAIL until the precedence chain is wired through to the
    /// generated <c>inputSchema.properties.&lt;name&gt;.description</c> field
    /// in both runtime paths. Do NOT weaken this assertion — its failure is the
    /// concrete signal that the gap remains.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Get-FixtureFullHelp.Message → FR-510 step 1 (.PARAMETER block).</item>
    /// <item>Get-FixtureFullHelp.Count   → FR-510 step 1 (.PARAMETER block).</item>
    /// <item>Get-FixtureHelpMessageOnly.UserId → FR-510 step 2 (HelpMessage attr).</item>
    /// <item>Get-FixtureValidateSetScalar.Color → FR-510 step 3 (singleton).</item>
    /// <item>Get-FixtureValidateSetArray.Directions → FR-510 step 3 (array).</item>
    /// </list>
    /// </remarks>
    [Theory]
    [InlineData("Get-FixtureFullHelp", "Message")]
    [InlineData("Get-FixtureFullHelp", "Count")]
    [InlineData("Get-FixtureHelpMessageOnly", "UserId")]
    [InlineData("Get-FixtureValidateSetScalar", "Color")]
    [InlineData("Get-FixtureValidateSetArray", "Directions")]
    public void ParameterDescription_IsNonEmpty_WhenHelpTextAvailable_InProcessMode(
        string commandName, string parameterName)
    {
        Assert.NotNull(_fixture.InProcessSession);

        var variants = _fixture.InProcessSession!.GetToolsForCommand(commandName);
        Assert.NotEmpty(variants);

        // The parameter must have a non-empty description in at least one variant
        // it appears in. (Per FR-511 it must be identical across all variants
        // where it appears; that invariant is checked separately by
        // ParameterSetConsistencyTests.)
        var variantsWithParam = variants
            .Where(t => ParameterDescriptionsOf(t).ContainsKey(parameterName))
            .ToList();

        Assert.True(
            variantsWithParam.Count > 0,
            $"Parameter '{parameterName}' did not appear on any tool variant of '{commandName}'.\n" +
            "  Variants: " +
            string.Join(", ", variants.Select(t => t["name"]?.ToString())));

        foreach (var variant in variantsWithParam)
        {
            var desc = ParameterDescriptionsOf(variant)[parameterName];
            Assert.False(
                string.IsNullOrWhiteSpace(desc),
                $"Tool '{variant["name"]}' parameter '{parameterName}' has empty description in InProcess mode.\n" +
                "  Expected: non-empty text sourced from the FR-510 precedence chain.\n" +
                "  Actual:   '" + (desc ?? "<missing>") + "'\n" +
                "  This confirms the FR-510 wiring gap reported in PR #241.");
        }
    }

    /// <summary>
    /// Same FR-510 non-empty assertion as above, executed through the
    /// OutOfProcess path so the gap is surfaced in BOTH runtimes when present.
    /// Skipped when pwsh is not on PATH.
    /// </summary>
    [PwshAvailableTheory]
    [InlineData("Get-FixtureFullHelp", "Message")]
    [InlineData("Get-FixtureFullHelp", "Count")]
    [InlineData("Get-FixtureHelpMessageOnly", "UserId")]
    [InlineData("Get-FixtureValidateSetScalar", "Color")]
    [InlineData("Get-FixtureValidateSetArray", "Directions")]
    public void ParameterDescription_IsNonEmpty_WhenHelpTextAvailable_OutOfProcessMode(
        string commandName, string parameterName)
    {
        if (!IsOopAvailable()) return;

        var variants = _fixture.OutOfProcessSession!.GetToolsForCommand(commandName);
        Assert.NotEmpty(variants);

        var variantsWithParam = variants
            .Where(t => ParameterDescriptionsOf(t).ContainsKey(parameterName))
            .ToList();
        Assert.True(
            variantsWithParam.Count > 0,
            $"Parameter '{parameterName}' did not appear on any tool variant of '{commandName}' (OOP).");

        foreach (var variant in variantsWithParam)
        {
            var desc = ParameterDescriptionsOf(variant)[parameterName];
            Assert.False(
                string.IsNullOrWhiteSpace(desc),
                $"Tool '{variant["name"]}' parameter '{parameterName}' has empty description in OOP mode.\n" +
                "  Expected: non-empty text sourced from the FR-510 precedence chain.\n" +
                "  Actual:   '" + (desc ?? "<missing>") + "'\n" +
                "  This confirms the FR-510 wiring gap reported in PR #241.");
        }
    }

    private static Dictionary<string, string> ParameterDescriptionsOf(JObject tool)
    {
        var result = new Dictionary<string, string>(System.StringComparer.Ordinal);
        var props = tool["inputSchema"]?["properties"] as JObject;
        if (props is null) return result;

        foreach (var prop in props.Properties())
        {
            // Synthetic helper params like _AllProperties, _MaxResults,
            // _RequestedProperties are out of scope for spec 010.
            if (prop.Name.StartsWith('_')) continue;

            var desc = prop.Value?["description"]?.ToString();
            result[prop.Name] = desc ?? string.Empty;
        }
        return result;
    }

    private bool IsOopAvailable()
    {
        if (_fixture.IsOutOfProcessAvailable)
        {
            return true;
        }

        Logger.LogWarning(
            "OutOfProcess parity assertions are not run because the fixture session was not initialized. Reason: {Reason}",
            _fixture.OutOfProcessUnavailableReason ?? "unknown");
        return false;
    }
}
