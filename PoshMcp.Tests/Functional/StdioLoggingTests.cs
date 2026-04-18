using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PoshMcp.Tests.Integration;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Functional;

/// <summary>
/// Functional tests that verify stdio logging suppression behavior:
/// - No log output leaks to stderr in stdio mode without a log file configured.
/// - When a log file is configured, logs are written to that file instead.
/// </summary>
public class StdioLoggingTests : PowerShellTestBase
{
    // Matches Serilog template "[2024-01-01 12:00:00 INF]" and MEL console "info: " / "warn: " etc.
    private static readonly Regex _logLinePattern = new Regex(
        @"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} [A-Z]{3}\]|^\s*(info|warn|dbug|fail|crit|trce):",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public StdioLoggingTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task StdioTransport_WithNoLogFile_ProducesNoConsoleLogOutput()
    {
        using var configFile = new TemporaryConfigFile();
        using var server = new InProcessMcpServer(Logger, explicitConfigPath: configFile.Path);

        await server.StartAsync();

        using var client = new ExternalMcpClient(Logger, server);
        await client.StartAsync();

        // Send a request to exercise the server's logging paths
        var response = await client.SendInitializeAsync();
        Assert.NotNull(response);

        var stderrLines = server.GetStandardErrorLines();
        var logLines = stderrLines.Where(line => _logLinePattern.IsMatch(line)).ToList();

        Assert.True(
            logLines.Count == 0,
            $"Expected no log lines on stderr in stdio mode without --log-file, but found {logLines.Count}:{Environment.NewLine}{string.Join(Environment.NewLine, logLines)}");
    }

    [Fact]
    public async Task StdioTransport_WithLogFile_WritesLogsToFile()
    {
        var logFilePath = Path.Combine(
            Path.GetTempPath(),
            $"poshmcp-stdio-logging-test-{Guid.NewGuid():N}.log");

        try
        {
            using var configFile = new TemporaryConfigFile();

            // The serve subcommand honours --log-file; explicitConfigPath appends --config
            using var server = new InProcessMcpServer(
                Logger,
                extraArgs: $"serve --log-file \"{logFilePath}\"",
                explicitConfigPath: configFile.Path);

            await server.StartAsync();

            using var client = new ExternalMcpClient(Logger, server);
            await client.StartAsync();

            // Exercise a round-trip so the server actually emits at least one log entry
            await client.SendListToolsAsync();

            // Give Serilog's file sink time to flush
            await Task.Delay(500);
        }
        finally
        {
            // Serilog RollingInterval.Day appends a date suffix: base20240101.log
            // Search for any file matching the base name to cover the rolling filename
            var logDir = Path.GetDirectoryName(logFilePath) ?? Path.GetTempPath();
            var logBase = Path.GetFileNameWithoutExtension(logFilePath);
            var logFiles = Directory.GetFiles(logDir, $"{logBase}*.log");

            try
            {
                Assert.True(
                    logFiles.Length > 0,
                    $"Expected Serilog to create a log file near path: {logFilePath}");

                var logContent = string.Concat(logFiles.Select(File.ReadAllText));
                Assert.False(
                    string.IsNullOrWhiteSpace(logContent),
                    "Expected the log file to contain at least one log entry");
            }
            finally
            {
                foreach (var file in logFiles)
                {
                    try { File.Delete(file); } catch { /* best effort cleanup */ }
                }

                if (File.Exists(logFilePath))
                {
                    try { File.Delete(logFilePath); } catch { /* best effort cleanup */ }
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private sealed class TemporaryConfigFile : IDisposable
    {
        public string Path { get; }

        public TemporaryConfigFile()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"poshmcp-stdio-logging-cfg-{Guid.NewGuid():N}.json");

            File.WriteAllText(Path, """
                {
                  "PowerShellConfiguration": {
                    "FunctionNames": ["Get-Date"],
                    "Modules": [],
                    "ExcludePatterns": [],
                    "IncludePatterns": []
                  }
                }
                """);
        }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                try { File.Delete(Path); } catch { /* best effort cleanup */ }
            }
        }
    }
}
