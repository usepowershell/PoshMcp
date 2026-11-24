using System.Collections.Immutable;
using System.Text.Json;

namespace PoshMcp.Foundation.Tests;

[Trait("Category", "Unit")]
public sealed class ContractTests
{
    [Fact]
    public void LeaseCannotBeReusedEvenAfterFailure()
    {
        var lease = new SingleUseLease();
        lease.Consume();
        Assert.Throws<InvalidOperationException>(lease.Consume);
    }

    [Fact]
    public void StartupContentAndOrderInvalidateGeneration()
    {
        var profile = new RuntimeProfile("test", RuntimeKind.PowerShell7, "/runtime", "7.6.0", "x64",
            "/work", [new("a.ps1", "aaa"), new("b.ps1", "bbb")], [], ["Test-Command"], ImmutableDictionary<string, string>.Empty);
        Assert.NotEqual(profile.Generation, (profile with { StartupFiles = [new("a.ps1", "changed"), new("b.ps1", "bbb")] }).Generation);
        Assert.NotEqual(profile.Generation, (profile with { StartupFiles = [new("b.ps1", "bbb"), new("a.ps1", "aaa")] }).Generation);
    }

    [Fact]
    public void StructuredInvocationRoundTripsWithoutShellInterpolation()
    {
        var invocation = new Invocation("Test-Command", ImmutableDictionary<string, JsonElement>.Empty
            .Add("Text", JsonSerializer.SerializeToElement("quotes ' \" ;\nUnicode \u03bb")));
        var restored = JsonSerializer.Deserialize<Invocation>(JsonSerializer.Serialize(invocation, Contract.Json), Contract.Json)!;
        Assert.Equal(invocation.Parameters["Text"].GetString(), restored.Parameters["Text"].GetString());
    }
}
