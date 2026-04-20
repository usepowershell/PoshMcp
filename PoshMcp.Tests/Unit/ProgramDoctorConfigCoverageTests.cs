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
    public async Task DoctorJson_IncludesAuthenticationConfig()
    {
        using var configFile = new DoctorConfigFile(includeAuthentication: true);
        using var capture = new DoctorConsoleCapture();

        var result = await Program.Main(["doctor", "--config", configFile.Path, "--format", "json"]);

        Assert.Equal(0, result);
        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        Assert.NotNull(payload!["authenticationConfig"]);
    }

    [Fact]
    public async Task DoctorJson_IncludesLoggingConfig()
    {
        using var configFile = new DoctorConfigFile();
        using var capture = new DoctorConsoleCapture();

        var result = await Program.Main(["doctor", "--config", configFile.Path, "--format", "json"]);

        Assert.Equal(0, result);
        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        Assert.NotNull(payload!["loggingConfig"]);
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
        var defs = payload!["resourceDefinitions"]?.AsArray();
        Assert.NotNull(defs);
        Assert.True(defs!.Count > 0);
        Assert.Equal("poshmcp://test/resource", defs[0]?["Uri"]?.GetValue<string>());
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
        var defs = payload!["promptDefinitions"]?.AsArray();
        Assert.NotNull(defs);
        Assert.True(defs!.Count > 0);
        Assert.Equal("test-prompt", defs[0]?["Name"]?.GetValue<string>());
    }

    [Fact]
    public async Task DoctorText_IncludesEnvironmentVariablesSection()
    {
        using var configFile = new DoctorConfigFile();
        using var capture = new DoctorConsoleCapture();

        var result = await Program.Main(["doctor", "--config", configFile.Path]);

        Assert.Equal(0, result);
        Assert.Contains("Environment variables:", capture.StandardOutput);
        Assert.Contains("POSHMCP_TRANSPORT=", capture.StandardOutput);
    }

    [Fact]
    public async Task DoctorText_IncludesAuthenticationConfigSection()
    {
        using var configFile = new DoctorConfigFile(includeAuthentication: true);
        using var capture = new DoctorConsoleCapture();

        var result = await Program.Main(["doctor", "--config", configFile.Path]);

        Assert.Equal(0, result);
        Assert.Contains("Authentication config:", capture.StandardOutput);
    }

    [Fact]
    public async Task DoctorText_IncludesLoggingConfigSection()
    {
        using var configFile = new DoctorConfigFile();
        using var capture = new DoctorConsoleCapture();

        var result = await Program.Main(["doctor", "--config", configFile.Path]);

        Assert.Equal(0, result);
        Assert.Contains("Logging config:", capture.StandardOutput);
    }

    [Fact]
    public async Task DoctorText_UnsetEnvVarDisplaysNotSet()
    {
        using var configFile = new DoctorConfigFile();
        using var capture = new DoctorConsoleCapture();
        using var envScope = new DoctorEnvVarScope("POSHMCP_MCP_PATH", null);

        var result = await Program.Main(["doctor", "--config", configFile.Path]);

        Assert.Equal(0, result);
        Assert.Contains("POSHMCP_MCP_PATH=(not set)", capture.StandardOutput);
    }

    [Fact]
    public async Task DoctorText_IncludesResourceDefinitionsSection()
    {
        using var configFile = new DoctorConfigFile(includeResource: true);
        using var capture = new DoctorConsoleCapture();

        var result = await Program.Main(["doctor", "--config", configFile.Path]);

        Assert.Equal(0, result);
        Assert.Contains("Resource definitions:", capture.StandardOutput);
        Assert.Contains("poshmcp://test/resource", capture.StandardOutput);
    }

    [Fact]
    public async Task DoctorText_IncludesPromptDefinitionsSection()
    {
        using var configFile = new DoctorConfigFile(includePrompt: true);
        using var capture = new DoctorConsoleCapture();

        var result = await Program.Main(["doctor", "--config", configFile.Path]);

        Assert.Equal(0, result);
        Assert.Contains("Prompt definitions:", capture.StandardOutput);
        Assert.Contains("test-prompt", capture.StandardOutput);
    }

    private sealed class DoctorConfigFile : IDisposable
    {
        public string Path { get; }

        public DoctorConfigFile(
            bool includeAuthentication = false,
            bool includeResource = false,
            bool includePrompt = false)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"poshmcp-doctor-coverage-{Guid.NewGuid():N}.json");

            var authSection = includeAuthentication
                ? """
  "Authentication": {
    "Enabled": false,
    "DefaultScheme": "Bearer"
  },
"""
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
