using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

public class ConfigurationGuidanceIntegrationTests : PowerShellTestBase
{
    public ConfigurationGuidanceIntegrationTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task StdioServer_WithConfigurationGuidanceEnabled_ShouldListAndCallTool()
    {
        using var configFile = new TemporaryConfigFile();
        using var server = new InProcessMcpServer(Logger, explicitConfigPath: configFile.Path);
        await server.StartAsync();

        using var client = new ExternalMcpClient(Logger, server);
        await client.StartAsync();

        var toolsResponse = await client.SendListToolsAsync();
        var tools = toolsResponse["result"]?["tools"] as JArray;

        Assert.NotNull(tools);
        Assert.Contains(tools!, tool => string.Equals(tool?["name"]?.ToString(), "get-configuration-guidance", StringComparison.OrdinalIgnoreCase));

        var callResponse = await client.SendToolCallAsync("get-configuration-guidance", new { });
        var content = callResponse["result"]?["content"] as JArray;

        Assert.NotNull(content);
        Assert.NotEmpty(content!);

        var textContent = content![0]?["text"]?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(textContent));
        Assert.Contains("PowerShellConfiguration.Environment.InstallModules", textContent, StringComparison.Ordinal);
        Assert.Contains("Authentication.Enabled", textContent, StringComparison.Ordinal);
    }

    private sealed class TemporaryConfigFile : IDisposable
    {
        public string Path { get; }

        public TemporaryConfigFile()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"poshmcp-guidance-integration-{Guid.NewGuid():N}.json");

            var json = """
{
  "PowerShellConfiguration": {
    "CommandNames": ["Get-Date"],
    "Modules": [],
    "IncludePatterns": [],
    "ExcludePatterns": [],
    "EnableConfigurationTroubleshootingTool": true
  },
  "Authentication": {
    "Enabled": false,
    "DefaultScheme": "Bearer",
    "DefaultPolicy": {
      "RequireAuthentication": true,
      "RequiredScopes": [],
      "RequiredRoles": []
    },
    "Schemes": {}
  }
}
""";

            File.WriteAllText(Path, json);
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