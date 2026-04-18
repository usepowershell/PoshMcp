using System.Collections.Generic;
using System.IO;
using PoshMcp.Server.McpPrompts;
using Xunit;

namespace PoshMcp.Tests.Unit.McpPrompts;

/// <summary>
/// Unit tests for McpPromptsValidator — doctor validation checks from Spec 002 User Story 5
/// (FR-026, FR-027, SC-012).
/// </summary>
public class McpPromptsValidatorTests
{
    private static readonly string ExistingDir = Path.GetTempPath();

    private static string CreateTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"poshmcp-prompt-test-{System.Guid.NewGuid():N}.md");
        File.WriteAllText(path, "# Test Prompt");
        return path;
    }

    // ── Error: file source with missing Path ─────────────────────────────────

    [Fact]
    public void Validate_FilePrompt_WithMissingPath_ReportsError()
    {
        // FR-026: file source prompt requires an existing file at Path.
        var config = new McpPromptsConfiguration
        {
            Prompts = new List<McpPromptConfiguration>
            {
                new()
                {
                    Name = "missing-path-prompt",
                    Source = "file"
                    // Path intentionally omitted
                }
            }
        };

        var result = McpPromptsValidator.Validate(config, ExistingDir);

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("Path is missing"));
    }

    [Fact]
    public void Validate_FilePrompt_WithNonExistentPath_ReportsError()
    {
        // FR-026: doctor reports error when the configured file path does not exist on disk.
        var config = new McpPromptsConfiguration
        {
            Prompts = new List<McpPromptConfiguration>
            {
                new()
                {
                    Name = "bad-path-prompt",
                    Source = "file",
                    Path = "/nonexistent/prompt/file.md"
                }
            }
        };

        var result = McpPromptsValidator.Validate(config, ExistingDir);

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("does not resolve to an existing file"));
    }

    // ── Error: command source with empty Command ──────────────────────────────

    [Fact]
    public void Validate_CommandPrompt_WithEmptyCommand_ReportsError()
    {
        // FR-026: command source requires a non-empty Command string.
        var config = new McpPromptsConfiguration
        {
            Prompts = new List<McpPromptConfiguration>
            {
                new()
                {
                    Name = "empty-command-prompt",
                    Source = "command",
                    Command = "   " // whitespace-only
                }
            }
        };

        var result = McpPromptsValidator.Validate(config, ExistingDir);

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("Command is empty"));
    }

    // ── Error: duplicate prompt Name ─────────────────────────────────────────

    [Fact]
    public void Validate_TwoPromptsWithSameName_ReportsDuplicateError()
    {
        // FR-027: duplicate prompt names must be detected and reported as errors.
        var tempFile = CreateTempFile();
        try
        {
            var config = new McpPromptsConfiguration
            {
                Prompts = new List<McpPromptConfiguration>
                {
                    new()
                    {
                        Name = "dup-prompt",
                        Source = "file",
                        Path = tempFile
                    },
                    new()
                    {
                        Name = "dup-prompt",
                        Source = "command",
                        Command = "Get-Date"
                    }
                }
            };

            var result = McpPromptsValidator.Validate(config, ExistingDir);

            Assert.NotEmpty(result.Errors);
            Assert.Contains(result.Errors, e => e.Contains("duplicated"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── Error: argument with empty Name ──────────────────────────────────────

    [Fact]
    public void Validate_Prompt_WithEmptyArgumentName_ReportsError()
    {
        // FR-026: argument names must be non-empty (used as PS variable names).
        var config = new McpPromptsConfiguration
        {
            Prompts = new List<McpPromptConfiguration>
            {
                new()
                {
                    Name = "prompt-with-nameless-arg",
                    Source = "command",
                    Command = "Get-Date",
                    Arguments = new List<McpPromptArgumentConfiguration>
                    {
                        new() { Name = "", Required = false }
                    }
                }
            }
        };

        var result = McpPromptsValidator.Validate(config, ExistingDir);

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("empty Name"));
    }

    // ── Valid prompts → no errors ─────────────────────────────────────────────

    [Fact]
    public void Validate_ValidFileAndCommandPrompts_ReportsNoErrors()
    {
        // SC-012: a fully valid configuration should produce zero errors.
        var tempFile = CreateTempFile();
        try
        {
            var config = new McpPromptsConfiguration
            {
                Prompts = new List<McpPromptConfiguration>
                {
                    new()
                    {
                        Name = "valid-file-prompt",
                        Source = "file",
                        Path = tempFile,
                        Arguments = new List<McpPromptArgumentConfiguration>
                        {
                            new() { Name = "serviceName", Required = true }
                        }
                    },
                    new()
                    {
                        Name = "valid-cmd-prompt",
                        Source = "command",
                        Command = "Get-Date | Out-String",
                        Arguments = new List<McpPromptArgumentConfiguration>()
                    }
                }
            };

            var result = McpPromptsValidator.Validate(config, ExistingDir);

            Assert.Empty(result.Errors);
            Assert.Equal(2, result.Configured);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── Absent McpPrompts section → 0 configured, no error ───────────────────

    [Fact]
    public void Validate_EmptyPromptsList_ReportsZeroConfiguredNoErrors()
    {
        // SC-012: absent McpPrompts section binds to empty list; doctor shows 0 configured, no errors.
        var config = new McpPromptsConfiguration();

        var result = McpPromptsValidator.Validate(config, ExistingDir);

        Assert.Equal(0, result.Configured);
        Assert.Empty(result.Errors);
    }

    // ── Error: invalid Source value ───────────────────────────────────────────

    [Fact]
    public void Validate_PromptWithInvalidSource_ReportsError()
    {
        // FR-026: source must be "file" or "command".
        var config = new McpPromptsConfiguration
        {
            Prompts = new List<McpPromptConfiguration>
            {
                new()
                {
                    Name = "bad-source-prompt",
                    Source = "database"
                }
            }
        };

        var result = McpPromptsValidator.Validate(config, ExistingDir);

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("invalid"));
    }
}
