using System;
using System.Collections.Generic;

namespace PoshMcp;

/// <summary>Converts a <see cref="DoctorReport"/> into human-readable formatted text.</summary>
public static class DoctorTextRenderer
{
    private const int BannerInnerWidth = 42;

    /// <summary>Renders the full doctor report as formatted text output.</summary>
    public static string Render(DoctorReport report)
    {
        var parts = new List<string>
        {
            RenderBanner(report.Summary),
            FormatSection("Runtime Settings",      RenderRuntimeSettings(report.RuntimeSettings)),
            FormatSection("Environment Variables", RenderEnvironmentVariables(report.EnvironmentVariables)),
            FormatSection("PowerShell",            RenderPowerShell(report.PowerShell)),
            FormatSection("Functions/Tools",       RenderFunctionsTools(report.FunctionsTools)),
            FormatSection("MCP Definitions",       RenderMcpDefinitions(report.McpDefinitions)),
        };

        var warningsBody = RenderWarnings(report.Warnings);
        if (!string.IsNullOrEmpty(warningsBody))
            parts.Add(FormatSection("Warnings", warningsBody));

        return string.Join("\n\n", parts);
    }

    private static string FormatSection(string name, string body)
        => $"{RenderSectionHeader(name)}\n{body}";

    private static string RenderSectionHeader(string name)
        => $"── {name} {new string('─', Math.Max(0, 44 - name.Length - 4))}";

    private static string StatusSymbol(bool ok) => ok ? "✓" : "✗";

    private static string RenderBanner(DoctorSummary summary)
    {
        var symbol = summary.Status switch
        {
            "healthy" => "✓",
            "warnings" => "⚠",
            _ => "✗",
        };
        var content = $"  PoshMcp Doctor  {symbol} {summary.Status}".PadRight(BannerInnerWidth);
        return string.Join("\n",
            $"╔{new string('═', BannerInnerWidth)}╗",
            $"║{content}║",
            $"╚{new string('═', BannerInnerWidth)}╝");
    }

    private static string RenderRuntimeSettings(RuntimeSettingsSection section)
    {
        static string Row(string key, ResolvedSetting s)
            => $"  {key,-12}: {s.Value ?? "(not set)",-16} ({s.Source})";

        return string.Join("\n",
            Row("configuration", section.ConfigurationPath),
            Row("transport", section.Transport),
            Row("log-level", section.LogLevel),
            Row("session-mode", section.SessionMode),
            Row("runtime-mode", section.RuntimeMode),
            Row("mcp-path", section.McpPath));
    }

    private static string RenderEnvironmentVariables(Dictionary<string, string?> variables)
    {
        var lines = new List<string>();
        foreach (var (key, value) in variables)
            lines.Add($"  {key,-35}: {value ?? "(not set)"}");
        return string.Join("\n", lines);
    }

    private static string RenderPowerShell(PowerShellSection section)
    {
        var lines = new List<string>
        {
            $"  version      : {section.Version}",
            $"  module-paths : {section.ModulePathEntries}",
        };
        foreach (var path in section.ModulePaths)
            lines.Add($"    - {path}");
        lines.Add($"  oop-module-paths : {section.OopModulePathEntries}");
        foreach (var path in section.OopModulePaths)
            lines.Add($"    - {path}");
        return string.Join("\n", lines);
    }

    private static string RenderFunctionsTools(FunctionsToolsSection section)
    {
        var allFound = section.ConfiguredFunctionsMissing.Count == 0;
        var lines = new List<string>
        {
            $"  {StatusSymbol(allFound)} {section.ConfiguredFunctionsFound.Count}/{section.ConfiguredFunctionCount} configured functions found",
            $"  tools discovered: {section.ToolCount}",
        };

        foreach (var fs in section.ConfiguredFunctionStatus)
        {
            var sym = StatusSymbol(fs.Found);
            var status = fs.Found ? "FOUND" : "MISSING";
            var extra = fs.Found
                ? $"matched: {string.Join(", ", fs.MatchedToolNames)}"
                : $"reason: {fs.ResolutionReason ?? "unknown"}";
            lines.Add($"  - {fs.FunctionName} → {fs.ExpectedToolName} [{sym} {status}] {extra}");
        }

        if (section.ToolNames.Count > 0)
        {
            lines.Add("  tool names:");
            foreach (var name in section.ToolNames)
                lines.Add($"  - {name}");
        }

        return string.Join("\n", lines);
    }

    private static string RenderMcpDefinitions(McpDefinitionsSection section)
    {
        var lines = new List<string>
        {
            $"  {"resources",-9}: {section.Resources.Configured} configured | {section.Resources.Valid} valid",
        };
        foreach (var e in section.Resources.Errors)
            lines.Add($"  ✖ {e}");
        foreach (var w in section.Resources.Warnings)
            lines.Add($"  ⚠ {w}");
        lines.Add($"  {"prompts",-9}: {section.Prompts.Configured} configured | {section.Prompts.Valid} valid");
        foreach (var e in section.Prompts.Errors)
            lines.Add($"  ✖ {e}");
        foreach (var w in section.Prompts.Warnings)
            lines.Add($"  ⚠ {w}");
        return string.Join("\n", lines);
    }

    private static string RenderWarnings(List<string> warnings)
    {
        if (warnings.Count == 0)
            return string.Empty;

        var lines = new List<string>(warnings.Count);
        foreach (var w in warnings)
            lines.Add($"  ⚠ {w}");
        return string.Join("\n", lines);
    }
}
