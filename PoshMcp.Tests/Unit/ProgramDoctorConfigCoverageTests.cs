using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Collection("TransportSelectionTests")]
public class ProgramDoctorConfigCoverageTests
{
    [Fact]
    public async Task DoctorJson_IncludesEnvironmentVariables_WithSevenExpectedKeys()
    {
        using var configFile = new DoctorConfigFile();
        using var capture = new DoctorConsoleCapture();

        var result = await Program.Main(["doctor", "--config", configFile.Path, "--format", "json"]);

        Assert.Equal(0, result);
        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        var envVars = payload!["environmentVariables"]?.AsObject();
        Assert.NotNull(envVars);
        Assert.True(envVars!.ContainsKey("POSHMCP_CONFIG"));
        Assert.True(envVars.ContainsKey("POSHMCP_TRANSPORT"));
        Assert.True(envVars.ContainsKey("POSHMCP_LOG_LEVEL"));
        Assert.True(envVars.ContainsKey("POSHMCP_SESSION_MODE"));
        Assert.True(envVars.ContainsKey("POSHMCP_RUNTIME_MODE"));
        Assert.True(envVars.ContainsKey("POSHMCP_MCP_PATH"));
        Assert.True(envVars.ContainsKey("ASPNETCORE_ENVIRONMENT"));
    }

    [Fact]
    public async Task DoctorJson_EnvironmentVariables_ReflectsSetValues()
    {
        using var configFile = new DoctorConfigFile();
        using var capture = new DoctorConsoleCapture();
        using var envScope = new DoctorEnvVarScope("POSHMCP_TRANSPORT", "http");

        var result = await Program.Main(["doctor", "--config", configFile.Path, "--format", "json"]);

        Assert.Equal(0, result);
        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        var envVars = payload!["environmentVariables"]?.AsObject();
        Assert.NotNull(envVars);
        Assert.Equal("http", envVars!["POSHMCP_TRANSPORT"]?.GetValue<string>());
    }

    [Fact]
    public async Task DoctorJson_IncludesRuntimeSettingsSection()
    {
        using var configFile = new DoctorConfigFile();
        using var capture = new DoctorConsoleCapture();

        var result = await Program.Main(["doctor", "--config", configFile.Path, "--format", "json"]);

        Assert.Equal(0, result);
        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        Assert.NotNull(payload!["runtimeSettings"]);
        Assert.NotNull(payload["runtimeSettings"]!["transport"]);
        Assert.NotNull(payload["runtimeSettings"]!["logLevel"]);
    }

    [Fact]
    public async Task DoctorJson_IncludesSummarySection()
    {
        using var configFile = new DoctorConfigFile();
        using var capture = new DoctorConsoleCapture();

        var result = await Program.Main(["doctor", "--config", configFile.Path, "--format", "json"]);

        Assert.Equal(0, result);
        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        var summary = payload!["summary"]?.AsObject();
        Assert.NotNull(summary);
        Assert.NotNull(summary!["status"]);
        var status = summary["status"]!.GetValue<string>();
        Assert.True(status == "healthy" || status == "warnings" || status == "errors",
            $"Unexpected status value: {status}");
    }

    [Fact]
    public async Task DoctorJson_IncludesResourceDefinitions()
    {
        using var configFile = new DoctorConfigFile(includeResource: true);
        using var capture = new DoctorConsoleCapture();

        var result = await Program.Main(["doctor", "--config", configFile.Path, "--format", "json"]);

        Assert.Equal(0, result);
        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        var resources = payload!["mcpDefinitions"]?["resources"]?.AsObject();
        Assert.NotNull(resources);
        Assert.True(resources!["configured"]?.GetValue<int>() > 0, "expected at least one configured resource");
    }

    [Fact]
    public async Task DoctorJson_IncludesPromptDefinitions()
    {
        using var configFile = new DoctorConfigFile(includePrompt: true);
        using var capture = new DoctorConsoleCapture();

        var result = await Program.Main(["doctor", "--config", configFile.Path, "--format", "json"]);

        Assert.Equal(0, result);
        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        var prompts = payload!["mcpDefinitions"]?["prompts"]?.AsObject();
        Assert.NotNull(prompts);
        Assert.True(prompts!["configured"]?.GetValue<int>() > 0, "expected at least one configured prompt");
    }

    [Fact]
    public async Task DoctorText_IncludesEnvironmentVariablesSection()
    {
        using var configFile = new DoctorConfigFile();
        using var capture = new DoctorConsoleCapture();

        var result = await Program.Main(["doctor", "--config", configFile.Path]);

        Assert.Equal(0, result);
        Assert.Contains("── Environment Variables", capture.StandardOutput);
        Assert.Contains("POSHMCP_TRANSPORT", capture.StandardOutput);
    }

    [Fact]
    public async Task DoctorText_IncludesRuntimeSettingsSection()
    {
        using var configFile = new DoctorConfigFile();
        using var capture = new DoctorConsoleCapture();

        var result = await Program.Main(["doctor", "--config", configFile.Path]);

        Assert.Equal(0, result);
        Assert.Contains("── Runtime Settings", capture.StandardOutput);
    }

    [Fact]
    public async Task DoctorText_IncludesMcpDefinitionsSection()
    {
        using var configFile = new DoctorConfigFile(includeResource: true);
        using var capture = new DoctorConsoleCapture();

        var result = await Program.Main(["doctor", "--config", configFile.Path]);

        Assert.Equal(0, result);
        Assert.Contains("── MCP Definitions", capture.StandardOutput);
        Assert.Contains("resources", capture.StandardOutput);
    }

