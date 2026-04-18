using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;
using PoshMcp.Tests.Models;
using Xunit;

namespace PoshMcp.Tests.Unit.McpResources;

/// <summary>
/// Unit tests that validate McpResources configuration model binding from JSON.
/// Tests the configuration schema defined in Spec 002 (FR-018 through FR-021, FR-030, FR-034).
///
/// These tests compile and pass today using stub POCOs in PoshMcp.Tests.Models.
/// Once the server implementation PR lands, update to bind against the server-side types.
/// </summary>
public class McpResourceConfigurationBindingTests
{
    private static IConfiguration BuildConfig(string json)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return new ConfigurationBuilder().AddJsonStream(stream).Build();
    }

    [Fact]
    public void McpResourceDefinition_BindsUri_FromConfiguration()
    {
        var json = """
        {
          "McpResources": {
            "Resources": [
              {
                "Uri": "poshmcp://resources/server-config",
                "Name": "Server Config",
                "Source": "file",
                "Path": "./appsettings.json"
              }
            ]
          }
        }
        """;

        var config = BuildConfig(json);
        var section = new McpResourcesSection();
        config.GetSection("McpResources").Bind(section);

        Assert.Single(section.Resources);
        Assert.Equal("poshmcp://resources/server-config", section.Resources[0].Uri);
    }

    [Fact]
    public void McpResourceDefinition_BindsName_FromConfiguration()
    {
        var json = """
        {
          "McpResources": {
            "Resources": [
              {
                "Uri": "poshmcp://resources/test",
                "Name": "My Resource",
                "Source": "file",
                "Path": "./file.txt"
              }
            ]
          }
        }
        """;

        var config = BuildConfig(json);
        var section = new McpResourcesSection();
        config.GetSection("McpResources").Bind(section);

        Assert.Equal("My Resource", section.Resources[0].Name);
    }

    [Fact]
    public void McpResourceDefinition_BindsDescription_FromConfiguration()
    {
        var json = """
        {
          "McpResources": {
            "Resources": [
              {
                "Uri": "poshmcp://resources/test",
                "Name": "Test",
                "Description": "A test resource",
                "Source": "file",
                "Path": "./file.txt"
              }
            ]
          }
        }
        """;

        var config = BuildConfig(json);
        var section = new McpResourcesSection();
        config.GetSection("McpResources").Bind(section);

        Assert.Equal("A test resource", section.Resources[0].Description);
    }

    [Fact]
    public void McpResourceDefinition_BindsMimeType_FromConfiguration()
    {
        var json = """
        {
          "McpResources": {
            "Resources": [
              {
                "Uri": "poshmcp://resources/config",
                "Name": "Config",
                "MimeType": "application/json",
                "Source": "file",
                "Path": "./appsettings.json"
              }
            ]
          }
        }
        """;

        var config = BuildConfig(json);
        var section = new McpResourcesSection();
        config.GetSection("McpResources").Bind(section);

        Assert.Equal("application/json", section.Resources[0].MimeType);
    }

    [Fact]
    public void McpResourceDefinition_MimeType_DefaultsToTextPlain_WhenOmitted()
    {
        // FR-030: MimeType MUST default to "text/plain" when not specified.
        var json = """
        {
          "McpResources": {
            "Resources": [
              {
                "Uri": "poshmcp://resources/notes",
                "Name": "Notes",
                "Source": "file",
                "Path": "./notes.txt"
              }
            ]
          }
        }
        """;

        var config = BuildConfig(json);
        var section = new McpResourcesSection();
        config.GetSection("McpResources").Bind(section);

        Assert.Equal("text/plain", section.Resources[0].MimeType);
    }

    [Fact]
    public void McpResourceDefinition_BindsFileSource_WithPath()
    {
        // FR-020: file source requires Path field.
        var json = """
        {
          "McpResources": {
            "Resources": [
              {
                "Uri": "poshmcp://resources/runbook",
                "Name": "Runbook",
                "Source": "file",
                "Path": "./runbooks/deploy.md"
              }
            ]
          }
        }
        """;

        var config = BuildConfig(json);
        var section = new McpResourcesSection();
        config.GetSection("McpResources").Bind(section);

        var resource = section.Resources[0];
        Assert.Equal("file", resource.Source);
        Assert.Equal("./runbooks/deploy.md", resource.Path);
        Assert.Null(resource.Command);
    }

    [Fact]
    public void McpResourceDefinition_BindsCommandSource_WithCommand()
    {
        // FR-021: command source requires Command field.
        var json = """
        {
          "McpResources": {
            "Resources": [
              {
                "Uri": "poshmcp://resources/process-list",
                "Name": "Running Processes",
                "Source": "command",
                "Command": "Get-Process | Select-Object Name, Id | ConvertTo-Json"
              }
            ]
          }
        }
        """;

        var config = BuildConfig(json);
        var section = new McpResourcesSection();
        config.GetSection("McpResources").Bind(section);

        var resource = section.Resources[0];
        Assert.Equal("command", resource.Source);
        Assert.Equal("Get-Process | Select-Object Name, Id | ConvertTo-Json", resource.Command);
        Assert.Null(resource.Path);
    }

    [Fact]
    public void McpResourcesSection_CanBindMultipleResources()
    {
        // Acceptance scenario (US1-5): two resources configured with different URIs.
        var json = """
        {
          "McpResources": {
            "Resources": [
              {
                "Uri": "poshmcp://resources/first",
                "Name": "First",
                "Source": "file",
                "Path": "./first.txt"
              },
              {
                "Uri": "poshmcp://resources/second",
                "Name": "Second",
                "Source": "command",
                "Command": "Get-Date | Out-String"
              }
            ]
          }
        }
        """;

        var config = BuildConfig(json);
        var section = new McpResourcesSection();
        config.GetSection("McpResources").Bind(section);

        Assert.Equal(2, section.Resources.Count);
        Assert.Equal("poshmcp://resources/first", section.Resources[0].Uri);
        Assert.Equal("poshmcp://resources/second", section.Resources[1].Uri);
    }

    [Fact]
    public void McpResourcesSection_AbsentFromConfig_BindsToEmptyList()
    {
        // Edge case: McpResources section absent from appsettings.json → empty list, no crash.
        var json = """{ "PowerShellConfiguration": { "CommandNames": [] } }""";

        var config = BuildConfig(json);
        var section = new McpResourcesSection();
        config.GetSection("McpResources").Bind(section);

        Assert.Empty(section.Resources);
    }
}
