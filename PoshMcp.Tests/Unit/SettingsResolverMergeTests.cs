using System;
using System.Reflection;
using System.Text.Json.Nodes;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public class SettingsResolverMergeTests
{
    private static readonly MethodInfo MergeMissingPropertiesMethod = typeof(SettingsResolver).GetMethod(
        "MergeMissingProperties",
        BindingFlags.NonPublic | BindingFlags.Static,
        binder: null,
        types: new[] { typeof(JsonObject), typeof(JsonObject) },
        modifiers: null)
        ?? throw new MissingMethodException(
            "SettingsResolver.MergeMissingProperties(JsonObject, JsonObject) not found — was the method renamed or its signature changed?");

    [Fact]
    public void MergeMissingProperties_AddsMissingTopLevelKeyFromDefaults()
    {
        var defaults = ParseObject("""
            {
              "transport": "stdio"
            }
            """);
        var target = ParseObject("""
            {
              "logLevel": "Information"
            }
            """);

        var changed = InvokeMergeMissingProperties(defaults, target);

        Assert.True(changed);
        Assert.Equal("stdio", target["transport"]?.GetValue<string>());
        Assert.Equal("Information", target["logLevel"]?.GetValue<string>());
    }

    [Fact]
    public void MergeMissingProperties_PreservesExistingTargetValue()
    {
        var defaults = ParseObject("""
            {
              "transport": "stdio"
            }
            """);
        var target = ParseObject("""
            {
              "transport": "http"
            }
            """);

        var changed = InvokeMergeMissingProperties(defaults, target);

        Assert.False(changed);
        Assert.Equal("http", target["transport"]?.GetValue<string>());
    }

    [Fact]
    public void MergeMissingProperties_MergesNestedObjectsWithoutOverwritingExistingValues()
    {
        var defaults = ParseObject("""
            {
              "logging": {
                "level": "Information",
                "file": {
                  "enabled": true,
                  "path": "default.log"
                }
              }
            }
            """);
        var target = ParseObject("""
            {
              "logging": {
                "level": "Debug",
                "file": {
                  "enabled": false
                }
              }
            }
            """);

        var changed = InvokeMergeMissingProperties(defaults, target);
        var logging = target["logging"]!.AsObject();
        var file = logging["file"]!.AsObject();

        Assert.True(changed);
        Assert.Equal("Debug", logging["level"]?.GetValue<string>());
        Assert.False(file["enabled"]!.GetValue<bool>());
        Assert.Equal("default.log", file["path"]?.GetValue<string>());
    }

    [Fact]
    public void MergeMissingProperties_WhenNothingMissing_ReturnsFalse()
    {
        var defaults = ParseObject("""
            {
              "transport": "stdio",
              "logging": {
                "level": "Information"
              }
            }
            """);
        var target = ParseObject("""
            {
              "transport": "http",
              "logging": {
                "level": "Debug"
              }
            }
            """);
        var originalJson = target.ToJsonString();

        var changed = InvokeMergeMissingProperties(defaults, target);

        Assert.False(changed);
        Assert.Equal(originalJson, target.ToJsonString());
    }

    [Theory]
    [InlineData(false, true, "[1,2,3]")]
    [InlineData(true, false, "[9]")]
    public void MergeMissingProperties_HandlesArraysWithoutOverwritingExistingValues(
        bool targetHasExistingArray,
        bool expectedChanged,
        string expectedArrayJson)
    {
        var defaults = ParseObject("""
            {
              "items": [1, 2, 3]
            }
            """);
        var target = ParseObject(targetHasExistingArray
            ? """
              {
                "items": [9]
              }
              """
            : "{}"
        );

        var changed = InvokeMergeMissingProperties(defaults, target);

        Assert.Equal(expectedChanged, changed);
        Assert.Equal(expectedArrayJson, target["items"]!.ToJsonString());

        if (!targetHasExistingArray)
        {
            Assert.NotSame(defaults["items"], target["items"]);
        }
    }

    [Fact]
    public void MergeMissingProperties_AddsMissingNullsAndPreservesExistingNulls()
    {
        var defaults = ParseObject("""
            {
              "optional": null,
              "nested": {
                "value": "default"
              }
            }
            """);
        var target = ParseObject("""
            {
              "nested": null
            }
            """);

        var changed = InvokeMergeMissingProperties(defaults, target);

        Assert.True(changed);
        Assert.True(target.TryGetPropertyValue("optional", out var optionalValue));
        Assert.Null(optionalValue);
        Assert.True(target.TryGetPropertyValue("nested", out var nestedValue));
        Assert.Null(nestedValue);
    }

    private static bool InvokeMergeMissingProperties(JsonObject defaults, JsonObject target)
    {
        return (bool)(MergeMissingPropertiesMethod.Invoke(null, new object[] { defaults, target })
            ?? throw new InvalidOperationException("MergeMissingProperties returned null unexpectedly."));
    }

    private static JsonObject ParseObject(string json)
    {
        return JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Expected JSON object.");
    }
}
