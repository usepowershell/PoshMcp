using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using PoshMcp.Server.PowerShell.OutOfProcess;
using Xunit;

namespace PoshMcp.Tests.Unit.OutOfProcess;

/// <summary>
/// Unit tests for RemoteToolSchema and RemoteParameterSchema DTOs.
/// Validates default values, construction, and JSON serialization round-trips.
/// </summary>
public class RemoteToolSchemaTests
{
    #region RemoteToolSchema Defaults

    [Fact]
    public void RemoteToolSchema_Name_DefaultsToEmptyString()
    {
        var schema = new RemoteToolSchema();
        Assert.Equal(string.Empty, schema.Name);
    }

    [Fact]
    public void RemoteToolSchema_Description_DefaultsToEmptyString()
    {
        var schema = new RemoteToolSchema();
        Assert.Equal(string.Empty, schema.Description);
    }

    [Fact]
    public void RemoteToolSchema_ParameterSetName_DefaultsToNull()
    {
        var schema = new RemoteToolSchema();
        Assert.Null(schema.ParameterSetName);
    }

    [Fact]
    public void RemoteToolSchema_Parameters_DefaultsToEmptyList()
    {
        var schema = new RemoteToolSchema();
        Assert.NotNull(schema.Parameters);
        Assert.Empty(schema.Parameters);
    }

    #endregion

    #region RemoteToolSchema Construction

    [Fact]
    public void RemoteToolSchema_CanSetProperties()
    {
        var schema = new RemoteToolSchema
        {
            Name = "Get-AzContext",
            Description = "Gets the current Azure context",
            ParameterSetName = "__AllParameterSets",
            Parameters = new List<RemoteParameterSchema>
            {
                new RemoteParameterSchema { Name = "Subscription", TypeName = "System.String", IsMandatory = false }
            }
        };

        Assert.Equal("Get-AzContext", schema.Name);
        Assert.Equal("Gets the current Azure context", schema.Description);
        Assert.Equal("__AllParameterSets", schema.ParameterSetName);
        Assert.Single(schema.Parameters);
        Assert.Equal("Subscription", schema.Parameters[0].Name);
    }

    #endregion

    #region RemoteParameterSchema Defaults

    [Fact]
    public void RemoteParameterSchema_Name_DefaultsToEmptyString()
    {
        var param = new RemoteParameterSchema();
        Assert.Equal(string.Empty, param.Name);
    }

    [Fact]
    public void RemoteParameterSchema_TypeName_DefaultsToSystemString()
    {
        var param = new RemoteParameterSchema();
        Assert.Equal("System.String", param.TypeName);
    }

    [Fact]
    public void RemoteParameterSchema_IsMandatory_DefaultsToFalse()
    {
        var param = new RemoteParameterSchema();
        Assert.False(param.IsMandatory);
    }

    [Fact]
    public void RemoteParameterSchema_Position_DefaultsToIntMaxValue()
    {
        var param = new RemoteParameterSchema();
        Assert.Equal(int.MaxValue, param.Position);
    }

    #endregion

    #region RemoteParameterSchema Construction

    [Fact]
    public void RemoteParameterSchema_CanSetAllProperties()
    {
        var param = new RemoteParameterSchema
        {
            Name = "ResourceGroupName",
            TypeName = "System.String",
            IsMandatory = true,
            Position = 0
        };

        Assert.Equal("ResourceGroupName", param.Name);
        Assert.Equal("System.String", param.TypeName);
        Assert.True(param.IsMandatory);
        Assert.Equal(0, param.Position);
    }

    #endregion

    #region JSON Serialization Round-Trip

    [Fact]
    public void RemoteToolSchema_JsonRoundTrip_PreservesAllProperties()
    {
        var original = new RemoteToolSchema
        {
            Name = "Get-AzResourceGroup",
            Description = "Gets Azure resource groups",
            ParameterSetName = "ByName",
            Parameters = new List<RemoteParameterSchema>
            {
                new RemoteParameterSchema
                {
                    Name = "Name",
                    TypeName = "System.String",
                    IsMandatory = true,
                    Position = 0
                },
                new RemoteParameterSchema
                {
                    Name = "Location",
                    TypeName = "System.String",
                    IsMandatory = false,
                    Position = 1
                }
            }
        };

        var json = JsonConvert.SerializeObject(original);
        var deserialized = JsonConvert.DeserializeObject<RemoteToolSchema>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Description, deserialized.Description);
        Assert.Equal(original.ParameterSetName, deserialized.ParameterSetName);
        Assert.Equal(original.Parameters.Count, deserialized.Parameters.Count);

        for (int i = 0; i < original.Parameters.Count; i++)
        {
            Assert.Equal(original.Parameters[i].Name, deserialized.Parameters[i].Name);
            Assert.Equal(original.Parameters[i].TypeName, deserialized.Parameters[i].TypeName);
            Assert.Equal(original.Parameters[i].IsMandatory, deserialized.Parameters[i].IsMandatory);
            Assert.Equal(original.Parameters[i].Position, deserialized.Parameters[i].Position);
        }
    }

    [Fact]
    public void RemoteToolSchema_JsonRoundTrip_WithDefaults()
    {
        var original = new RemoteToolSchema();

        var json = JsonConvert.SerializeObject(original);
        var deserialized = JsonConvert.DeserializeObject<RemoteToolSchema>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(string.Empty, deserialized.Name);
        Assert.Equal(string.Empty, deserialized.Description);
        Assert.Empty(deserialized.Parameters);
    }

    [Fact]
    public void RemoteParameterSchema_JsonRoundTrip_PreservesDefaults()
    {
        var original = new RemoteParameterSchema();

        var json = JsonConvert.SerializeObject(original);
        var deserialized = JsonConvert.DeserializeObject<RemoteParameterSchema>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(string.Empty, deserialized.Name);
        Assert.Equal("System.String", deserialized.TypeName);
        Assert.False(deserialized.IsMandatory);
        Assert.Equal(int.MaxValue, deserialized.Position);
    }

    [Fact]
    public void RemoteToolSchema_DeserializeFromJson_WithNullParameterSetName()
    {
        var json = """{"Name":"Test","Description":"Desc","ParameterSetName":null,"Parameters":[]}""";
        var deserialized = JsonConvert.DeserializeObject<RemoteToolSchema>(json);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.ParameterSetName);
    }

    #endregion
}
