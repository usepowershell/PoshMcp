using System;
using System.Collections.Generic;
using System.Linq;
using PoshMcp.Server.PowerShell;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// FR-511 / SC-208 — Parameter-set consistency tests. The
/// <see cref="HelpAwareToolMetadataSource"/> resolver is the single source of
/// truth for parameter descriptions and is invoked per parameter (NOT per
/// parameter set). This test suite enforces that contract via two layers:
///
/// 1. Resolver determinism — identical
///    <see cref="ParameterDescriptionRequest"/> inputs MUST yield byte-identical
///    <see cref="ParameterDescriptionResult.Description"/> outputs across
///    repeated invocations and across the four FR-510 precedence steps.
///
/// 2. Per-parameter independence — varying the
///    <see cref="ParameterDescriptionRequest.CommandName"/> while holding the
///    parameter inputs constant MUST NOT change the resolved description. This
///    proves the resolver does not key off command identity, which is a
///    necessary condition for FR-511 (same parameter in multiple parameter
///    sets of the same command receives the same description).
///
/// The resolver is a pure function, so byte-identical input/output across
/// invocations is the defining property of "applied per parameter, not per
/// parameter set". Any future change that introduced parameter-set-keyed
/// branching (e.g., adding a ParameterSetName field to the request and
/// branching on it) would break these tests.
/// </summary>
[Trait("Spec", "010")]
public sealed class ParameterSetConsistencyTests : PowerShellTestBase
{
    private readonly HelpAwareToolMetadataSource _resolver = new();

    public ParameterSetConsistencyTests(ITestOutputHelper output) : base(output)
    {
    }

    /// <summary>
    /// FR-510 step 1 + FR-511 — when a parameter has authored
    /// <c>.PARAMETER</c> help text, every parameter-set occurrence of that
    /// parameter MUST resolve to the same description.
    /// </summary>
    [Fact]
    public void HelpParameter_ResolvesIdentically_AcrossParameterSets()
    {
        var request = MakeRequest(
            commandName: "Get-FixtureFullHelp",
            parameterName: "Message",
            helpParameterDescription:
                "The text to echo back to the caller. Used to verify that per-parameter " +
                ".PARAMETER help blocks are surfaced into the MCP parameter description.");

        AssertSameDescriptionAcrossInvocations(request, expectedSource: ParameterDescriptionSource.HelpParameter);
    }

    /// <summary>
    /// FR-510 step 2 + FR-511 — <c>[Parameter(HelpMessage="...")]</c> wins
    /// when no .PARAMETER help is present and resolves identically across
    /// parameter sets.
    /// </summary>
    [Fact]
    public void HelpMessage_ResolvesIdentically_AcrossParameterSets()
    {
        var request = MakeRequest(
            commandName: "Get-FixtureHelpMessageOnly",
            parameterName: "UserId",
            helpMessage: "The user identifier to look up. Sourced from the HelpMessage attribute.");

        AssertSameDescriptionAcrossInvocations(request, expectedSource: ParameterDescriptionSource.HelpMessage);
    }

    /// <summary>
    /// FR-510 step 3 (singleton) + FR-511 — <c>[ValidateSet]</c> on a scalar
    /// parameter resolves to "One of: ..." identically across parameter sets.
    /// </summary>
    [Fact]
    public void ValidateSetSingleton_ResolvesIdentically_AcrossParameterSets()
    {
        var request = MakeRequest(
            commandName: "Get-FixtureValidateSetScalar",
            parameterName: "Color",
            validateSetValues: new[] { "Red", "Green", "Blue" },
            validateSetAppliesToArrayElement: false);

        var result = AssertSameDescriptionAcrossInvocations(
            request, expectedSource: ParameterDescriptionSource.ValidateSet);

        // Spot-check the FR-510 step 3 phrasing.
        Assert.Equal("One of: Red, Green, Blue", result.Description);
    }

    /// <summary>
    /// FR-510 step 3 (array element) + FR-511 — <c>[ValidateSet]</c> on an
    /// array parameter resolves to "Each item is one of: ..." identically.
    /// </summary>
    [Fact]
    public void ValidateSetArrayElement_ResolvesIdentically_AcrossParameterSets()
    {
        var request = MakeRequest(
            commandName: "Get-FixtureValidateSetArray",
            parameterName: "Directions",
            validateSetValues: new[] { "North", "South", "East", "West" },
            validateSetAppliesToArrayElement: true);

        var result = AssertSameDescriptionAcrossInvocations(
            request, expectedSource: ParameterDescriptionSource.ValidateSet);

        Assert.Equal("Each item is one of: North, South, East, West", result.Description);
    }

    /// <summary>
    /// FR-510 step 4 + FR-511 — bare parameters with no help fall back to
    /// "Parameter of type &lt;TypeName&gt;" identically across parameter sets.
    /// </summary>
    [Fact]
    public void TypeFallback_ResolvesIdentically_AcrossParameterSets()
    {
        var request = MakeRequest(
            commandName: "Get-FixtureBare",
            parameterName: "Anything",
            parameterTypeName: "System.String");

        var result = AssertSameDescriptionAcrossInvocations(
            request, expectedSource: ParameterDescriptionSource.TypeFallback);

        Assert.Equal("Parameter of type System.String", result.Description);
    }

