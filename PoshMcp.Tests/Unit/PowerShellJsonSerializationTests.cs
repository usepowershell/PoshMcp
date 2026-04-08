using System.Collections;
using System.Management.Automation;
using System.Text;
using System.Text.Json;
using PoshMcp.Server.PowerShell;
using Xunit;

namespace PoshMcp.Tests.Unit;

public class PowerShellJsonSerializationTests
{
    [Fact]
    public void PowerShellStringResults_SerializeAsJsonStrings()
    {
        var results = new[] { PSObject.AsPSObject("FromUnitTest") };

        var json = JsonSerializer.Serialize(results, PowerShellJsonOptions.Options);

        Assert.Equal("[\"FromUnitTest\"]", json);
    }

    [Fact]
    public void PowerShellComplexResults_SerializeAsObjects()
    {
        var results = new[] { PSObject.AsPSObject(new { Name = "Widget", Count = 2 }) };

        var json = JsonSerializer.Serialize(results, PowerShellJsonOptions.Options);

        Assert.Contains("\"Name\":\"Widget\"", json);
        Assert.Contains("\"Count\":2", json);
    }

    [Fact]
    public void PowerShellHashtableResults_PreserveUserKeys()
    {
        var results = new[]
        {
            PSObject.AsPSObject(new Hashtable
            {
                ["Name"] = "dotnet",
                ["Type"] = "process"
            })
        };

        var json = JsonSerializer.Serialize(results, PowerShellJsonOptions.Options);

        Assert.Contains("\"Name\":\"dotnet\"", json);
        Assert.Contains("\"Type\":\"process\"", json);
        Assert.DoesNotContain("\"IsReadOnly\"", json);
        Assert.DoesNotContain("\"Keys\"", json);
    }

    [Fact]
    public void PowerShellNestedClrResults_NormalizeUnsupportedMembers()
    {
        var results = new[]
        {
            PSObject.AsPSObject(new
            {
                Name = "Widget",
                Encoding = Encoding.UTF8
            })
        };

        var json = JsonSerializer.Serialize(results, PowerShellJsonOptions.Options);

        Assert.Contains("\"Name\":\"Widget\"", json);
        Assert.Contains("\"Encoding\":", json);
        Assert.DoesNotContain("\"Preamble\"", json);
    }
}