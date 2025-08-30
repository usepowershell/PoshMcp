using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Represents different types of authentication requirements for commands
/// </summary>
public enum AuthenticationType
{
    /// <summary>
    /// No authentication required
    /// </summary>
    None,

    /// <summary>
    /// Requires a specific Entra ID role
    /// </summary>
    Role,

    /// <summary>
    /// Requires a specific permission/scope
    /// </summary>
    Permission
}

/// <summary>
/// Configuration for command authentication requirements
/// </summary>
public class CommandAuthenticationGroup
{
    /// <summary>
    /// The type of authentication required
    /// </summary>
    [JsonPropertyName("type")]
    public AuthenticationType Type { get; set; } = AuthenticationType.None;

    /// <summary>
    /// Required Entra ID role (when Type is Role)
    /// </summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    /// <summary>
    /// Required permission/scope (when Type is Permission)
    /// </summary>
    [JsonPropertyName("permission")]
    public string? Permission { get; set; }

    /// <summary>
    /// List of commands that require this authentication
    /// </summary>
    [JsonPropertyName("commands")]
    public List<string> Commands { get; set; } = new();
}

/// <summary>
/// Enhanced configuration for PowerShell commands with authentication support
/// </summary>
public class AuthenticationAwareConfiguration
{
    /// <summary>
    /// Commands grouped by authentication requirements
    /// </summary>
    [JsonPropertyName("commandGroups")]
    public List<CommandAuthenticationGroup> CommandGroups { get; set; } = new();

    /// <summary>
    /// Gets all command names from all groups
    /// </summary>
    public List<string> GetAllCommands()
    {
        var allCommands = new List<string>();
        foreach (var group in CommandGroups)
        {
            allCommands.AddRange(group.Commands);
        }
        return allCommands;
    }

    /// <summary>
    /// Gets the authentication requirements for a specific command
    /// </summary>
    /// <param name="commandName">The command name to look up</param>
    /// <returns>The authentication group containing the command, or null if not found</returns>
    public CommandAuthenticationGroup? GetAuthenticationRequirements(string commandName)
    {
        foreach (var group in CommandGroups)
        {
            foreach (var command in group.Commands)
            {
                if (string.Equals(command, commandName, StringComparison.OrdinalIgnoreCase))
                {
                    return group;
                }
            }
        }
        return null;
    }
}

/// <summary>
/// Flexible parser for command configurations that supports the user-friendly YAML-like format
/// </summary>
public static class CommandConfigurationParser
{
    /// <summary>
    /// Parses a flexible command configuration from JSON that supports mixed formats
    /// </summary>
    /// <param name="commandsElement">The JsonElement representing the commands configuration</param>
    /// <returns>Parsed authentication-aware configuration</returns>
    public static AuthenticationAwareConfiguration ParseCommandsConfiguration(JsonElement commandsElement)
    {
        var config = new AuthenticationAwareConfiguration();

        if (commandsElement.ValueKind == JsonValueKind.Array)
        {
            // Handle array format similar to the YAML structure
            config.CommandGroups = ParseCommandsArray(commandsElement);
        }
        else if (commandsElement.ValueKind == JsonValueKind.Object)
        {
            // Handle object format with commandGroups
            if (commandsElement.TryGetProperty("commandGroups", out var commandGroupsElement))
            {
                config.CommandGroups = ParseCommandsArray(commandGroupsElement);
            }
        }

        return config;
    }

    private static List<CommandAuthenticationGroup> ParseCommandsArray(JsonElement arrayElement)
    {
        var groups = new List<CommandAuthenticationGroup>();

        foreach (var item in arrayElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                // Simple string command - no authentication required
                groups.Add(new CommandAuthenticationGroup
                {
                    Type = AuthenticationType.None,
                    Commands = new List<string> { item.GetString()! }
                });
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                // Object with authentication requirements
                var group = ParseAuthenticationGroup(item);
                if (group != null)
                {
                    groups.Add(group);
                }
            }
        }

        return groups;
    }

    private static CommandAuthenticationGroup? ParseAuthenticationGroup(JsonElement groupElement)
    {
        var group = new CommandAuthenticationGroup();

        // Check for role-based authentication
        if (groupElement.TryGetProperty("role", out var roleElement))
        {
            group.Type = AuthenticationType.Role;
            group.Role = roleElement.GetString();
        }
        // Check for permission-based authentication
        else if (groupElement.TryGetProperty("permission", out var permissionElement))
        {
            group.Type = AuthenticationType.Permission;
            group.Permission = permissionElement.GetString();
        }
        // Check for explicit type
        else if (groupElement.TryGetProperty("type", out var typeElement))
        {
            if (typeElement.ValueKind == JsonValueKind.Number)
            {
                group.Type = (AuthenticationType)typeElement.GetInt32();
            }
        }

        // Get the commands for this group
        if (groupElement.TryGetProperty("commands", out var commandsElement))
        {
            if (commandsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var commandElement in commandsElement.EnumerateArray())
                {
                    if (commandElement.ValueKind == JsonValueKind.String)
                    {
                        group.Commands.Add(commandElement.GetString()!);
                    }
                }
            }
        }
        else
        {
            // Handle the case where commands are direct properties of the group object
            // This supports the format: { "role": "Admin", "commands": ["cmd1"] }
            // But also: { "role": "Admin", "Update-User": true } style
            foreach (var property in groupElement.EnumerateObject())
            {
                if (property.Name != "role" && property.Name != "permission" && property.Name != "type")
                {
                    // Treat other properties as command names
                    group.Commands.Add(property.Name);
                }
            }
        }

        return group.Commands.Count > 0 ? group : null;
    }
}