    /// <summary>
    /// FR-511 invariant — varying the owning command name while keeping all
    /// parameter inputs identical MUST NOT change the resolved description.
    /// This is a stronger statement than per-invocation determinism: it proves
    /// the resolver does not key descriptions off command identity, which in
    /// turn means it cannot key off parameter-set identity either.
    /// </summary>
    [Fact]
    public void Resolver_IsInsensitiveToCommandIdentity_GivenSameParameterInputs()
    {
        var baseRequest = MakeRequest(
            commandName: "FirstCommand",
            parameterName: "Shared",
            helpParameterDescription: "A description shared across all owners.");

        var alternateOwners = new[] { "SecondCommand", "ThirdCommand", "Some-OtherCommand" };

        var baseline = _resolver.ResolveParameterDescription(baseRequest);

        foreach (var owner in alternateOwners)
        {
            var alt = baseRequest with { CommandName = owner };
            var altResult = _resolver.ResolveParameterDescription(alt);
            Assert.Equal(baseline.Description, altResult.Description);
            Assert.Equal(baseline.Source, altResult.Source);
        }
    }

    /// <summary>
    /// SC-208 — direct simulation of the multi-parameter-set scenario. A
    /// parameter named "Force" appearing on three parameter sets of the same
    /// command (modeled here as three identical resolver requests) MUST
    /// produce a single distinct description.
    /// </summary>
    [Fact]
    public void Parameter_AppearingInMultipleSets_ProducesSingleDescription()
    {
        // Simulate the same parameter being resolved three times — once for
        // each parameter set in which it appears. Inputs to the resolver are
        // identical because the inputs (HelpMessage / ValidateSet / type) are
        // properties of the parameter, not of the parameter set.
        var request = MakeRequest(
            commandName: "Set-FakeService",
            parameterName: "Force",
            helpMessage: "Bypass confirmation prompts.");

        var distinctDescriptions = Enumerable.Range(0, 3)
            .Select(_ => _resolver.ResolveParameterDescription(request).Description)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Single(distinctDescriptions);
        Assert.Equal("Bypass confirmation prompts.", distinctDescriptions[0]);
    }

    /// <summary>
    /// FR-501 invariant for the tool-level resolver — for a given command
    /// (regardless of which parameter set the discovery code is currently
    /// processing), the tool description is a property of the command, not
    /// the parameter set. Mirrors FR-511 at the tool level.
    /// </summary>
    [Fact]
    public void ToolDescription_IsInvariantAcrossParameterSetSyntaxes()
    {
        // Same command, same synopsis, varying ParameterSetSyntax (which
        // would correspond to processing different parameter sets) — the
        // resolved tool description MUST be the same because FR-500 step 1
        // (.Synopsis) takes precedence over FR-500 step 3 (syntax line).
        var requestSetA = new ToolDescriptionRequest(
            CommandName: "Get-Thing",
            ParameterSetName: "ById",
            Synopsis: "Returns a thing.",
            LongDescription: null,
            ParameterSetSyntax: "-Id <int> [-Force]");

        var requestSetB = new ToolDescriptionRequest(
            CommandName: "Get-Thing",
            ParameterSetName: "ByName",
            Synopsis: "Returns a thing.",
            LongDescription: null,
            ParameterSetSyntax: "-Name <string> [-Force]");

        var resultA = _resolver.ResolveToolDescription(requestSetA);
        var resultB = _resolver.ResolveToolDescription(requestSetB);

        Assert.Equal(resultA.Description, resultB.Description);
        Assert.Equal(ToolDescriptionSource.Synopsis, resultA.Source);
        Assert.Equal(ToolDescriptionSource.Synopsis, resultB.Source);
    }

    private ParameterDescriptionResult AssertSameDescriptionAcrossInvocations(
        ParameterDescriptionRequest request,
        ParameterDescriptionSource expectedSource)
    {
        var results = Enumerable.Range(0, 5)
            .Select(_ => _resolver.ResolveParameterDescription(request))
            .ToList();

        var distinctDescriptions = results
            .Select(r => r.Description)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Single(distinctDescriptions);
        Assert.False(string.IsNullOrWhiteSpace(distinctDescriptions[0]));
        Assert.All(results, r => Assert.Equal(expectedSource, r.Source));
        return results[0];
    }

    private static ParameterDescriptionRequest MakeRequest(
        string commandName,
        string parameterName,
        string parameterTypeName = "System.Object",
        string? helpParameterDescription = null,
        string? helpMessage = null,
        IReadOnlyList<string>? validateSetValues = null,
        bool validateSetAppliesToArrayElement = false)
        => new(
            CommandName: commandName,
            ParameterName: parameterName,
            ParameterTypeName: parameterTypeName,
            HelpParameterDescription: helpParameterDescription,
            HelpMessage: helpMessage,
            ValidateSetValues: validateSetValues,
            ValidateSetAppliesToArrayElement: validateSetAppliesToArrayElement);
}
