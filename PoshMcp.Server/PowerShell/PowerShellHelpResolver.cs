using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Text;
using Microsoft.Extensions.Logging;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Resolves <c>Get-Help</c>-derived metadata for in-process PowerShell command discovery,
/// implementing the lookup half of spec 010 (the precedence chain logic itself lives in
/// <see cref="HelpAwareToolMetadataSource"/>).
/// </summary>
/// <remarks>
/// <para>
/// FR-570 / FR-571: <c>Get-Help</c> is invoked at most once per command and the result is
/// cached for the lifetime of this resolver instance (and therefore for the lifetime of the
/// owning <see cref="IPowerShellRunspace"/>). Per-parameter <c>Get-Help</c> calls are
/// forbidden — parameter help is read from the per-command result.
/// </para>
/// <para>
/// FR-502 / FR-580 / FR-581: any failure (null result, exception, missing MAML) is treated
/// as "no value" and recorded as an empty cache entry. Discovery never fails because help
/// cannot be resolved.
/// </para>
/// </remarks>
public sealed class PowerShellHelpResolver
{
    private readonly ConcurrentDictionary<string, CommandHelpInfo> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns a structured snapshot of <c>Get-Help</c> output for <paramref name="commandName"/>.
    /// First call per command name invokes <c>Get-Help</c>; subsequent calls return the cached
    /// result. Never throws — failures yield an empty <see cref="CommandHelpInfo"/>.
    /// </summary>
    /// <param name="commandName">PowerShell command name (e.g., <c>Get-Process</c>).</param>
    /// <param name="powerShell">A live PSPowerShell instance from the discovery runspace.</param>
    /// <param name="logger">Diagnostic logger for help-resolution failures.</param>
    public CommandHelpInfo Resolve(string commandName, PSPowerShell powerShell, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return CommandHelpInfo.Empty;
        }

