using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Discovers DefaultDisplayPropertySet metadata for PowerShell commands
/// without executing the commands themselves. Uses Get-Command OutputType
/// and Get-TypeData to find which properties PowerShell would show by default.
/// </summary>
public static class PropertySetDiscovery
{
    private static readonly ConcurrentDictionary<string, IReadOnlyList<string>?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Discovers the DefaultDisplayPropertySet for a command's output type.
    /// Returns null if no display property set is defined (caller should use all properties).
    /// Thread-safe: creates its own runspace for discovery.
    /// </summary>
    public static IReadOnlyList<string>? DiscoverDefaultDisplayProperties(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return null;
        }

        return _cache.GetOrAdd(commandName, static name => DiscoverDefaultDisplayPropertiesCore(name));
    }

    /// <summary>
    /// Batch discovery for multiple commands. Results cached.
    /// Uses a single runspace for efficiency.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>?> DiscoverAll(IEnumerable<string> commandNames)
    {
        if (commandNames == null)
        {
            return new Dictionary<string, IReadOnlyList<string>?>();
        }

        var names = commandNames.ToList();
        if (names.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<string>?>();
        }

        // Check cache first, collect uncached names
        var results = new Dictionary<string, IReadOnlyList<string>?>(StringComparer.OrdinalIgnoreCase);
        var uncached = new List<string>();

        foreach (var name in names)
        {
            if (_cache.TryGetValue(name, out var cached))
            {
                results[name] = cached;
            }
            else
            {
                uncached.Add(name);
            }
        }

        if (uncached.Count == 0)
        {
            return results;
        }

        // Discover all uncached commands in a single runspace
        try
        {
            using var runspace = RunspaceFactory.CreateRunspace();
            runspace.Open();

            foreach (var name in uncached)
            {
                var discovered = DiscoverInRunspace(runspace, name);
                _cache.TryAdd(name, discovered);
                results[name] = discovered;
            }
        }
        catch
        {
            // Best-effort: if we can't open a runspace, return null for all uncached
            foreach (var name in uncached)
            {
                _cache.TryAdd(name, null);
                results[name] = null;
            }
        }

        return results;
    }

    /// <summary>
    /// Clears the discovery cache. Useful for testing.
    /// </summary>
    internal static void ClearCache()
    {
        _cache.Clear();
    }

    private static IReadOnlyList<string>? DiscoverDefaultDisplayPropertiesCore(string commandName)
    {
        try
        {
            using var runspace = RunspaceFactory.CreateRunspace();
            runspace.Open();
            return DiscoverInRunspace(runspace, commandName);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string>? DiscoverInRunspace(Runspace runspace, string commandName)
    {
        try
        {
            // Step 1: Get output type names from Get-Command
            var outputTypeNames = GetOutputTypeNames(runspace, commandName);
            if (outputTypeNames == null || outputTypeNames.Count == 0)
            {
                return null;
            }

            // Step 2: For each output type, try to find DefaultDisplayPropertySet
            foreach (var typeName in outputTypeNames)
            {
                var properties = GetDisplayPropertiesFromTypeData(runspace, typeName);
                if (properties != null && properties.Count > 0)
                {
                    return properties;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static List<string>? GetOutputTypeNames(Runspace runspace, string commandName)
    {
        using var ps = PSPowerShell.Create();
        ps.Runspace = runspace;

        ps.AddCommand("Get-Command")
          .AddParameter("Name", commandName)
          .AddParameter("ErrorAction", "SilentlyContinue");

        var results = ps.Invoke();
        if (ps.HadErrors || results == null || results.Count == 0)
        {
            return null;
        }

        var cmdInfo = results[0];
        var outputTypeProperty = cmdInfo.Properties["OutputType"];
        if (outputTypeProperty?.Value == null)
        {
            return null;
        }

        var typeNames = new List<string>();

        if (outputTypeProperty.Value is IEnumerable<PSObject> outputTypes)
        {
            foreach (var ot in outputTypes)
            {
                // PSTypeName has a Name property with the full type name
                var nameProperty = ot.Properties["Name"];
                if (nameProperty?.Value is string name && !string.IsNullOrWhiteSpace(name))
                {
                    typeNames.Add(name);
                }
            }
        }
        else if (outputTypeProperty.Value is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item is PSObject psObj)
                {
                    var nameProperty = psObj.Properties["Name"];
                    if (nameProperty?.Value is string name && !string.IsNullOrWhiteSpace(name))
                    {
                        typeNames.Add(name);
                    }
                }
                else
                {
                    var wrapped = PSObject.AsPSObject(item);
                    var nameProperty = wrapped.Properties["Name"];
                    if (nameProperty?.Value is string name && !string.IsNullOrWhiteSpace(name))
                    {
                        typeNames.Add(name);
                    }
                }
            }
        }

        return typeNames.Count > 0 ? typeNames : null;
    }

    private static IReadOnlyList<string>? GetDisplayPropertiesFromTypeData(Runspace runspace, string typeName)
    {
        using var ps = PSPowerShell.Create();
        ps.Runspace = runspace;

        ps.AddCommand("Get-TypeData")
          .AddParameter("TypeName", typeName);

        var results = ps.Invoke();
        if (ps.HadErrors || results == null || results.Count == 0)
        {
            return null;
        }

        var typeData = results[0];

        // Navigate: TypeData.DefaultDisplayPropertySet.ReferencedProperties
        var displaySetProperty = typeData.Properties["DefaultDisplayPropertySet"];
        if (displaySetProperty?.Value == null)
        {
            return null;
        }

        var displaySet = PSObject.AsPSObject(displaySetProperty.Value);
        var referencedProps = displaySet.Properties["ReferencedProperties"];
        if (referencedProps?.Value == null)
        {
            return null;
        }

        var propertyNames = new List<string>();

        if (referencedProps.Value is IEnumerable<string> stringList)
        {
            propertyNames.AddRange(stringList);
        }
        else if (referencedProps.Value is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                var name = item?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    propertyNames.Add(name);
                }
            }
        }

        return propertyNames.Count > 0 ? propertyNames.AsReadOnly() : null;
    }
}
