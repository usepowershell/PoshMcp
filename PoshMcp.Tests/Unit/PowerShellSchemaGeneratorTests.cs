using PoshMcp.Server.PowerShell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public class PowerShellSchemaGeneratorTests
{
    private enum TestEnum
    {
        First,
        Second,
        Third
    }

    private sealed class CustomType { }

    private sealed class RecordingToolMetadataSource : IToolMetadataSource
    {
        public ParameterDescriptionRequest? LastParameterRequest { get; private set; }

        public ToolDescriptionResult ResolveToolDescription(in ToolDescriptionRequest request)
            => new(request.CommandName, ToolDescriptionSource.Name);

        public ParameterDescriptionResult ResolveParameterDescription(in ParameterDescriptionRequest request)
        {
            LastParameterRequest = request;
            return new ParameterDescriptionResult("Custom metadata description", ParameterDescriptionSource.HelpMessage);
        }
    }

    [Fact]
    public void StringType_MapsToString()
    {
        var schema = CreateSchema(typeof(string));

        Assert.Equal("string", schema["type"]);
    }

    [Fact]
    public void BoolType_MapsToBoolean()
    {
        var schema = CreateSchema(typeof(bool));

        Assert.Equal("boolean", schema["type"]);
    }

    [Fact]
    public void SwitchParameter_MapsToBoolean()
    {
        var schema = CreateSchema(typeof(SwitchParameter));

        Assert.Equal("boolean", schema["type"]);
    }

    [Fact]
    public void IntType_MapsToInteger()
    {
        var schema = CreateSchema(typeof(int));

        Assert.Equal("integer", schema["type"]);
    }

    [Fact]
    public void LongType_MapsToInteger()
    {
        var schema = CreateSchema(typeof(long));

        Assert.Equal("integer", schema["type"]);
    }

    [Fact]
    public void DoubleType_MapsToNumber()
    {
        var schema = CreateSchema(typeof(double));

        Assert.Equal("number", schema["type"]);
    }

    [Fact]
    public void DecimalType_MapsToNumber()
    {
        var schema = CreateSchema(typeof(decimal));

        Assert.Equal("number", schema["type"]);
    }

    [Fact]
    public void FloatType_MapsToNumber()
    {
        var schema = CreateSchema(typeof(float));

        Assert.Equal("number", schema["type"]);
    }

    [Fact]
    public void ArrayType_MapsToArray()
    {
        var schema = CreateSchema(typeof(string[]));

        Assert.Equal("array", schema["type"]);
        var items = schema["items"];
        var itemType = items.GetType().GetProperty("type")!.GetValue(items);
        Assert.Equal("string", itemType);
    }

    [Fact]
    public void EnumType_MapsToStringWithEnum()
    {
        var schema = CreateSchema(typeof(TestEnum));

        Assert.Equal("string", schema["type"]);
        var values = Assert.IsType<string[]>(schema["enum"]);
        Assert.Equal(new[] { nameof(TestEnum.First), nameof(TestEnum.Second), nameof(TestEnum.Third) }, values);
    }

    [Fact]
    public void UnknownType_DefaultsToString()
    {
        var schema = CreateSchema(typeof(CustomType));

        Assert.Equal("string", schema["type"]);
    }

    [Fact]
    public void NullableInt_UnwrapsToInteger()
    {
        var schema = CreateSchema(typeof(int?));

        Assert.Equal("integer", schema["type"]);
    }

    [Fact]
    public void NullableDouble_UnwrapsToNumber()
    {
        var schema = CreateSchema(typeof(double?));

        Assert.Equal("number", schema["type"]);
    }

    [Fact]
    public void Description_FromHelpMessage()
    {
        var metadata = CreateParameterMetadata(typeof(string), new ParameterAttribute { HelpMessage = "Friendly description" });

        var schema = Assert.IsType<Dictionary<string, object>>(
            PowerShellSchemaGenerator.CreateParameterSchema(metadata, "Test-Command", metadata.Name, null));

        Assert.Equal("Friendly description", schema["description"]);
    }

    [Fact]
    public void Description_FallsBackToTyped()
    {
        var schema = CreateSchema(typeof(int));

        Assert.Equal("Parameter of type Int32", schema["description"]);
    }

    [Fact]
    public void Description_FromValidateSet_MentionsValidValues()
    {
        var metadata = new ParameterMetadata("Status", typeof(string));
        metadata.Attributes.Add(new ValidateSetAttribute("Active", "Inactive", "Pending"));

        var schema = Assert.IsType<Dictionary<string, object>>(
            PowerShellSchemaGenerator.CreateParameterSchema(metadata, "Test-Command", "Status", null));

        var description = Assert.IsType<string>(schema["description"]);
        Assert.Contains("Active", description);
        Assert.Contains("Inactive", description);
        Assert.Contains("Pending", description);
    }

    [Fact]
    public void Description_UsesCustomMetadataSource()
    {
        var metadata = CreateParameterMetadata(typeof(string));
        var metadataSource = new RecordingToolMetadataSource();

        var schema = Assert.IsType<Dictionary<string, object>>(
            PowerShellSchemaGenerator.CreateParameterSchema(metadata, "Test-Command", metadata.Name, metadataSource));

        Assert.Equal("Custom metadata description", schema["description"]);
        Assert.True(metadataSource.LastParameterRequest.HasValue);
        var request = metadataSource.LastParameterRequest.Value;
        Assert.Equal("Test-Command", request.CommandName);
        Assert.Equal(metadata.Name, request.ParameterName);
    }

    [Fact]
    public void NullMetadata_ThrowsArgumentNull()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            PowerShellSchemaGenerator.CreateParameterSchema(null!, "Test-Command", "TestParam", null));

        Assert.Equal("parameterMetadata", ex.ParamName);
    }

    [Fact]
    public void GenerateParameterSchema_SkipsCommonParameters()
    {
        var commandInfo = GetTestCommandInfo();

        var schema = ReadGeneratedSchema(PowerShellSchemaGenerator.GenerateParameterSchema(commandInfo));

        Assert.Equal("object", schema.Type);
        Assert.Contains("Name", schema.Properties.Keys);
        Assert.Contains("Count", schema.Properties.Keys);
        Assert.DoesNotContain("Verbose", schema.Properties.Keys);
        Assert.DoesNotContain("Debug", schema.Properties.Keys);
        Assert.DoesNotContain("ErrorAction", schema.Properties.Keys);
        Assert.DoesNotContain("WarningAction", schema.Properties.Keys);
    }

    [Fact]
    public void GenerateParameterSchema_IncludesMandatoryInRequired()
    {
        var commandInfo = GetTestCommandInfo();

        var schema = ReadGeneratedSchema(PowerShellSchemaGenerator.GenerateParameterSchema(commandInfo));

        Assert.Contains("Name", schema.Required);
        Assert.DoesNotContain("Count", schema.Required);
    }

    private static Dictionary<string, object> CreateSchema(Type parameterType)
    {
        var metadata = CreateParameterMetadata(parameterType);
        return Assert.IsType<Dictionary<string, object>>(
            PowerShellSchemaGenerator.CreateParameterSchema(metadata, "Test-Command", metadata.Name, null));
    }

    private static ParameterMetadata CreateParameterMetadata(Type parameterType, params Attribute[] attributes)
    {
        var metadata = new ParameterMetadata("TestParam", parameterType);
        foreach (var attribute in attributes)
        {
            metadata.Attributes.Add(attribute);
        }

        return metadata;
    }

    private static CommandInfo GetTestCommandInfo()
    {
        using var ps = System.Management.Automation.PowerShell.Create();
        ps.AddScript(@"
function Test-SchemaGeneratorCommand {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [string]$Name,

        [Parameter()]
        [int]$Count
    )
}

Get-Command -Name Test-SchemaGeneratorCommand
");

        var commandInfo = Assert.Single(ps.Invoke<CommandInfo>());
        Assert.False(ps.HadErrors, string.Join(Environment.NewLine, ps.Streams.Error.Select(e => e.ToString())));
        return commandInfo;
    }

    private static (string Type, Dictionary<string, object> Properties, string[] Required) ReadGeneratedSchema(object schema)
    {
        var schemaType = schema.GetType();
        var type = Assert.IsType<string>(schemaType.GetProperty("type")!.GetValue(schema));
        var properties = Assert.IsType<Dictionary<string, object>>(schemaType.GetProperty("properties")!.GetValue(schema));
        var required = Assert.IsType<string[]>(schemaType.GetProperty("required")!.GetValue(schema));
        return (type, properties, required);
    }
}
