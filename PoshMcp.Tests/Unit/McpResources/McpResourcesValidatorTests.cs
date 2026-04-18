using System.Collections.Generic;
using System.IO;
using PoshMcp.Server.McpResources;
using Xunit;

namespace PoshMcp.Tests.Unit.McpResources;

/// <summary>
/// Unit tests for McpResourcesValidator — doctor validation checks from Spec 002 User Story 5
/// (FR-026, FR-027, SC-012).
/// </summary>
public class McpResourcesValidatorTests
{
    private static readonly string ExistingDir = Path.GetTempPath();

    private static string CreateTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"poshmcp-validator-test-{System.Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "test");
        return path;
    }

    // ── Error: file source with missing Path ─────────────────────────────────

    [Fact]
    public void Validate_FileResource_WithMissingPath_ReportsError()
    {
        // FR-026: file source requires an existing file at Path.
        var config = new McpResourcesConfiguration
        {
            Resources = new List<McpResourceConfiguration>
            {
                new() { Uri = "poshmcp://resources/missing-path", Name = "Missing Path", Source = "file" }
            }
        };

        var result = McpResourcesValidator.Validate(config, ExistingDir);

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("Path is missing"));
    }

    [Fact]
    public void Validate_FileResource_WithNonExistentPath_ReportsError()
    {
        // FR-026: doctor reports error when the configured file path does not exist on disk.
        var config = new McpResourcesConfiguration
        {
            Resources = new List<McpResourceConfiguration>
            {
                new()
                {
                    Uri = "poshmcp://resources/bad-path",
                    Name = "Bad Path",
                    Source = "file",
                    Path = "/nonexistent/path/that/does/not/exist.txt"
                }
            }
        };

        var result = McpResourcesValidator.Validate(config, ExistingDir);

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("does not resolve to an existing file"));
    }

    // ── Error: command source with empty Command ──────────────────────────────

    [Fact]
    public void Validate_CommandResource_WithEmptyCommand_ReportsError()
    {
        // FR-026: command source requires a non-empty Command.
        var config = new McpResourcesConfiguration
        {
            Resources = new List<McpResourceConfiguration>
            {
                new()
                {
                    Uri = "poshmcp://resources/empty-cmd",
                    Name = "Empty Command",
                    Source = "command",
                    Command = ""
                }
            }
        };

        var result = McpResourcesValidator.Validate(config, ExistingDir);

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("Command is empty"));
    }

    // ── Error: duplicate URI ──────────────────────────────────────────────────

    [Fact]
    public void Validate_TwoResourcesWithSameUri_ReportsDuplicateError()
    {
        // FR-027: duplicate URIs must be detected and reported as errors.
        var tempFile = CreateTempFile();
        try
        {
            var config = new McpResourcesConfiguration
            {
                Resources = new List<McpResourceConfiguration>
                {
                    new()
                    {
                        Uri = "poshmcp://resources/dup",
                        Name = "Resource A",
                        Source = "file",
                        Path = tempFile
                    },
                    new()
                    {
                        Uri = "poshmcp://resources/dup",
                        Name = "Resource B",
                        Source = "command",
                        Command = "Get-Date"
                    }
                }
            };

            var result = McpResourcesValidator.Validate(config, ExistingDir);

            Assert.NotEmpty(result.Errors);
            Assert.Contains(result.Errors, e => e.Contains("duplicated"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── Valid resources → no errors ───────────────────────────────────────────

    [Fact]
    public void Validate_ValidFileAndCommandResources_ReportsNoErrors()
    {
        // SC-012: a fully valid configuration should produce zero errors.
        var tempFile = CreateTempFile();
        try
        {
            var config = new McpResourcesConfiguration
            {
                Resources = new List<McpResourceConfiguration>
                {
                    new()
                    {
                        Uri = "poshmcp://resources/file-resource",
                        Name = "File Resource",
                        Source = "file",
                        MimeType = "text/plain",
                        Path = tempFile
                    },
                    new()
                    {
                        Uri = "poshmcp://resources/cmd-resource",
                        Name = "Command Resource",
                        Source = "command",
                        MimeType = "text/plain",
                        Command = "Get-Date | Out-String"
                    }
                }
            };

            var result = McpResourcesValidator.Validate(config, ExistingDir);

            Assert.Empty(result.Errors);
            Assert.Equal(2, result.Configured);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── Absent McpResources section → 0 configured, no error ─────────────────

    [Fact]
    public void Validate_EmptyResourcesList_ReportsZeroConfiguredNoErrors()
    {
        // SC-012: absent McpResources section binds to empty list; doctor shows 0 configured, no errors.
        var config = new McpResourcesConfiguration();

        var result = McpResourcesValidator.Validate(config, ExistingDir);

        Assert.Equal(0, result.Configured);
        Assert.Empty(result.Errors);
    }

    // ── Warning: URI format ───────────────────────────────────────────────────

    [Fact]
    public void Validate_ResourceWithNonRecommendedUriScheme_ReportsWarning()
    {
        // FR-027: URI format check — non-poshmcp:// URIs get a warning (not an error).
        var tempFile = CreateTempFile();
        try
        {
            var config = new McpResourcesConfiguration
            {
                Resources = new List<McpResourceConfiguration>
                {
                    new()
                    {
                        Uri = "https://example.com/resource",
                        Name = "Web Resource",
                        Source = "file",
                        MimeType = "text/plain",
                        Path = tempFile
                    }
                }
            };

            var result = McpResourcesValidator.Validate(config, ExistingDir);

            Assert.Empty(result.Errors);
            Assert.NotEmpty(result.Warnings);
            Assert.Contains(result.Warnings, w => w.Contains("recommended"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── Warning: missing MimeType ─────────────────────────────────────────────

    [Fact]
    public void Validate_ResourceWithNoMimeType_ReportsMimeTypeWarning()
    {
        // FR-027: MimeType is nullable; when null the validator warns that runtime will fall back to text/plain.
        var config = new McpResourcesConfiguration
        {
            Resources = new List<McpResourceConfiguration>
            {
                new()
                {
                    Uri = "poshmcp://resources/no-mime",
                    Name = "No MimeType",
                    Source = "command",
                    Command = "Get-Date"
                }
            }
        };

        var result = McpResourcesValidator.Validate(config, ExistingDir);

        Assert.Empty(result.Errors);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("MimeType"));
    }

    // ── Error: invalid Source value ───────────────────────────────────────────

    [Fact]
    public void Validate_ResourceWithInvalidSource_ReportsError()
    {
        // FR-026: source must be "file" or "command".
        var config = new McpResourcesConfiguration
        {
            Resources = new List<McpResourceConfiguration>
            {
                new()
                {
                    Uri = "poshmcp://resources/bad-source",
                    Name = "Bad Source",
                    Source = "http"
                }
            }
        };

        var result = McpResourcesValidator.Validate(config, ExistingDir);

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("invalid"));
    }

}
