using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public class ConfigurationFileManagerTests
{
    private static readonly MethodInfo IsYesAnswerMethod = typeof(ConfigurationFileManager)
        .GetMethod("IsYesAnswer", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException("ConfigurationFileManager.IsYesAnswer(string?) not found.");

    private static readonly MethodInfo AddUniqueValuesMethod = typeof(ConfigurationFileManager)
        .GetMethod("AddUniqueValues", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException("ConfigurationFileManager.AddUniqueValues(JsonArray, IEnumerable<string>, out List<string>) not found.");

    private static readonly MethodInfo RemoveValuesMethod = typeof(ConfigurationFileManager)
        .GetMethod("RemoveValues", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException("ConfigurationFileManager.RemoveValues(JsonArray, IEnumerable<string>) not found.");

    [Theory]
    [InlineData("json", "json")]
    [InlineData("JSON", "json")]
    [InlineData("JsOn", "json")]
    [InlineData(null, "text")]
    [InlineData("", "text")]
    [InlineData("text", "text")]
    [InlineData(" json ", "text")]
    public void NormalizeFormat_ReturnsExpectedValue(string? input, string expected)
    {
        var result = ConfigurationFileManager.NormalizeFormat(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData(" false ", false)]
    public void TryParseRequiredBoolean_ReturnsExpectedValue(string? input, bool? expected)
    {
        var result = ConfigurationFileManager.TryParseRequiredBoolean(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void TryParseRequiredBoolean_InvalidValue_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() => ConfigurationFileManager.TryParseRequiredBoolean("maybe"));

        Assert.Equal("Expected 'true' or 'false' but received 'maybe'.", exception.Message);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("in-process", "InProcess")]
    [InlineData("INPROCESS", "InProcess")]
    [InlineData(" inprocess ", "InProcess")]
    [InlineData("out-of-process", "OutOfProcess")]
    [InlineData("OUTOFPROCESS", "OutOfProcess")]
    [InlineData(" out-of-process ", "OutOfProcess")]
    public void NormalizeRuntimeMode_ReturnsExpectedValue(string? input, string? expected)
    {
        var result = ConfigurationFileManager.NormalizeRuntimeMode(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeRuntimeMode_InvalidValue_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() => ConfigurationFileManager.NormalizeRuntimeMode("background"));

        Assert.Equal("Expected 'in-process' or 'out-of-process' but received 'background'.", exception.Message);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("y", true)]
    [InlineData("Y", true)]
    [InlineData("yes", true)]
    [InlineData("YES", true)]
    [InlineData(" yes ", true)]
    [InlineData("n", false)]
    [InlineData("no", false)]
    [InlineData("true", false)]
    public void IsYesAnswer_ReturnsExpectedValue(string? input, bool expected)
    {
        var result = InvokeIsYesAnswer(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void AddUniqueValues_AddsOnlyTrimmedUniqueValues_CaseInsensitively()
    {
        var array = new JsonArray(" Alpha ", null, "   ");

        var count = InvokeAddUniqueValues(array, new[] { "alpha", " beta ", "BETA", "", "gamma", "   " }, out var addedValues);

        Assert.Equal(2, count);
        Assert.Equal(new[] { "beta", "gamma" }, addedValues);
        Assert.Equal(new string?[] { " Alpha ", null, "   ", "beta", "gamma" }, GetArrayValues(array));
    }

    [Fact]
    public void AddUniqueValues_EmptyInput_DoesNotChangeArray()
    {
        var array = new JsonArray();

        var count = InvokeAddUniqueValues(array, Array.Empty<string>(), out var addedValues);

        Assert.Equal(0, count);
        Assert.Empty(addedValues);
        Assert.Empty(array);
    }

    [Fact]
    public void RemoveValues_RemovesAllMatchingValues_CaseInsensitively()
    {
        var array = new JsonArray("alpha", " Beta ", null, "   ", "ALPHA", "gamma");

        var count = InvokeRemoveValues(array, new[] { " alpha ", "BETA", "", "delta" });

        Assert.Equal(3, count);
        Assert.Equal(new string?[] { null, "   ", "gamma" }, GetArrayValues(array));
    }

    [Fact]
    public void RemoveValues_EmptyInput_DoesNotChangeArray()
    {
        var array = new JsonArray("alpha");

        var count = InvokeRemoveValues(array, new[] { "", "   " });

        Assert.Equal(0, count);
        Assert.Equal(new[] { "alpha" }, GetArrayValues(array));
    }

    private static bool InvokeIsYesAnswer(string? input)
    {
        return (bool)IsYesAnswerMethod.Invoke(null, new object?[] { input })!;
    }

    private static int InvokeAddUniqueValues(JsonArray array, IEnumerable<string> values, out List<string> addedValues)
    {
        var args = new object?[] { array, values, null };
        var result = (int)AddUniqueValuesMethod.Invoke(null, args)!;
        addedValues = (List<string>)args[2]!;
        return result;
    }

    private static int InvokeRemoveValues(JsonArray array, IEnumerable<string> values)
    {
        return (int)RemoveValuesMethod.Invoke(null, new object[] { array, values })!;
    }

    private static string?[] GetArrayValues(JsonArray array)
    {
        return array.Select(node => node?.GetValue<string>()).ToArray();
    }
}