        return _cache.GetOrAdd(commandName, name => ResolveCore(name, powerShell, logger));
    }

    /// <summary>Drops all cached help entries.</summary>
    public void ClearCache() => _cache.Clear();

    private static CommandHelpInfo ResolveCore(string commandName, PSPowerShell powerShell, ILogger logger)
    {
        try
        {
            powerShell.Commands.Clear();
            powerShell.Streams.ClearStreams();
            powerShell.AddCommand("Get-Help")
                .AddParameter("Name", commandName)
                .AddParameter("Full")
                .AddParameter("ErrorAction", "SilentlyContinue");

            var results = powerShell.Invoke();
            powerShell.Commands.Clear();

            if (results == null || results.Count == 0 || results[0] == null)
            {
                return CommandHelpInfo.Empty;
            }

            return BuildFromHelpObject(results[0], commandName);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Get-Help failed for {Command}; treating as no help available.", commandName);
            try { powerShell.Commands.Clear(); } catch { /* best-effort cleanup */ }
            return CommandHelpInfo.Empty;
        }
    }

    private static CommandHelpInfo BuildFromHelpObject(PSObject help, string commandName)
    {
        var synopsisRaw = SafeGetString(help, "Synopsis");
        var synopsis = string.Equals(synopsisRaw?.Trim(), commandName, StringComparison.Ordinal)
            ? null // FR-500 step 1: synopsis equal to command name = auto-generated, treat as missing.
            : synopsisRaw;

        var longDescription = ExtractDescriptionBody(help);
        var parameters = ExtractParameterDescriptions(help);

        return new CommandHelpInfo(synopsis, longDescription, parameters);
    }

    private static string? ExtractDescriptionBody(PSObject help)
    {
        var descriptionProperty = help.Properties["description"];
        if (descriptionProperty?.Value is null)
        {
            return null;
        }

        return JoinMamlParagraphs(descriptionProperty.Value);
    }

    private static IReadOnlyDictionary<string, string> ExtractParameterDescriptions(PSObject help)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var parametersProperty = help.Properties["parameters"];
        if (parametersProperty?.Value is null)
        {
            return result;
        }

        // .parameters is a PSCustomObject with a .parameter array (or single parameter object).
        var parameterEntries = ResolveParameterArray(parametersProperty.Value);
        if (parameterEntries == null)
        {
            return result;
        }

        foreach (var entry in parameterEntries)
        {
            if (entry == null) continue;
            var psEntry = entry as PSObject ?? new PSObject(entry);
            var name = SafeGetString(psEntry, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var descriptionValue = psEntry.Properties["description"]?.Value;
            if (descriptionValue == null)
            {
                continue;
            }

            var joined = JoinMamlParagraphs(descriptionValue);
            if (!string.IsNullOrWhiteSpace(joined))
            {
                result[name!] = joined!;
            }
        }

        return result;
    }

    private static IEnumerable<object?>? ResolveParameterArray(object value)
    {
        // Get-Help returns .parameters as a PSObject wrapping a PSCustomObject. The
        // synthesized .parameter[] member lives on the PSObject wrapper — calling
        // .BaseObject would dereference to the PSCustomObject marker type, which has
        // no public members, dropping the parameter array on the floor (issue #242).
        // Access Properties directly on the wrapper, only falling back to BaseObject
        // unwrapping if the wrapper does not expose .parameter.
        var holder = value as PSObject ?? new PSObject(value);
        var parameterMember = holder.Properties["parameter"]?.Value;
        if (parameterMember == null && value is PSObject pso && pso.BaseObject is { } baseObj && !ReferenceEquals(baseObj, pso))
        {
            var baseHolder = baseObj as PSObject ?? new PSObject(baseObj);
            parameterMember = baseHolder.Properties["parameter"]?.Value;
        }

        if (parameterMember == null)
        {
            // Some shapes pass the parameter array directly.
            return value as IEnumerable<object?>
                   ?? (value as System.Collections.IEnumerable)?.Cast<object?>();
        }

        if (parameterMember is System.Collections.IEnumerable enumerable && parameterMember is not string)
        {
            return enumerable.Cast<object?>();
        }

        return new[] { (object?)parameterMember };
    }

    private static string? JoinMamlParagraphs(object? value)
    {
        if (value == null)
        {
            return null;
        }

        var unwrapped = value is PSObject pso ? pso.BaseObject ?? pso : value;

        // MAML paragraphs come back as either a string, a single PSObject with a `Text`
        // property, or an array of those.
        if (unwrapped is string s)
        {
            return s;
        }

        if (unwrapped is System.Collections.IEnumerable enumerable && unwrapped is not string)
        {
            var paragraphs = new List<string>();
            foreach (var item in enumerable)
            {
                var text = ExtractParaText(item);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    paragraphs.Add(text!);
                }
            }
            return paragraphs.Count == 0
                ? null
                : string.Join(DescriptionSanitizer.ParagraphSeparator, paragraphs);
        }

        return ExtractParaText(unwrapped);
    }

    private static string? ExtractParaText(object? item)
    {
        if (item == null)
        {
            return null;
        }
        if (item is string s)
        {
            return s;
        }

        var pso = item as PSObject ?? new PSObject(item);
        var textProperty = pso.Properties["Text"]?.Value;
        if (textProperty != null)
        {
            return textProperty.ToString();
        }

        return pso.ToString();
    }

    private static string? SafeGetString(PSObject obj, string propertyName)
    {
        var prop = obj.Properties[propertyName];
        if (prop?.Value == null)
        {
            return null;
        }

        // Synopsis can come back wrapped in PSObject, plain string, or even a
        // ParamTextSpan-style array. Stringify defensively.
        var raw = prop.Value;
        if (raw is string s)
        {
            return s;
        }

        if (raw is System.Collections.IEnumerable enumerable && raw is not PSObject)
        {
            var sb = new StringBuilder();
            foreach (var item in enumerable)
            {
                if (item == null) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(item);
            }
            return sb.ToString();
        }

        return raw.ToString();
    }
}

/// <summary>
/// Snapshot of <c>Get-Help</c> output for a single command, in the shape
/// <see cref="HelpAwareToolMetadataSource"/> consumes.
/// </summary>
/// <param name="Synopsis">Raw <c>.Synopsis</c> text, already filtered for the
/// auto-generated case (equal to command name → <c>null</c>). Sanitization is the
/// metadata source's responsibility, not this record's.</param>
/// <param name="LongDescription">Raw <c>.Description</c> body with paragraphs joined by
/// <see cref="DescriptionSanitizer.ParagraphSeparator"/>.</param>
/// <param name="ParameterDescriptions">Map from parameter name to raw paragraph-joined
/// description text. Empty when no per-parameter help is present.</param>
public sealed record CommandHelpInfo(
    string? Synopsis,
    string? LongDescription,
    IReadOnlyDictionary<string, string> ParameterDescriptions)
{
    /// <summary>The "no help available" sentinel; returned for any resolution failure.</summary>
    public static CommandHelpInfo Empty { get; } = new(
        Synopsis: null,
        LongDescription: null,
        ParameterDescriptions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}
