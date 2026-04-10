using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Collection("TransportSelectionTests")]
public class ProgramDoctorToolExposureTests
{
    private const string TroubleshootingToolEnvVar = "POSHMCP_ENABLE_CONFIGURATION_TROUBLESHOOTING_TOOL";

    [Fact]
    public async Task DoctorCommand_WithConfigurationTroubleshootingToolDisabled_OmitsTroubleshootingToolName()
    {
        using var configFile = new TemporaryConfigFile(enableConfigurationTroubleshootingTool: false);
        using var capture = new ConsoleCapture();
        using var envScope = new EnvironmentVariableScope(TroubleshootingToolEnvVar, null);

        var result = await Program.Main(new[] { "doctor", "--config", configFile.Path, "--format", "json" });

        Assert.Equal(0, result);
        var toolNames = GetToolNames(capture.StandardOutput);
        Assert.DoesNotContain("get-configuration-troubleshooting", toolNames);
    }

    [Fact]
    public async Task DoctorCommand_WithConfigurationTroubleshootingToolEnabled_IncludesTroubleshootingToolName()
    {
        using var configFile = new TemporaryConfigFile(enableConfigurationTroubleshootingTool: true);
        using var capture = new ConsoleCapture();
        using var envScope = new EnvironmentVariableScope(TroubleshootingToolEnvVar, null);

        var result = await Program.Main(new[] { "doctor", "--config", configFile.Path, "--format", "json" });

        Assert.Equal(0, result);
        var toolNames = GetToolNames(capture.StandardOutput);
        Assert.Contains("get-configuration-troubleshooting", toolNames);
    }

    [Fact]
    public async Task DoctorCommand_WithEnvironmentOverride_EnablesConfigurationTroubleshootingTool()
    {
        using var configFile = new TemporaryConfigFile(enableConfigurationTroubleshootingTool: false);
        using var capture = new ConsoleCapture();
        using var envScope = new EnvironmentVariableScope(TroubleshootingToolEnvVar, "true");

        var result = await Program.Main(new[] { "doctor", "--config", configFile.Path, "--format", "json" });

        Assert.Equal(0, result);

        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        Assert.True(payload!["effectivePowerShellConfiguration"]?["EnableConfigurationTroubleshootingTool"]?.GetValue<bool>());

        var toolNames = GetToolNames(capture.StandardOutput);
        Assert.Contains("get-configuration-troubleshooting", toolNames);
    }

    private static string[] GetToolNames(string standardOutput)
    {
        return JsonNode.Parse(standardOutput.Trim())?["toolNames"]?.AsArray()
            .Select(node => node?.GetValue<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray()
            ?? Array.Empty<string>();
    }

    private sealed class TemporaryConfigFile : IDisposable
    {
        public string Path { get; }

        public TemporaryConfigFile(bool enableConfigurationTroubleshootingTool)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"poshmcp-doctor-tests-{Guid.NewGuid():N}.json");

            var json = $$"""
{
  "PowerShellConfiguration": {
    "FunctionNames": ["Get-Date"],
    "Modules": [],
    "ExcludePatterns": [],
    "IncludePatterns": [],
    "EnableConfigurationTroubleshootingTool": {{enableConfigurationTroubleshootingTool.ToString().ToLowerInvariant()}}
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

    private sealed class ConsoleCapture : IDisposable
    {
        private readonly TextWriter _originalOut;
        private readonly TextWriter _originalError;
        private readonly StringWriter _capturedOut;
        private readonly StringWriter _capturedError;

        public string StandardOutput => _capturedOut.ToString();

        public ConsoleCapture()
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

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        public EnvironmentVariableScope(string name, string? value)
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