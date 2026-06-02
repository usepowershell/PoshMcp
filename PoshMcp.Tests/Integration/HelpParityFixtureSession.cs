using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Spec 010 helper that brings up an <see cref="InProcessMcpServer"/> against the
/// HelpParityFixture corpus for a specified RuntimeMode (InProcess or OutOfProcess),
/// captures one <c>tools/list</c> snapshot, and tears the server down. Used by
/// FR-521 parity tests, FR-550 regression tests, and FR-511 consistency tests.
/// </summary>
internal sealed class HelpParityFixtureSession : IAsyncDisposable
{
    public const string RuntimeModeInProcess = "InProcess";
    public const string RuntimeModeOutOfProcess = "OutOfProcess";

    /// <summary>
    /// Names exported by HelpParityFixture.psm1. Kept in sync with the fixture
    /// (PoshMcp.Tests/Fixtures/Modules/HelpParityFixture/HelpParityFixture.psm1).
    /// </summary>
    public static readonly IReadOnlyList<string> FixtureCommands = new[]
    {
        "Get-FixtureSynopsisOnly",
        "Get-FixtureFullHelp",
        "Get-FixtureHelpMessageOnly",
        "Get-FixtureValidateSetScalar",
        "Get-FixtureValidateSetArray",
        "Get-FixtureBare",
    };

    private readonly ILogger _logger;
    private readonly ITestOutputHelper _output;
    private readonly string _runtimeMode;
    private InProcessMcpServer? _server;
    private ExternalMcpClient? _client;
    private string? _configPath;
    private string? _previousPSModulePath;

    public HelpParityFixtureSession(string runtimeMode, ILogger logger, ITestOutputHelper output)
    {
        _runtimeMode = runtimeMode;
        _logger = logger;
        _output = output;
    }

    /// <summary>
    /// Snapshot of the <c>tools/list</c> response (full JSON-RPC envelope).
    /// </summary>
    public JObject ToolsListResponse { get; private set; } = new JObject();

    /// <summary>
    /// Convenience accessor for the <c>result.tools</c> array.
    /// </summary>
    public JArray Tools =>
        ToolsListResponse["result"]?["tools"] as JArray
        ?? throw new InvalidOperationException("tools/list response did not contain result.tools");

    public async Task StartAsync()
    {
        var workspaceRoot = ResolveWorkspaceRoot();
        var fixtureRoot = Path.Combine(workspaceRoot, "PoshMcp.Tests", "Fixtures", "Modules");

        if (!File.Exists(Path.Combine(fixtureRoot, "HelpParityFixture", "HelpParityFixture.psd1")))
        {
            throw new FileNotFoundException(
                $"HelpParityFixture not found under {fixtureRoot}");
        }

        _configPath = WriteConfigFile(fixtureRoot);

        // PSModulePath must include the fixture root so the in-process runspace
        // (and the OOP subprocess via inherited env) auto-loads HelpParityFixture
        // by simple name. Mirrors capture-snapshots.ps1.
        _previousPSModulePath = Environment.GetEnvironmentVariable("PSModulePath");
        var sep = Path.PathSeparator;
        var newPath = string.IsNullOrEmpty(_previousPSModulePath)
            ? fixtureRoot
            : $"{fixtureRoot}{sep}{_previousPSModulePath}";
        Environment.SetEnvironmentVariable("PSModulePath", newPath);

        _logger.LogInformation(
            "Starting MCP server for HelpParityFixture session (RuntimeMode={Mode}, ConfigPath={Path})",
            _runtimeMode, _configPath);

        _server = new InProcessMcpServer(_logger, explicitConfigPath: _configPath);
        await _server.StartAsync();

        _client = new ExternalMcpClient(
            _logger,
            _server,
            startupTimeout: TimeSpan.FromSeconds(120));
        await _client.StartAsync();

        // FR-521 pre-warm: discovery already invoked Get-Help during tool registration,
        // but call tools/list once and discard, then call again for the snapshot. The
        // second call lets MAML caches stabilize across both runspaces.
        _ = await _client.SendListToolsAsync();
        ToolsListResponse = await _client.SendListToolsAsync();

        _logger.LogInformation(
            "Captured tools/list for RuntimeMode={Mode}: {Count} tools",
            _runtimeMode, Tools.Count);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _client?.Dispose();
            _server?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing HelpParityFixtureSession");
        }

        if (_configPath is not null && File.Exists(_configPath))
        {
            try { File.Delete(_configPath); } catch { }
        }

        if (_previousPSModulePath is not null)
        {
            Environment.SetEnvironmentVariable("PSModulePath", _previousPSModulePath);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Returns the subset of tools whose source command name matches <paramref name="commandName"/>.
    /// Multiple parameter sets of the same command produce multiple tool entries
    /// distinguished by the <c>title</c> field (which carries the original PowerShell
    /// command name) and the parameter-set suffix on <c>name</c>.
    /// </summary>
    public IReadOnlyList<JObject> GetToolsForCommand(string commandName)
    {
        return Tools
            .OfType<JObject>()
            .Where(t => string.Equals(t["title"]?.ToString(), commandName, StringComparison.Ordinal))
            .ToList();
    }

    private string WriteConfigFile(string fixtureRoot)
    {
        var config = new
        {
            Logging = new { LogLevel = new { Default = "Warning" } },
            PowerShellConfiguration = new
            {
                RuntimeMode = _runtimeMode,
                CommandNames = FixtureCommands.ToArray(),
                Modules = new[]
                {
                    "Microsoft.PowerShell.Management",
                    "HelpParityFixture",
                },
                ExcludePatterns = Array.Empty<string>(),
                IncludePatterns = new[] { "*" },
                EnableDynamicReloadTools = false,
                EnableConfigurationTroubleshootingTool = false,
                Performance = new
                {
                    EnableResultCaching = false,
                    UseDefaultDisplayProperties = true,
                },
                SubprocessHostMode = "Pool",
                Environment = new
                {
                    ModulePaths = new[] { fixtureRoot },
                    ImportModules = new[] { "HelpParityFixture" },
                },
            },
            McpServer = new { IdleSessionTimeoutSeconds = 60 },
            Authentication = new { Enabled = false },
        };

        var json = JsonConvert.SerializeObject(config, Formatting.Indented);
        var path = Path.Combine(
            Path.GetTempPath(),
            $"poshmcp-helpparity-{_runtimeMode.ToLowerInvariant()}-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string ResolveWorkspaceRoot()
    {
        var current = Directory.GetCurrentDirectory();
        while (current is not null && !File.Exists(Path.Combine(current, "PoshMcp.sln")))
        {
            current = Path.GetDirectoryName(current);
        }
        return current
            ?? throw new InvalidOperationException(
                $"Could not find workspace root from {Directory.GetCurrentDirectory()}");
    }
}
