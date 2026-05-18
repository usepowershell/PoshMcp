using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace PoshMcp.Server.McpResources;

/// <summary>
/// Immutable registry that maps PowerShell nouns to MCP resource entries, derived from discovered command names.
/// Built once per discovery cycle via <see cref="Build"/>; thread-safe after construction.
/// </summary>
public sealed class NounRegistry
{
    private static readonly Regex ResourceNameRegex =
        new(@"(?<=[a-z])([A-Z])|(?<=[A-Z])([A-Z][a-z])", RegexOptions.Compiled);

    private const string UriPrefix = "poshmcp://resources/";

    private readonly FrozenDictionary<string, NounEntry> _byNoun;
    private readonly FrozenDictionary<string, NounEntry> _byResourceName;

    private NounRegistry(
        FrozenDictionary<string, NounEntry> byNoun,
        FrozenDictionary<string, NounEntry> byResourceName,
        IReadOnlyList<NounEntry> allEntries)
    {
        _byNoun = byNoun;
        _byResourceName = byResourceName;
        AllEntries = allEntries;
    }

    /// <summary>
    /// Builds an immutable <see cref="NounRegistry"/> from the given set of discovered command names.
    /// Commands must be supplied in discovery order; first-writer-wins for resource name conflicts.
    /// </summary>
    /// <param name="discoveredCommandNames">
    /// Full list of discovered PowerShell command names in priority order
    /// (CommandNames → Modules → IncludePatterns, within each in declaration order).
    /// </param>
    /// <param name="logger">Logger; receives a warning for each resource name conflict.</param>
    public static NounRegistry Build(IEnumerable<string> discoveredCommandNames, ILogger logger)
    {
        var commands = discoveredCommandNames.ToList();

        // resource_name → winning (non-conflicted) NounEntry
        var claimedByResourceName = new Dictionary<string, NounEntry>(StringComparer.OrdinalIgnoreCase);
        var allEntries = new List<NounEntry>();

        foreach (var cmd in commands)
        {
            var noun = ExtractNounFromCommandName(cmd);
            if (noun is null)
                continue;

            var verb = ExtractVerbFromCommandName(cmd);
            if (!verb.Equals("Get", StringComparison.OrdinalIgnoreCase))
                continue;

            var resourceName = DeriveResourceName(noun);
            var uri = $"{UriPrefix}{resourceName}";
            var canonicalGetCommand = $"Get-{noun}";

            if (!claimedByResourceName.TryGetValue(resourceName, out var winner))
            {
                var entry = new NounEntry(noun, resourceName, uri, canonicalGetCommand, false);
                claimedByResourceName[resourceName] = entry;
                allEntries.Add(entry);
            }
            else
            {
                var conflicted = new NounEntry(noun, resourceName, uri, canonicalGetCommand, true);
                allEntries.Add(conflicted);
                logger.LogWarning(
                    "NounRegistry: resource name '{ResourceName}' already claimed by '{WinningCommand}'; " +
                    "command '{ConflictingCommand}' (noun '{Noun}') is conflicted and will not produce a resource.",
                    resourceName, winner.CanonicalGetCommand, cmd, noun);
            }
        }

        var byNoun = claimedByResourceName.Values
            .ToFrozenDictionary(e => e.Noun, StringComparer.OrdinalIgnoreCase);

        var byResourceName = claimedByResourceName.Values
            .ToFrozenDictionary(e => e.ResourceName, StringComparer.OrdinalIgnoreCase);

        return new NounRegistry(byNoun, byResourceName, allEntries.AsReadOnly());
    }

    /// <summary>
    /// Returns the registry entry for the given PascalCase noun, or <c>null</c> if the noun is not
    /// resourceable (no canonical <c>Get-{Noun}</c> command found) or is conflicted.
    /// </summary>
    /// <param name="noun">PascalCase noun (e.g. <c>BamiTenantUser</c>). Case-insensitive.</param>
    public NounEntry? GetEntry(string noun)
        => _byNoun.TryGetValue(noun, out var entry) ? entry : null;

    /// <summary>
    /// Returns the registry entry for the given snake_case resource name, or <c>null</c> if no
    /// non-conflicted entry owns that resource name.
    /// </summary>
    /// <param name="resourceName">Snake_case resource name (e.g. <c>bami_tenant_user</c>). Case-insensitive.</param>
    public NounEntry? GetEntryByResourceName(string resourceName)
        => _byResourceName.TryGetValue(resourceName, out var entry) ? entry : null;

    /// <summary>
    /// All registered noun entries, including conflicted ones. Useful for diagnostics and doctor reports.
    /// </summary>
    public IReadOnlyList<NounEntry> AllEntries { get; }

    /// <summary>
    /// Extracts the noun (everything after the first <c>-</c>) from a PowerShell <c>Verb-Noun</c> command name.
    /// Returns <c>null</c> for commands with no dash, or where the dash is the last character.
    /// Handles module-qualified names (e.g. <c>ModuleA\Get-User</c> → noun <c>User</c>).
    /// </summary>
    internal static string? ExtractNounFromCommandName(string commandName)
    {
        var dashIndex = commandName.IndexOf('-');
        if (dashIndex < 0 || dashIndex == commandName.Length - 1)
            return null;
        return commandName[(dashIndex + 1)..];
    }

    /// <summary>
    /// Derives the snake_case resource name from a PascalCase noun.
    /// Examples: <c>BamiTenantUser</c> → <c>bami_tenant_user</c>, <c>HTMLParser</c> → <c>html_parser</c>.
    /// </summary>
    internal static string DeriveResourceName(string noun)
        => ResourceNameRegex.Replace(noun, "_$1$2").ToLowerInvariant();

    private static string ExtractVerbFromCommandName(string commandName)
    {
        var dashIndex = commandName.IndexOf('-');
        if (dashIndex < 0)
            return commandName;

        // Strip optional module prefix (e.g. "ModuleA\Get-User" → verb "Get")
        var verbPart = commandName[..dashIndex];
        var lastBackslash = verbPart.LastIndexOf('\\');
        return lastBackslash >= 0 ? verbPart[(lastBackslash + 1)..] : verbPart;
    }
}

/// <summary>
/// A noun-to-resource mapping entry in the <see cref="NounRegistry"/>.
/// </summary>
/// <param name="Noun">Original PascalCase noun extracted from the command name (e.g. <c>BamiTenantUser</c>).</param>
/// <param name="ResourceName">Snake_case resource identifier (e.g. <c>bami_tenant_user</c>).</param>
/// <param name="Uri">Full MCP resource URI (e.g. <c>poshmcp://resources/bami_tenant_user</c>).</param>
/// <param name="CanonicalGetCommand">The <c>Get-{Noun}</c> command that backs this resource (e.g. <c>Get-BamiTenantUser</c>).</param>
/// <param name="IsConflicted">
/// <c>true</c> when this entry lost a resource name conflict and does not produce a resource.
/// </param>
public sealed record NounEntry(
    string Noun,
    string ResourceName,
    string Uri,
    string CanonicalGetCommand,
    bool IsConflicted);
