using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;
using PoshMcp.Tests.Models;
using Xunit;

namespace PoshMcp.Tests.Unit.McpPrompts;

/// <summary>
/// Unit tests that validate McpPrompts configuration model binding from JSON.
/// Tests the configuration schema defined in Spec 002 (FR-022 through FR-025, FR-032).
///
/// These tests compile and pass today using stub POCOs in PoshMcp.Tests.Models.
/// Once the server implementation PR lands, update to bind against the server-side types.
/// </summary>
public class McpPromptConfigurationBindingTests
{
    private static IConfiguration BuildConfig(string json)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return new ConfigurationBuilder().AddJsonStream(stream).Build();
    }

    [Fact]
    public void McpPromptDefinition_BindsName_FromConfiguration()
    {
        // FR-022: prompts/list includes name from config.
        var json = """
        {
          "McpPrompts": {
            "Prompts": [
              {
                "Name": "analyze-service",
                "Description": "Analyze a service",
                "Source": "file",
                "Path": "./prompts/analyze-service.md",
                "Arguments": []
              }
            ]
          }
        }
        """;

        var config = BuildConfig(json);
        var section = new McpPromptsSection();
        config.GetSection("McpPrompts").Bind(section);

        Assert.Single(section.Prompts);
        Assert.Equal("analyze-service", section.Prompts[0].Name);
    }

    [Fact]
    public void McpPromptDefinition_BindsDescription_FromConfiguration()
    {
        var json = """
        {
          "McpPrompts": {
            "Prompts": [
              {
                "Name": "test-prompt",
                "Description": "A prompt for testing",
                "Source": "file",
                "Path": "./test.md",
                "Arguments": []
              }
            ]
          }
        }
        """;

        var config = BuildConfig(json);
        var section = new McpPromptsSection();
        config.GetSection("McpPrompts").Bind(section);

        Assert.Equal("A prompt for testing", section.Prompts[0].Description);
    }

    [Fact]
    public void McpPromptDefinition_BindsFileSource_WithPath()
    {
        // FR-024: file source for prompts, Path required.
        var json = """
        {
          "McpPrompts": {
            "Prompts": [
              {
                "Name": "deployment-checklist",
                "Source": "file",
                "Path": "./prompts/deployment.md",
                "Arguments": []
              }
            ]
          }
        }
        """;

        var config = BuildConfig(json);
        var section = new McpPromptsSection();
        config.GetSection("McpPrompts").Bind(section);

        var prompt = section.Prompts[0];
        Assert.Equal("file", prompt.Source);
        Assert.Equal("./prompts/deployment.md", prompt.Path);
        Assert.Null(prompt.Command);
    }

    [Fact]
    public void McpPromptDefinition_BindsCommandSource_WithCommand()
    {
        // FR-025: command source for prompts, Command required.
        var json = """
        {
          "McpPrompts": {
            "Prompts": [
              {
                "Name": "system-summary",
                "Source": "command",
                "Command": "Get-SystemSummaryPrompt",
                "Arguments": []
              }
            ]
          }
        }
        """;

        var config = BuildConfig(json);
        var section = new McpPromptsSection();
        config.GetSection("McpPrompts").Bind(section);

        var prompt = section.Prompts[0];
        Assert.Equal("command", prompt.Source);
        Assert.Equal("Get-SystemSummaryPrompt", prompt.Command);
        Assert.Null(prompt.Path);
    }

    [Fact]
    public void McpPromptDefinition_BindsArguments_WithRequiredTrue()
    {
        // FR-022: prompts/list includes arguments array; Required:true binding.
        // Acceptance scenario (US3-2): required argument appears with required:true.
        var json = """
        {
          "McpPrompts": {
            "Prompts": [
              {
                "Name": "analyze-service",
                "Source": "file",
                "Path": "./analyze.md",
                "Arguments": [
                  {
                    "Name": "serviceName",
                    "Description": "The service to analyze",
                    "Required": true
                  }
                ]
              }
            ]
          }
        }
        """;

        var config = BuildConfig(json);
        var section = new McpPromptsSection();
        config.GetSection("McpPrompts").Bind(section);

        var arg = section.Prompts[0].Arguments[0];
        Assert.Equal("serviceName", arg.Name);
        Assert.Equal("The service to analyze", arg.Description);
        Assert.True(arg.Required);
    }

    [Fact]
    public void McpPromptArgument_Required_DefaultsFalse_WhenOmitted()
    {
        // Spec 002 argument schema: Required defaults to false.
        var json = """
        {
          "McpPrompts": {
            "Prompts": [
              {
                "Name": "test",
                "Source": "file",
                "Path": "./test.md",
                "Arguments": [
                  { "Name": "optionalArg" }
                ]
              }
            ]
          }
        }
        """;

        var config = BuildConfig(json);
        var section = new McpPromptsSection();
        config.GetSection("McpPrompts").Bind(section);

        Assert.False(section.Prompts[0].Arguments[0].Required);
    }

    [Fact]
    public void McpPromptDefinition_BindsMultipleArguments_FromConfiguration()
    {
        // FR-032: multiple arguments can be declared; each has Name, Description, Required.
        var json = """
        {
          "McpPrompts": {
            "Prompts": [
              {
                "Name": "multi-arg-prompt",
                "Source": "command",
                "Command": "Get-AnalysisPrompt",
                "Arguments": [
                  { "Name": "serviceName", "Required": true },
                  { "Name": "environment", "Required": false },
                  { "Name": "depth" }
                ]
              }
            ]
          }
        }
        """;

        var config = BuildConfig(json);
        var section = new McpPromptsSection();
        config.GetSection("McpPrompts").Bind(section);

        var args = section.Prompts[0].Arguments;
        Assert.Equal(3, args.Count);
        Assert.True(args[0].Required);
        Assert.False(args[1].Required);
        Assert.False(args[2].Required);
    }

    [Fact]
    public void McpPromptsSection_AbsentFromConfig_BindsToEmptyList()
    {
        // Edge case: McpPrompts section absent → empty list, server should not crash.
        var json = """{ "PowerShellConfiguration": { "CommandNames": [] } }""";

        var config = BuildConfig(json);
        var section = new McpPromptsSection();
        config.GetSection("McpPrompts").Bind(section);

        Assert.Empty(section.Prompts);
    }

    [Fact]
    public void McpPromptsSection_CanBindMultiplePrompts()
    {
        var json = """
        {
          "McpPrompts": {
            "Prompts": [
              {
                "Name": "prompt-one",
                "Source": "file",
                "Path": "./one.md",
                "Arguments": []
              },
              {
                "Name": "prompt-two",
                "Source": "command",
                "Command": "Get-PromptTwo",
                "Arguments": []
              }
            ]
          }
        }
        """;

        var config = BuildConfig(json);
        var section = new McpPromptsSection();
        config.GetSection("McpPrompts").Bind(section);

        Assert.Equal(2, section.Prompts.Count);
        Assert.Equal("prompt-one", section.Prompts[0].Name);
        Assert.Equal("prompt-two", section.Prompts[1].Name);
    }
}
