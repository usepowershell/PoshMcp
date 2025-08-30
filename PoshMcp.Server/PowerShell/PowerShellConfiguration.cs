using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// JsonConverter for AuthenticationAwareConfiguration to support flexible formats
/// </summary>
public class AuthenticationAwareConfigurationConverter : JsonConverter<AuthenticationAwareConfiguration>
{
    public override AuthenticationAwareConfiguration Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return CommandConfigurationParser.ParseCommandsConfiguration(doc.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, AuthenticationAwareConfiguration value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}

/// <summary>
/// Configuration options for PowerShell command importing
/// </summary>
public class PowerShellConfiguration
{
    /// <summary>
    /// Specific function names to import (legacy format for backward compatibility)
    /// </summary>
    public List<string> FunctionNames { get; set; } = new();

    /// <summary>
    /// Authentication-aware command configuration
    /// </summary>
    [JsonPropertyName("commands")]
    [JsonConverter(typeof(AuthenticationAwareConfigurationConverter))]
    public AuthenticationAwareConfiguration? Commands { get; set; }

    /// <summary>
    /// Modules to import all commands from
    /// </summary>
    public List<string> Modules { get; set; } = new();

    /// <summary>
    /// Patterns to exclude from import (supports wildcards)
    /// </summary>
    public List<string> ExcludePatterns { get; set; } = new();

    /// <summary>
    /// Patterns to include in import (supports wildcards)
    /// </summary>
    public List<string> IncludePatterns { get; set; } = new();

    /// <summary>
    /// Whether to enable dynamic reload tools (reload-configuration-from-file, update-configuration, get-configuration-status)
    /// </summary>
    public bool EnableDynamicReloadTools { get; set; } = false;

    /// <summary>
    /// Gets all function names from both legacy and new configuration formats
    /// </summary>
    public List<string> GetAllFunctionNames()
    {
        var allNames = new List<string>(FunctionNames);

        if (Commands != null)
        {
            allNames.AddRange(Commands.GetAllCommands());
        }

        return allNames.Distinct().ToList();
    }

    /// <summary>
    /// Gets authentication requirements for a specific command
    /// </summary>
    /// <param name="commandName">The command name to look up</param>
    /// <returns>The authentication group for the command, or null if no specific requirements</returns>
    public CommandAuthenticationGroup? GetAuthenticationRequirements(string commandName)
    {
        // Commands from FunctionNames have no authentication requirements (legacy behavior)
        foreach (var functionName in FunctionNames)
        {
            if (string.Equals(functionName, commandName, System.StringComparison.OrdinalIgnoreCase))
            {
                return new CommandAuthenticationGroup
                {
                    Type = AuthenticationType.None,
                    Commands = new List<string> { commandName }
                };
            }
        }

        // Check authentication-aware configuration
        return Commands?.GetAuthenticationRequirements(commandName);
    }

    /// <summary>
    /// Manual configuration binding method to handle complex JSON structures
    /// </summary>
    /// <param name="configuration">Configuration section</param>
    public void BindFromConfiguration(IConfigurationSection configuration)
    {
        // Bind simple properties
        configuration.Bind(this);

        // Handle the commands section manually if present
        var commandsSection = configuration.GetSection("commands");
        if (commandsSection.Exists())
        {
            try
            {
                // Try to parse as JSON to handle flexible formats
                var commandsJson = commandsSection.Value;
                if (!string.IsNullOrEmpty(commandsJson))
                {
                    using var doc = JsonDocument.Parse(commandsJson);
                    Commands = CommandConfigurationParser.ParseCommandsConfiguration(doc.RootElement);
                }
            }
            catch
            {
                // If JSON parsing fails, try standard binding
                Commands = new AuthenticationAwareConfiguration();
                commandsSection.Bind(Commands);
            }
        }
    }
}
