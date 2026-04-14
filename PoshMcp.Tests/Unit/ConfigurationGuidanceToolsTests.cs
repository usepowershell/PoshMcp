using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PoshMcp.Server.PowerShell;
using Xunit;

namespace PoshMcp.Tests.Unit;

public class ConfigurationGuidanceToolsTests
{
    [Fact]
    public async Task GetConfigurationGuidance_WithStdioRuntime_ExplainsHttpAuthenticationPlanning()
    {
        using var configFile = new TemporaryConfigFile(authenticationEnabled: false, includeEnvironmentCustomization: false);
        var tool = new ConfigurationGuidanceTools(
            configFile.Path,
            "stdio",
            "InProcess",
            null,
            NullLogger<ConfigurationGuidanceTools>.Instance);

        var payload = JsonNode.Parse(await tool.GetConfigurationGuidance())?.AsObject();

        Assert.NotNull(payload);
        Assert.True(payload!["success"]?.GetValue<bool>());
        Assert.Equal("stdio", payload["runtimeContext"]?["transport"]?.GetValue<string>());

        var runtimeHints = payload["runtimeHints"]?.AsArray().Select(node => node?.GetValue<string>()).OfType<string>().ToArray();
        Assert.NotNull(runtimeHints);
        Assert.Contains(runtimeHints!, hint => hint.Contains("switching to HTTP transport", StringComparison.OrdinalIgnoreCase));

        var authenticationSection = GetSection(payload, "authentication");
        Assert.NotNull(authenticationSection);
        Assert.Contains("stdio", authenticationSection!["summary"]?.GetValue<string>() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(authenticationSection["configurationPaths"]?.AsArray() ?? new JsonArray(), item =>
            string.Equals(item?.GetValue<string>(), "Authentication.Enabled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetConfigurationGuidance_WithHttpRuntime_ListsEnvironmentAndAuthenticationFields()
    {
        using var configFile = new TemporaryConfigFile(authenticationEnabled: true, includeEnvironmentCustomization: true);
        var tool = new ConfigurationGuidanceTools(
            configFile.Path,
            "http",
            "OutOfProcess",
            "/mcp",
            NullLogger<ConfigurationGuidanceTools>.Instance);

        var payload = JsonNode.Parse(await tool.GetConfigurationGuidance())?.AsObject();

        Assert.NotNull(payload);
        Assert.True(payload!["runtimeContext"]?["environmentCustomizationConfigured"]?.GetValue<bool>());
        Assert.Equal("http", payload["runtimeContext"]?["transport"]?.GetValue<string>());
        Assert.Equal("/mcp", payload["runtimeContext"]?["mcpPath"]?.GetValue<string>());

        var environmentSection = GetSection(payload, "environment");
        Assert.NotNull(environmentSection);
        Assert.Contains(environmentSection!["configurationPaths"]?.AsArray() ?? new JsonArray(), item =>
            string.Equals(item?.GetValue<string>(), "PowerShellConfiguration.Environment.InstallModules", StringComparison.OrdinalIgnoreCase));

        var authenticationSection = GetSection(payload, "authentication");
        Assert.NotNull(authenticationSection);
        Assert.Contains("HTTP transport is active", authenticationSection!["summary"]?.GetValue<string>() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(authenticationSection["configurationPaths"]?.AsArray() ?? new JsonArray(), item =>
            string.Equals(item?.GetValue<string>(), "Authentication.ProtectedResource.Resource", StringComparison.OrdinalIgnoreCase));
    }

    private static JsonObject? GetSection(JsonObject payload, string topic)
    {
        return payload["sections"]?.AsArray()
            .Select(node => node as JsonObject)
            .FirstOrDefault(section => string.Equals(section?["topic"]?.GetValue<string>(), topic, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TemporaryConfigFile : IDisposable
    {
        public string Path { get; }

        public TemporaryConfigFile(bool authenticationEnabled, bool includeEnvironmentCustomization)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"poshmcp-guidance-tests-{Guid.NewGuid():N}.json");

            var powerShellConfiguration = new JsonObject
            {
                ["CommandNames"] = new JsonArray("Get-Date"),
                ["Modules"] = new JsonArray(),
                ["IncludePatterns"] = new JsonArray(),
                ["ExcludePatterns"] = new JsonArray(),
                ["EnableConfigurationTroubleshootingTool"] = true
            };

            if (includeEnvironmentCustomization)
            {
                powerShellConfiguration["Environment"] = new JsonObject
                {
                    ["InstallModules"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["Name"] = "Az.Accounts"
                        }
                    },
                    ["ImportModules"] = new JsonArray("Az.Accounts"),
                    ["ModulePaths"] = new JsonArray("./modules"),
                    ["StartupScriptPath"] = "/app/startup.ps1"
                };
            }

            var authentication = authenticationEnabled
                ? new JsonObject
                {
                    ["Enabled"] = true,
                    ["DefaultScheme"] = "Bearer",
                    ["DefaultPolicy"] = new JsonObject
                    {
                        ["RequireAuthentication"] = true,
                        ["RequiredScopes"] = new JsonArray("api://poshmcp/access_as_server"),
                        ["RequiredRoles"] = new JsonArray()
                    },
                    ["Schemes"] = new JsonObject
                    {
                        ["Bearer"] = new JsonObject
                        {
                            ["Type"] = "JwtBearer",
                            ["Authority"] = "https://login.microsoftonline.com/test-tenant",
                            ["Audience"] = "api://poshmcp",
                            ["ValidIssuers"] = new JsonArray("https://login.microsoftonline.com/test-tenant/v2.0")
                        }
                    },
                    ["ProtectedResource"] = new JsonObject
                    {
                        ["Resource"] = "api://poshmcp",
                        ["AuthorizationServers"] = new JsonArray("https://login.microsoftonline.com/test-tenant"),
                        ["ScopesSupported"] = new JsonArray("api://poshmcp/access_as_server")
                    }
                }
                : new JsonObject
                {
                    ["Enabled"] = false,
                    ["DefaultScheme"] = "Bearer",
                    ["DefaultPolicy"] = new JsonObject
                    {
                        ["RequireAuthentication"] = true,
                        ["RequiredScopes"] = new JsonArray(),
                        ["RequiredRoles"] = new JsonArray()
                    },
                    ["Schemes"] = new JsonObject()
                };

            var json = new JsonObject
            {
                ["PowerShellConfiguration"] = powerShellConfiguration,
                ["Authentication"] = authentication,
                ["Logging"] = new JsonObject
                {
                    ["LogLevel"] = new JsonObject
                    {
                        ["Default"] = "Information"
                    }
                }
            };

            File.WriteAllText(Path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}