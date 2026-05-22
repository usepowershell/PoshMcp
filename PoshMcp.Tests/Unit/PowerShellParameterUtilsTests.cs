using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using PoshMcp.Server.PowerShell;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public class PowerShellParameterUtilsTests
{
    [Fact]
    public void ConvertParameterValue_ReturnsNull_ForNullInputToReferenceType()
    {
        var result = PowerShellParameterUtils.ConvertParameterValue(null!, typeof(string), "Name", NullLogger.Instance);

        Assert.Null(result);
    }

    [Fact]
    public void ConvertParameterValue_Throws_ForNullInputToNonNullableValueType()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            PowerShellParameterUtils.ConvertParameterValue(null!, typeof(int), "Count", NullLogger.Instance));

        Assert.Contains("Count", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Int32", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertParameterValue_PassesThrough_StringValue()
    {
        const string rawValue = "hello";

        var result = PowerShellParameterUtils.ConvertParameterValue(rawValue, typeof(string), "Name", NullLogger.Instance);

        Assert.Same(rawValue, result);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("yes", true)]
    [InlineData("no", false)]
    [InlineData("$true", true)]
    [InlineData("$false", false)]
    public void ConvertParameterValue_Converts_BooleanAliases(string rawValue, bool expected)
    {
        var result = PowerShellParameterUtils.ConvertParameterValue(rawValue, typeof(bool), "Enabled", NullLogger.Instance);

        Assert.IsType<bool>(result);
        Assert.Equal(expected, (bool)result);
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData("-7", -7)]
    public void ConvertParameterValue_Converts_IntValues(string rawValue, int expected)
    {
        var result = PowerShellParameterUtils.ConvertParameterValue(rawValue, typeof(int), "Count", NullLogger.Instance);

        Assert.Equal(expected, Assert.IsType<int>(result));
    }

    [Theory]
    [InlineData("922337203685477580", 922337203685477580L)]
    [InlineData("-99", -99L)]
    public void ConvertParameterValue_Converts_LongValues(string rawValue, long expected)
    {
        var result = PowerShellParameterUtils.ConvertParameterValue(rawValue, typeof(long), "Count", NullLogger.Instance);

        Assert.Equal(expected, Assert.IsType<long>(result));
    }

    [Theory]
    [InlineData("3.5", 3.5d)]
    [InlineData("-0.25", -0.25d)]
    public void ConvertParameterValue_Converts_DoubleValues(string rawValue, double expected)
    {
        var result = PowerShellParameterUtils.ConvertParameterValue(rawValue, typeof(double), "Ratio", NullLogger.Instance);

        Assert.Equal(expected, Assert.IsType<double>(result));
    }

    [Theory]
    [InlineData("12.34", "12.34")]
    [InlineData("-5", "-5")]
    public void ConvertParameterValue_Converts_DecimalValues(string rawValue, string expected)
    {
        var result = PowerShellParameterUtils.ConvertParameterValue(rawValue, typeof(decimal), "Amount", NullLogger.Instance);

        Assert.Equal(decimal.Parse(expected), Assert.IsType<decimal>(result));
    }

    [Theory]
    [InlineData("2026-05-22T05:50:13")]
    [InlineData("2026-05-22")]
    public void ConvertParameterValue_Converts_DateTimeValues(string rawValue)
    {
        var result = PowerShellParameterUtils.ConvertParameterValue(rawValue, typeof(DateTime), "When", NullLogger.Instance);

        Assert.Equal(DateTime.Parse(rawValue), Assert.IsType<DateTime>(result));
    }

    [Theory]
    [InlineData("First", TestMode.First)]
    [InlineData("second", TestMode.Second)]
    public void ConvertParameterValue_Converts_EnumValues(string rawValue, TestMode expected)
    {
        var result = PowerShellParameterUtils.ConvertParameterValue(rawValue, typeof(TestMode), "Mode", NullLogger.Instance);

        Assert.Equal(expected, Assert.IsType<TestMode>(result));
    }

    [Fact]
    public void ConvertParameterValue_Converts_ArrayValuesRecursively()
    {
        var rawValue = new object[] { "1", 2, "3" };

        var result = PowerShellParameterUtils.ConvertParameterValue(rawValue, typeof(int[]), "Ids", NullLogger.Instance);

        Assert.Equal(new[] { 1, 2, 3 }, Assert.IsType<int[]>(result));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConvertParameterValue_Converts_SwitchParameter(bool rawValue)
    {
        var result = PowerShellParameterUtils.ConvertParameterValue(rawValue, typeof(SwitchParameter), "Force", NullLogger.Instance);

        Assert.Equal(rawValue, Assert.IsType<SwitchParameter>(result).IsPresent);
    }

    [Fact]
    public void ConvertParameterValue_Uses_ConvertChangeTypeFallback()
    {
        var result = PowerShellParameterUtils.ConvertParameterValue("Z", typeof(char), "Initial", NullLogger.Instance);

        Assert.Equal('Z', Assert.IsType<char>(result));
    }

    [Theory]
    [InlineData("maybe", typeof(bool), "Enabled")]
    [InlineData("not-an-int", typeof(int), "Count")]
    [InlineData("not-a-date", typeof(DateTime), "When")]
    [InlineData("third", typeof(TestMode), "Mode")]
    [InlineData("too long", typeof(char), "Initial")]
    public void ConvertParameterValue_ThrowsArgumentException_ForInvalidConversions(string rawValue, Type targetType, string parameterName)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            PowerShellParameterUtils.ConvertParameterValue(rawValue, targetType, parameterName, NullLogger.Instance));

        Assert.Contains(parameterName, ex.Message, StringComparison.Ordinal);
        Assert.Contains(targetType.Name, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Verbose")]
    [InlineData("Debug")]
    [InlineData("WhatIf")]
    [InlineData("Confirm")]
    [InlineData("ErrorAction")]
    [InlineData("WarningAction")]
    [InlineData("InformationAction")]
    [InlineData("ErrorVariable")]
    [InlineData("WarningVariable")]
    [InlineData("InformationVariable")]
    [InlineData("OutVariable")]
    [InlineData("OutBuffer")]
    [InlineData("PipelineVariable")]
    [InlineData("ProgressAction")]
    public void IsCommonParameter_ReturnsTrue_ForSupportedCommonParameters(string parameterName)
    {
        Assert.True(PowerShellParameterUtils.IsCommonParameter(parameterName));
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("ComputerName")]
    [InlineData("LiteralPath")]
    public void IsCommonParameter_ReturnsFalse_ForNonCommonParameters(string parameterName)
    {
        Assert.False(PowerShellParameterUtils.IsCommonParameter(parameterName));
    }

    [Fact]
    public void ProcessParameter_Throws_WhenMandatoryParameterIsMissing()
    {
        var metadata = new ParameterMetadata("Name", typeof(string));
        metadata.Attributes.Add(new ParameterAttribute { Mandatory = true });

        var ex = Assert.Throws<ArgumentException>(() =>
            PowerShellParameterUtils.ProcessParameter("Name", metadata, new Dictionary<string, object>(), NullLogger.Instance));

        Assert.Contains("Mandatory parameter 'Name' is required", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessParameter_ReturnsNull_WhenOptionalParameterIsMissing()
    {
        var metadata = new ParameterMetadata("Name", typeof(string));

        var result = PowerShellParameterUtils.ProcessParameter("Name", metadata, new Dictionary<string, object>(), NullLogger.Instance);

        Assert.Null(result);
    }

    [Fact]
    public void ProcessParameter_ConvertsProvidedValue()
    {
        var metadata = new ParameterMetadata("Count", typeof(int));
        var arguments = new Dictionary<string, object> { ["Count"] = "42" };

        var result = PowerShellParameterUtils.ProcessParameter("Count", metadata, arguments, NullLogger.Instance);

        Assert.Equal(42, Assert.IsType<int>(result));
    }

    [Fact]
    public void CreateParameterArray_MatchesValuesByParameterName()
    {
        var method = typeof(PowerShellParameterUtilsTests).GetMethod(nameof(NameMatchingTarget), BindingFlags.Static | BindingFlags.NonPublic)!;
        var values = new Dictionary<string, object?>
        {
            ["second"] = "beta",
            ["first"] = "alpha"
        };

        var result = PowerShellParameterUtils.CreateParameterArray(method, values);

        Assert.Equal(new object?[] { "alpha", "beta" }, result);
    }

    [Fact]
    public void CreateParameterArray_WrapsSingleValue_ForArrayParameter()
    {
        var method = typeof(PowerShellParameterUtilsTests).GetMethod(nameof(ArrayWrappingTarget), BindingFlags.Static | BindingFlags.NonPublic)!;
        var values = new Dictionary<string, object?> { ["items"] = "one" };

        var result = PowerShellParameterUtils.CreateParameterArray(method, values);

        var wrapped = Assert.IsType<string[]>(result[0]);
        Assert.Equal(new[] { "one" }, wrapped);
    }

    [Fact]
    public void CreateParameterArray_UsesParameterDefaultValues_WhenInputMissing()
    {
        var method = typeof(PowerShellParameterUtilsTests).GetMethod(nameof(DefaultValueTarget), BindingFlags.Static | BindingFlags.NonPublic)!;
        var values = new Dictionary<string, object?> { ["name"] = "Fry" };

        var result = PowerShellParameterUtils.CreateParameterArray(method, values);

        Assert.Equal("Fry", result[0]);
        Assert.Equal(7, result[1]);
        Assert.Equal("fallback", result[2]);
    }

    [Fact]
    public void CreateParameterArray_UsesTypeDefaults_ForMissingNonNullableValueTypes()
    {
        var method = typeof(PowerShellParameterUtilsTests).GetMethod(nameof(ValueTypeDefaultTarget), BindingFlags.Static | BindingFlags.NonPublic)!;

        var result = PowerShellParameterUtils.CreateParameterArray(method, new Dictionary<string, object?>());

        Assert.Equal(0, result[0]);
        Assert.Equal(default(DateTime), result[1]);
        Assert.Null(result[2]);
    }

    [Fact]
    public void DeserializeFromPowerShellJson_ReturnsSingleObjectWrappedInArray()
    {
        const string json = "{\"Name\":\"Fry\",\"Count\":2,\"Enabled\":true}";

        var result = PowerShellParameterUtils.DeserializeFromPowerShellJson(json);

        var item = Assert.Single(result);
        var obj = Assert.IsType<Dictionary<string, object>>(item);
        Assert.Equal("Fry", obj["Name"]);
        Assert.Equal(2L, Convert.ToInt64(obj["Count"]));
        Assert.Equal(true, obj["Enabled"]);
    }

    [Fact]
    public void DeserializeFromPowerShellJson_ReturnsArrayItems()
    {
        const string json = "[{\"Name\":\"Fry\"},{\"Name\":\"Bender\",\"Tags\":[\"robot\",\"friend\"]}]";

        var result = PowerShellParameterUtils.DeserializeFromPowerShellJson(json);

        Assert.Equal(2, result.Length);
        var second = Assert.IsType<Dictionary<string, object>>(result[1]);
        Assert.Equal("Bender", second["Name"]);
        Assert.Equal(new object[] { "robot", "friend" }, Assert.IsType<object[]>(second["Tags"]));
    }

    [Fact]
    public void DeserializeFromPowerShellJson_ReturnsOriginalString_WhenJsonIsMalformed()
    {
        const string json = "{not-json}";

        var result = PowerShellParameterUtils.DeserializeFromPowerShellJson(json);

        Assert.Equal(new object[] { json }, result);
    }

    private static void NameMatchingTarget(string first, string second)
    {
    }

    private static void ArrayWrappingTarget(string[] items)
    {
    }

    private static void DefaultValueTarget(string name, int count = 7, string note = "fallback")
    {
    }

    private static void ValueTypeDefaultTarget(int count, DateTime when, string? note)
    {
    }

    public enum TestMode
    {
        First,
        Second
    }
}
