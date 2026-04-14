using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.Authentication;

namespace PoshMcp.Server.PowerShell;

public class ConfigurationGuidanceTools
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _configurationPath;
    private readonly string _effectiveTransport;
    private readonly string? _effectiveRuntimeMode;
    private readonly string? _effectiveMcpPath;
    private readonly ILogger<ConfigurationGuidanceTools> _logger;

    public ConfigurationGuidanceTools(
        string configurationPath,
        string effectiveTransport,
        string? effectiveRuntimeMode,
        string? effectiveMcpPath,
        ILogger<ConfigurationGuidanceTools> logger)
    {
        _configurationPath = configurationPath ?? throw new ArgumentNullException(nameof(configurationPath));
        _effectiveTransport = effectiveTransport ?? throw new ArgumentNullException(nameof(effectiveTransport));
        _effectiveRuntimeMode = effectiveRuntimeMode;
        _effectiveMcpPath = effectiveMcpPath;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<string> GetConfigurationGuidance(CancellationToken cancellationToken = default)
    {
        // Check cancellation early
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<string>(cancellationToken);
        }

        try
        {
            _logger.LogInformation("Processing configuration guidance request");

            var rootConfiguration = new ConfigurationBuilder()
                .AddJsonFile(_configurationPath, optional: false, reloadOnChange: false)
                .Build();

            var powerShellConfiguration = new PowerShellConfiguration();
            rootConfiguration.GetSection("PowerShellConfiguration").Bind(powerShellConfiguration);

            var authenticationConfiguration = new AuthenticationConfiguration();
            rootConfiguration.GetSection("Authentication").Bind(authenticationConfiguration);

            var sections = BuildSections(powerShellConfiguration, authenticationConfiguration);
            var sources = sections
                .SelectMany(section => section.References)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // Build runtime context. Note: configurationPath and mcpPath may expose sensitive information
            // (file paths, deployment structure). Only share this tool with trusted clients or gate it
            // behind authentication. In HTTP mode with untrusted networks, consider redacting these fields.
            var runtimeContext = new
            {
                configurationPath = _configurationPath,
                transport = _effectiveTransport,
                runtimeMode = _effectiveRuntimeMode,
                mcpPath = _effectiveMcpPath,
                commandCount = powerShellConfiguration.GetEffectiveCommandNames().Count,
                moduleCount = powerShellConfiguration.Modules.Count,
                authenticationEnabled = authenticationConfiguration.Enabled,
                dynamicReloadToolsEnabled = powerShellConfiguration.EnableDynamicReloadTools,
                configurationTroubleshootingToolEnabled = powerShellConfiguration.EnableConfigurationTroubleshootingTool,
                environmentCustomizationConfigured = IsEnvironmentCustomizationConfigured(powerShellConfiguration)
            };

            return Task.FromResult(JsonSerializer.Serialize(new
            {
                success = true,
                runtimeContext,
                runtimeHints = BuildRuntimeHints(powerShellConfiguration, authenticationConfiguration),
                sections,
                suggestedNextSteps = BuildSuggestedNextSteps(powerShellConfiguration, authenticationConfiguration),
                sources
            }, SerializerOptions));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating configuration guidance output");
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Unexpected error: {ex.Message}"
            }, SerializerOptions));
        }
    }

    private string[] BuildRuntimeHints(PowerShellConfiguration powerShellConfiguration, AuthenticationConfiguration authenticationConfiguration)
    {
        var hints = new List<string>();

        if (string.Equals(_effectiveTransport, "http", StringComparison.OrdinalIgnoreCase))
        {
            hints.Add(authenticationConfiguration.Enabled
                ? "HTTP transport is active and authentication is enabled. Keep scopes, issuer settings, protected resource metadata, and CORS aligned with the deployment URL."
                : "HTTP transport is active and authentication is disabled. Enable the Authentication section before exposing the server beyond a trusted local boundary.");
        }
        else
        {
            hints.Add("The server is currently running over stdio. Authentication settings are usually unnecessary for a local single-client connection, but they should be planned before switching to HTTP transport.");
        }

        if (string.Equals(_effectiveRuntimeMode, "OutOfProcess", StringComparison.OrdinalIgnoreCase))
        {
            hints.Add("PowerShell is running out of process, so module installation, module paths, and startup scripts are applied to the persistent subprocess host.");
        }
        else
        {
            hints.Add("PowerShell is running in process, so startup scripts and imported modules affect the shared server runspace directly.");
        }

        if (IsEnvironmentCustomizationConfigured(powerShellConfiguration))
        {
            hints.Add("Environment customization is already configured. Prefer additive updates so startup scripts, module paths, and module install or import settings stay consistent with the current runspace setup.");
        }
        else
        {
            hints.Add("Environment customization is not configured yet. Start with ImportModules for pre-installed modules, then add InstallModules or StartupScriptPath only where needed.");
        }

        if (powerShellConfiguration.EnableDynamicReloadTools)
        {
            hints.Add("Dynamic reload tools are enabled, so configuration changes can be reloaded without restarting the process after the underlying file is updated.");
        }
        else
        {
            hints.Add("Dynamic reload tools are disabled. Configuration file changes typically require a restart, or the feature must be enabled before using live reload helpers.");
        }

        return hints.ToArray();
    }

    private GuidanceSection[] BuildSections(PowerShellConfiguration powerShellConfiguration, AuthenticationConfiguration authenticationConfiguration)
    {
        return new[]
        {
            BuildCreateSection(),
            BuildUpdateSection(powerShellConfiguration),
            BuildEnvironmentSection(),
            BuildAuthenticationSection(authenticationConfiguration)
        };
    }

    private static GuidanceSection BuildCreateSection()
    {
        return new GuidanceSection(
            "create",
            "Create or bootstrap configuration",
            "Start from the generated appsettings.json and then narrow the exposed commands. The docs favor CommandNames over the legacy FunctionNames field and keep most customization under PowerShellConfiguration.",
            new[]
            {
                "Use poshmcp create-config to generate the initial file instead of hand-authoring the full schema.",
                "Prefer PowerShellConfiguration.CommandNames when choosing commands to expose.",
                "Keep include and exclude patterns focused so tool discovery stays predictable."
            },
            new[]
            {
                "PowerShellConfiguration.CommandNames",
                "PowerShellConfiguration.FunctionNames",
                "PowerShellConfiguration.Modules",
                "PowerShellConfiguration.IncludePatterns",
                "PowerShellConfiguration.ExcludePatterns"
            },
            new[]
            {
                "poshmcp create-config",
                "poshmcp update-config --add-command Get-Process"
            },
            "{\n  \"PowerShellConfiguration\": {\n    \"CommandNames\": [\"Get-Process\"],\n    \"Modules\": [],\n    \"IncludePatterns\": [],\n    \"ExcludePatterns\": []\n  }\n}",
            new[]
            {
                "./docs/user-guide.md"
            });
    }

    private static GuidanceSection BuildUpdateSection(PowerShellConfiguration powerShellConfiguration)
    {
        var recommendations = new List<string>
        {
            "Use poshmcp update-config for command, module, and feature-flag changes before editing JSON by hand.",
            "Treat the configuration file as the source of truth even when dynamic reload tools are enabled.",
            "Regenerate or reload tools after changing the commands that should be exposed."
        };

        if (powerShellConfiguration.EnableDynamicReloadTools)
        {
            recommendations.Add("Because dynamic reload tools are enabled, you can update the file and then call reload-configuration-from-file to refresh the active toolset.");
        }
        else
        {
            recommendations.Add("If you need live updates, enable PowerShellConfiguration.EnableDynamicReloadTools before relying on runtime reload behavior.");
        }

        return new GuidanceSection(
            "update",
            "Update an existing configuration",
            "Use the CLI for common edits and reserve manual JSON changes for advanced environment or authentication sections. This keeps the config shape aligned with the server's current conventions.",
            recommendations.ToArray(),
            new[]
            {
                "PowerShellConfiguration.EnableDynamicReloadTools",
                "PowerShellConfiguration.EnableConfigurationTroubleshootingTool",
                "PowerShellConfiguration.Performance.EnableResultCaching",
                "Authentication.Enabled"
            },
            new[]
            {
                "poshmcp update-config --add-command Get-Service",
                "poshmcp update-config --set-auth-enabled true",
                "poshmcp update-config --enable-dynamic-reload-tools true"
            },
            null,
            new[]
            {
                "./docs/user-guide.md"
            });
    }

    private GuidanceSection BuildEnvironmentSection()
    {
        var recommendations = new List<string>
        {
            "Use ImportModules for modules that are already present in the image or host environment because it avoids install-time delays.",
            "Use InstallModules only when runtime installation is required and keep install timeouts conservative for container readiness.",
            "Prefer StartupScriptPath for substantial initialization logic and StartupScript only for short inline setup.",
            "Add custom module directories through ModulePaths when modules come from mounted volumes or shared locations."
        };

        if (string.Equals(_effectiveRuntimeMode, "OutOfProcess", StringComparison.OrdinalIgnoreCase))
        {
            recommendations.Add("Out-of-process mode is a good fit when module isolation matters or startup scripts need to avoid polluting the host process.");
        }

        return new GuidanceSection(
            "environment",
            "Customize the PowerShell environment",
            "Environment customization lives under PowerShellConfiguration.Environment. The docs split this into module installation, importing pre-installed modules, module path extension, and startup scripts.",
            recommendations.ToArray(),
            new[]
            {
                "PowerShellConfiguration.Environment.InstallModules",
                "PowerShellConfiguration.Environment.ImportModules",
                "PowerShellConfiguration.Environment.ModulePaths",
                "PowerShellConfiguration.Environment.StartupScript",
                "PowerShellConfiguration.Environment.StartupScriptPath",
                "PowerShellConfiguration.Environment.TrustPSGallery",
                "PowerShellConfiguration.Environment.InstallTimeoutSeconds"
            },
            new[]
            {
                "poshmcp update-config --add-module Az.Accounts"
            },
            "{\n  \"PowerShellConfiguration\": {\n    \"Environment\": {\n      \"ImportModules\": [\"Az.Accounts\"],\n      \"ModulePaths\": [\"./custom-modules\"],\n      \"StartupScriptPath\": \"/app/startup.ps1\",\n      \"TrustPSGallery\": true\n    }\n  }\n}",
            new[]
            {
                "./docs/ENVIRONMENT-CUSTOMIZATION.md",
                "./docs/ENVIRONMENT-CUSTOMIZATION-SUMMARY.md"
            });
    }

    private GuidanceSection BuildAuthenticationSection(AuthenticationConfiguration authenticationConfiguration)
    {
        string summary;
        var recommendations = new List<string>();

        if (string.Equals(_effectiveTransport, "http", StringComparison.OrdinalIgnoreCase))
        {
            summary = authenticationConfiguration.Enabled
                ? "HTTP transport is active and authentication is enabled. Keep bearer configuration, required scopes, protected resource metadata, and any CORS settings aligned with the deployed endpoint."
                : "HTTP transport is active and authentication is disabled. For shared or remote deployments, enable Authentication and define bearer metadata before exposing the server outside a trusted local boundary.";

            recommendations.Add("Set Authentication.Enabled to true for authenticated HTTP access.");
            recommendations.Add("Configure Authentication.DefaultPolicy.RequiredScopes and RequiredRoles to match the audience you expect clients to present.");
            recommendations.Add("For Entra ID, set Schemes.Bearer.Authority, Audience, ValidIssuers, and ProtectedResource metadata together.");
            recommendations.Add("Configure Cors.AllowedOrigins only for browser-based callers that actually need cross-origin access.");
        }
        else
        {
            summary = "The server is currently running over stdio. Authentication settings mainly matter when planning an HTTP deployment, especially if you intend to use Entra ID, OAuth metadata discovery, or browser-based clients.";

            recommendations.Add("Keep Authentication disabled for purely local stdio usage unless you are preparing the same config for HTTP deployment.");
            recommendations.Add("Before switching to HTTP, define the Authentication and ProtectedResource sections together so clients can discover scopes and authorization servers consistently.");
            recommendations.Add("Use the Entra ID guide when the deployment needs OAuth scopes, managed identity, or enterprise sign-in.");
        }

        return new GuidanceSection(
            "authentication",
            "Secure the server",
            summary,
            recommendations.ToArray(),
            new[]
            {
                "Authentication.Enabled",
                "Authentication.DefaultScheme",
                "Authentication.DefaultPolicy.RequiredScopes",
                "Authentication.DefaultPolicy.RequiredRoles",
                "Authentication.Schemes.Bearer.Authority",
                "Authentication.Schemes.Bearer.Audience",
                "Authentication.Schemes.Bearer.ValidIssuers",
                "Authentication.ProtectedResource.Resource",
                "Authentication.ProtectedResource.AuthorizationServers",
                "Authentication.ProtectedResource.ScopesSupported",
                "Authentication.Cors.AllowedOrigins"
            },
            new[]
            {
                "poshmcp update-config --set-auth-enabled true"
            },
            "{\n  \"Authentication\": {\n    \"Enabled\": true,\n    \"DefaultScheme\": \"Bearer\",\n    \"DefaultPolicy\": {\n      \"RequireAuthentication\": true,\n      \"RequiredScopes\": [\"api://poshmcp-prod/access_as_server\"]\n    },\n    \"Schemes\": {\n      \"Bearer\": {\n        \"Type\": \"JwtBearer\",\n        \"Authority\": \"https://login.microsoftonline.com/<tenant-id>\",\n        \"Audience\": \"api://poshmcp-prod\"\n      }\n    }\n  }\n}",
            new[]
            {
                "./docs/entra-id-auth-guide.md",
                "./docs/user-guide.md"
            });
    }

    private string[] BuildSuggestedNextSteps(PowerShellConfiguration powerShellConfiguration, AuthenticationConfiguration authenticationConfiguration)
    {
        var nextSteps = new List<string>
        {
            "Use the CLI create-config and update-config commands for common edits, then keep advanced Environment and Authentication sections in version-controlled JSON."
        };

        if (string.Equals(_effectiveTransport, "http", StringComparison.OrdinalIgnoreCase) && !authenticationConfiguration.Enabled)
        {
            nextSteps.Add("Prioritize the Authentication section before treating the current HTTP deployment as multi-user or internet reachable.");
        }

        if (!IsEnvironmentCustomizationConfigured(powerShellConfiguration))
        {
            nextSteps.Add("If you need modules or session bootstrap logic, add a minimal Environment section before growing the command surface.");
        }

        if (!powerShellConfiguration.EnableDynamicReloadTools)
        {
            nextSteps.Add("Enable dynamic reload tools if operators need to refresh tool discovery without restarting the process.");
        }

        return nextSteps.ToArray();
    }

    private static bool IsEnvironmentCustomizationConfigured(PowerShellConfiguration powerShellConfiguration)
    {
        var environment = powerShellConfiguration.Environment;

        return environment.InstallModules.Count > 0
            || environment.ImportModules.Count > 0
            || environment.ModulePaths.Count > 0
            || !string.IsNullOrWhiteSpace(environment.StartupScript)
            || !string.IsNullOrWhiteSpace(environment.StartupScriptPath);
    }

    public sealed record GuidanceSection(
        string Topic,
        string Title,
        string Summary,
        string[] Recommendations,
        string[] ConfigurationPaths,
        string[] SuggestedCommands,
        string? ExampleJson,
        string[] References);
}