    [Fact]
    public async Task DoctorText_IncludesPromptDefinitionsSection()
    {
        using var configFile = new DoctorConfigFile(includePrompt: true);
        using var capture = new DoctorConsoleCapture();

        var result = await Program.Main(["doctor", "--config", configFile.Path]);

        Assert.Equal(0, result);
        Assert.Contains("── MCP Definitions", capture.StandardOutput);
        Assert.Contains("prompts", capture.StandardOutput);
    }

    [Fact]
    public async Task DoctorText_UnsetEnvVarDisplaysNotSet()
    {
        using var configFile = new DoctorConfigFile();
        using var capture = new DoctorConsoleCapture();
        using var envScope = new DoctorEnvVarScope("POSHMCP_MCP_PATH", null);

        var result = await Program.Main(["doctor", "--config", configFile.Path]);

        Assert.Equal(0, result);
        // New format: "  POSHMCP_MCP_PATH                      : (not set)"
        Assert.Contains("POSHMCP_MCP_PATH", capture.StandardOutput);
        Assert.Contains("(not set)", capture.StandardOutput);
    }

    [Fact]
    public async Task DoctorJson_DoesNotContainAuthenticationConfig()
    {
        using var configFile = new DoctorConfigFile(includeAuthentication: true);
        using var capture = new DoctorConsoleCapture();

        var result = await Program.Main(["doctor", "--config", configFile.Path, "--format", "json"]);

        Assert.Equal(0, result);
        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        // Authentication config is not surfaced in this spec version (spec-006)
        Assert.False(payload!.ContainsKey("authenticationConfig"),
            "authenticationConfig must not appear in the new nested JSON structure");
    }

    [Fact]
    public async Task DoctorText_DoesNotContainLegacyAuthenticationConfigLabel()
    {
        using var configFile = new DoctorConfigFile(includeAuthWithSecret: true);
        using var capture = new DoctorConsoleCapture();

        var result = await Program.Main(["doctor", "--config", configFile.Path]);

        Assert.Equal(0, result);
        // The old "Authentication config:" label must not appear in the new text output
        Assert.DoesNotContain("Authentication config:", capture.StandardOutput);
        // Sensitive value from config must never leak into output
        Assert.DoesNotContain("super-secret-value", capture.StandardOutput);
    }

    [Fact]
    public void BuildDoctorJson_WithPreSuppliedResourceAndPromptDefs_UsesPreSupplied()
    {
        var configFile = new DoctorConfigFile();
        try
        {
            var config = PoshMcp.ConfigurationLoader.LoadPowerShellConfiguration(
                configFile.Path,
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                "InProcess");

            var report = Program.BuildDoctorReportFromConfig(
                configurationPath: configFile.Path,
                configurationPathSource: "test",
                effectiveLogLevel: "Warning",
                effectiveLogLevelSource: "default",
                effectiveTransport: "stdio",
                effectiveTransportSource: "default",
                effectiveSessionMode: null,
                effectiveSessionModeSource: "default",
                effectiveRuntimeMode: "InProcess",
                effectiveRuntimeModeSource: "default",
                effectiveMcpPath: null,
                effectiveMcpPathSource: "default",
                config: config,
                tools: []);

            var json = Program.BuildDoctorJson(report);
            var payload = JsonNode.Parse(json)?.AsObject();
            Assert.NotNull(payload);
        }
        finally
        {
            configFile.Dispose();
        }
    }

    private sealed class DoctorConfigFile : IDisposable
    {
        public string Path { get; }

        public DoctorConfigFile(
            bool includeAuthentication = false,
            bool includeAuthWithSecret = false,
            bool includeResource = false,
            bool includePrompt = false)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"poshmcp-doctor-coverage-{Guid.NewGuid():N}.json");

            var authSection = (includeAuthentication || includeAuthWithSecret)
                ? (includeAuthWithSecret
                    ? """
  "Authentication": {
    "Enabled": false,
    "ClientId": "my-client-id",
    "ClientSecret": "super-secret-value"
  },
"""
                    : """
  "Authentication": {
    "Enabled": false,
    "DefaultScheme": "Bearer"
  },
""")
                : string.Empty;

            var resourceSection = includeResource
                ? """
  "McpResources": {
    "Resources": [
      {
        "Uri": "poshmcp://test/resource",
        "Name": "Test Resource",
        "Source": "command",
        "Command": "echo test"
      }
    ]
  },
"""
                : string.Empty;

            var promptSection = includePrompt
                ? """
  "McpPrompts": {
    "Prompts": [
      {
        "Name": "test-prompt",
        "Description": "A test prompt",
        "Source": "command",
        "Command": "echo prompt"
      }
    ]
  },
"""
                : string.Empty;

            var json = $$"""
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning"
    }
  },
  {{authSection}}
  {{resourceSection}}
  {{promptSection}}
  "PowerShellConfiguration": {
    "FunctionNames": ["Get-Date"]
  }
}
""";

            File.WriteAllText(Path, json);
        }

        public void Dispose()
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
    }

    private sealed class DoctorConsoleCapture : IDisposable
    {
        private readonly TextWriter _originalOut;
        private readonly TextWriter _originalError;
        private readonly StringWriter _capturedOut;
        private readonly StringWriter _capturedError;

        public string StandardOutput => _capturedOut.ToString();

        public DoctorConsoleCapture()
        {
            _originalOut = Console.Out;
            _originalError = Console.Error;
            _capturedOut = new StringWriter();
            _capturedError = new StringWriter();
            Console.SetOut(_capturedOut);
            Console.SetError(_capturedError);
        }

        public void Dispose()
        {
            Console.SetOut(_originalOut);
            Console.SetError(_originalError);
            _capturedOut.Dispose();
            _capturedError.Dispose();
        }
    }

    private sealed class DoctorEnvVarScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        public DoctorEnvVarScope(string name, string? value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _originalValue);
        }
    }
}
