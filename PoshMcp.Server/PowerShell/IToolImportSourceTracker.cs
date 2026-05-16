using System;
using System.Collections.Generic;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Identifies which configuration source resolved a discovered PowerShell command.
/// </summary>
public enum ToolImportSource
{
    /// <summary>The command came from <c>CommandNames</c> / legacy function names.</summary>
    CommandName,

    /// <summary>The command came from <c>Modules</c>.</summary>
    Module,

    /// <summary>The command came from <c>IncludePatterns</c> discovery.</summary>
    Pattern,

    /// <summary>The command's source could not be resolved authoritatively.</summary>
    Unknown,
}

/// <summary>
/// Recorded discovery-source attribution for one PowerShell command.
/// </summary>
/// <param name="Source">Winning discovery source after configured priority is applied.</param>
/// <param name="SourceDetail">The configured string that produced the source.</param>
public sealed record ToolImportSourceInfo(
    ToolImportSource Source,
    string SourceDetail);

/// <summary>
/// Records the configuration source that resolved each discovered command during a
/// single discovery cycle.
/// </summary>
/// <remarks>
/// <para>
/// Implementations MUST be safe for concurrent record/read calls from multiple
/// discovery threads. The tracker is intended to live for one discovery cycle —
/// callers either dispose it or replace it on each call to
/// <see cref="McpToolFactoryV2.GetToolsListAsync(PowerShellConfiguration, Microsoft.Extensions.Logging.ILogger, System.Threading.CancellationToken)"/>.
/// </para>
/// <para>
/// The tracker is populated at the same call sites that already resolve command
/// discovery (in-process <c>Get-Command</c> enumeration or OOP <see cref="OutOfProcess.RemoteToolSchema"/>
/// consumption) so doctor reporting never needs to re-run <c>Get-Command</c> or
/// <c>Get-Module</c> to recover per-tool attribution.
/// </para>
/// </remarks>
public interface IToolImportSourceTracker
{
    /// <summary>
    /// Records the resolved import source for a command. If the same command is
    /// recorded multiple times, the first value wins so the tracker preserves the
    /// discovery pipeline's precedence order.
    /// </summary>
    void RecordToolSource(string commandName, ToolImportSource source, string sourceDetail);

    /// <summary>
    /// Snapshot of the per-command import sources recorded so far.
    /// </summary>
    IReadOnlyDictionary<string, ToolImportSourceInfo> ToolSources { get; }
}

/// <summary>
/// Default thread-safe in-memory implementation of <see cref="IToolImportSourceTracker"/>.
/// </summary>
public sealed class ToolImportSourceTracker : IToolImportSourceTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ToolImportSourceInfo> _toolSources =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void RecordToolSource(string commandName, ToolImportSource source, string sourceDetail)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return;
        }

        lock (_gate)
        {
            if (!_toolSources.ContainsKey(commandName))
            {
                _toolSources[commandName] = new ToolImportSourceInfo(source, sourceDetail ?? string.Empty);
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, ToolImportSourceInfo> ToolSources
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, ToolImportSourceInfo>(_toolSources, StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}

/// <summary>
/// Returns the doctor wire-format string literal for a <see cref="ToolImportSource"/>.
/// </summary>
public static class ToolImportSourceVocabulary
{
    /// <summary>String literal for a <see cref="ToolImportSource"/>.</summary>
    public static string ToWireValue(ToolImportSource source) => source switch
    {
        ToolImportSource.CommandName => "commandName",
        ToolImportSource.Module => "module",
        ToolImportSource.Pattern => "pattern",
        ToolImportSource.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown ToolImportSource."),
    };
}
