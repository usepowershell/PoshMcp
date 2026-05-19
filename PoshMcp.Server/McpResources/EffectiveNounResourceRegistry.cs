using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace PoshMcp.Server.McpResources;

internal static class NounResourceResolution
{
    private const string UriPrefix = "poshmcp://resources/";

    public static EffectiveNounResourceEntry? Resolve(
        NounEntry entry,
        IReadOnlyDictionary<string, NounResourceOverride>? overrides)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.IsConflicted)
        {
            return null;
        }

        var overrideConfig = GetOverride(entry, overrides);
        if (overrideConfig?.Disabled == true)
        {
            return null;
        }

        var resourceName = string.IsNullOrWhiteSpace(overrideConfig?.ResourceName)
            ? entry.ResourceName
            : overrideConfig.ResourceName!;

        var uri = string.IsNullOrWhiteSpace(overrideConfig?.Uri)
            ? DeriveUri(resourceName)
            : overrideConfig.Uri!;

        var description = string.IsNullOrWhiteSpace(overrideConfig?.Description)
            ? $"Read the current state of {entry.Noun} via {entry.CanonicalGetCommand}"
            : overrideConfig.Description!;

        return new EffectiveNounResourceEntry(
            entry.Noun,
            resourceName,
            uri,
            entry.CanonicalGetCommand,
            description,
            overrideConfig?.DisableResourceLinkBlock == true);
    }

    public static NounResourceOverride? GetOverride(
        NounEntry entry,
        IReadOnlyDictionary<string, NounResourceOverride>? overrides)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (overrides is null || overrides.Count == 0)
        {
            return null;
        }

        return overrides.TryGetValue(entry.ResourceName, out var overrideConfig)
            ? overrideConfig
            : null;
    }

    public static string DeriveUri(string resourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        return $"{UriPrefix}{resourceName}";
    }
}

internal sealed class EffectiveNounResourceRegistry
{
    private readonly FrozenDictionary<string, EffectiveNounResourceEntry> _byNoun;
    private readonly FrozenDictionary<string, EffectiveNounResourceEntry> _byResourceName;
    private readonly FrozenDictionary<string, EffectiveNounResourceEntry> _byUri;

    private EffectiveNounResourceRegistry(
        IReadOnlyList<EffectiveNounResourceEntry> allEntries,
        FrozenDictionary<string, EffectiveNounResourceEntry> byNoun,
        FrozenDictionary<string, EffectiveNounResourceEntry> byResourceName,
        FrozenDictionary<string, EffectiveNounResourceEntry> byUri)
    {
        AllEntries = allEntries;
        _byNoun = byNoun;
        _byResourceName = byResourceName;
        _byUri = byUri;
    }

    public IReadOnlyList<EffectiveNounResourceEntry> AllEntries { get; }

    public static EffectiveNounResourceRegistry Build(
        NounRegistry nounRegistry,
        IReadOnlyDictionary<string, NounResourceOverride>? overrides)
    {
        ArgumentNullException.ThrowIfNull(nounRegistry);

        var allEntries = nounRegistry.AllEntries
            .Select(entry => NounResourceResolution.Resolve(entry, overrides))
            .Where(entry => entry is not null)
            .Cast<EffectiveNounResourceEntry>()
            .ToList();

        return new EffectiveNounResourceRegistry(
            allEntries.AsReadOnly(),
            BuildLookup(allEntries, entry => entry.Noun),
            BuildLookup(allEntries, entry => entry.ResourceName),
            BuildLookup(allEntries, entry => entry.Uri));
    }

    public EffectiveNounResourceEntry? GetEntry(string noun)
        => _byNoun.TryGetValue(noun, out var entry) ? entry : null;

    public EffectiveNounResourceEntry? GetEntryByResourceName(string resourceName)
        => _byResourceName.TryGetValue(resourceName, out var entry) ? entry : null;

    public EffectiveNounResourceEntry? GetEntryByUri(string uri)
        => _byUri.TryGetValue(uri, out var entry) ? entry : null;

    private static FrozenDictionary<string, EffectiveNounResourceEntry> BuildLookup(
        IEnumerable<EffectiveNounResourceEntry> entries,
        Func<EffectiveNounResourceEntry, string> keySelector)
    {
        var lookup = new Dictionary<string, EffectiveNounResourceEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var key = keySelector(entry);
            if (!lookup.ContainsKey(key))
            {
                lookup[key] = entry;
            }
        }

        return lookup.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed record EffectiveNounResourceEntry(
    string Noun,
    string ResourceName,
    string Uri,
    string CanonicalGetCommand,
    string Description,
    bool DisableResourceLinkBlock);