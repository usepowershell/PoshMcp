using System;
using System.Collections.Generic;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Records the precedence step that resolved each MCP tool and parameter description
/// during a single discovery cycle. Spec 010 FR-582 / FR-583 / SC-207 use the recorded
/// values to populate the doctor report's <c>descriptionSource</c> fields. The same
/// values are reusable by the FR-590 OpenTelemetry counters in issue #231 — the
/// vocabulary and enum types are shared, never duplicated.
/// </summary>
/// <remarks>
/// <para>
/// Implementations MUST be safe for concurrent record/read calls from multiple
/// discovery threads. The tracker is intended to live for one discovery cycle —
/// callers either dispose it or replace it on each call to
/// <see cref="McpToolFactoryV2.GetToolsListAsync"/>.
/// </para>
/// <para>
/// The tracker is populated at the same call site that invokes
/// <see cref="IToolMetadataSource.ResolveToolDescription"/> /
/// <see cref="IToolMetadataSource.ResolveParameterDescription"/> so the recorded
/// source matches the actually-emitted description without re-running the precedence
/// chain.
/// </para>
/// </remarks>
public interface IToolDescriptionSourceTracker
{
    /// <summary>
    /// Records the resolved tool description source for a command. If multiple
    /// parameter sets of the same command resolve to different sources, the first
    /// recorded value wins (FR-501: tool description text is per-command, not
    /// per-parameter-set, so the precedence step is also per-command).
    /// </summary>
    void RecordToolSource(string commandName, ToolDescriptionSource source);

    /// <summary>
    /// Records the resolved parameter description source for a single parameter on a
    /// command. If the same parameter is recorded multiple times (e.g., it appears in
    /// multiple parameter sets — FR-511), the first recorded value wins.
    /// </summary>
    void RecordParameterSource(string commandName, string parameterName, ParameterDescriptionSource source);

    /// <summary>
    /// Snapshot of the per-command tool description sources recorded so far.
    /// </summary>
    IReadOnlyDictionary<string, ToolDescriptionSource> ToolSources { get; }

    /// <summary>
    /// Snapshot of the per-parameter description sources recorded so far, keyed by
    /// command name and then by parameter name.
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, ParameterDescriptionSource>> ParameterSources { get; }
}

/// <summary>
/// Default thread-safe in-memory implementation of
/// <see cref="IToolDescriptionSourceTracker"/>.
/// </summary>
public sealed class ToolDescriptionSourceTracker : IToolDescriptionSourceTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ToolDescriptionSource> _toolSources =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, ParameterDescriptionSource>> _parameterSources =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void RecordToolSource(string commandName, ToolDescriptionSource source)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return;
        }

        lock (_gate)
        {
            if (!_toolSources.ContainsKey(commandName))
            {
                _toolSources[commandName] = source;
            }
        }
    }

    /// <inheritdoc />
    public void RecordParameterSource(string commandName, string parameterName, ParameterDescriptionSource source)
    {
        if (string.IsNullOrWhiteSpace(commandName) || string.IsNullOrWhiteSpace(parameterName))
        {
            return;
        }

        lock (_gate)
        {
            if (!_parameterSources.TryGetValue(commandName, out var perParam))
            {
                perParam = new Dictionary<string, ParameterDescriptionSource>(StringComparer.OrdinalIgnoreCase);
                _parameterSources[commandName] = perParam;
            }

            if (!perParam.ContainsKey(parameterName))
            {
                perParam[parameterName] = source;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, ToolDescriptionSource> ToolSources
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, ToolDescriptionSource>(_toolSources, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, ParameterDescriptionSource>> ParameterSources
    {
        get
        {
            lock (_gate)
            {
                var result = new Dictionary<string, IReadOnlyDictionary<string, ParameterDescriptionSource>>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in _parameterSources)
                {
                    result[kvp.Key] = new Dictionary<string, ParameterDescriptionSource>(
                        kvp.Value, StringComparer.OrdinalIgnoreCase);
                }
                return result;
            }
        }
    }
}

/// <summary>
/// Returns the wire-format string literal for a <see cref="ToolDescriptionSource"/> as
/// defined by spec 010 FR-583. Centralized here so the doctor JSON serializer
/// (FR-583) and the OpenTelemetry counter tags (FR-590, issue #231) emit byte-identical
/// values without duplicating the vocabulary.
/// </summary>
public static class DescriptionSourceVocabulary
{
    /// <summary>FR-583 string literal for a <see cref="ToolDescriptionSource"/>.</summary>
    public static string ToWireValue(ToolDescriptionSource source) => source switch
    {
        ToolDescriptionSource.Synopsis => "synopsis",
        ToolDescriptionSource.Description => "description",
        ToolDescriptionSource.Syntax => "syntax",
        ToolDescriptionSource.Name => "name",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown ToolDescriptionSource."),
    };

    /// <summary>FR-583 string literal for a <see cref="ParameterDescriptionSource"/>.</summary>
    public static string ToWireValue(ParameterDescriptionSource source) => source switch
    {
        ParameterDescriptionSource.HelpParameter => "helpParameter",
        ParameterDescriptionSource.HelpMessage => "helpMessage",
        ParameterDescriptionSource.ValidateSet => "validateSet",
        ParameterDescriptionSource.TypeFallback => "typeFallback",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown ParameterDescriptionSource."),
    };
}
