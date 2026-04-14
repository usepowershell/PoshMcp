using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Collection("TransportSelectionTests")]
public class ProgramTransportSelectionTests
{
    private const string TransportEnvVar = "POSHMCP_TRANSPORT";
    private const string SessionModeEnvVar = "POSHMCP_SESSION_MODE";
    private const string McpPathEnvVar = "POSHMCP_MCP_PATH";

    [Fact]
    public async Task DoctorCommand_WithNoTransportInputs_ShouldDefaultToStdio()
    {
        using var configFile = new TemporaryConfigFile();
        using var capture = new ConsoleCapture();
        using var transportScope = new EnvironmentVariableScope(TransportEnvVar, null);

        var result = await Program.Main(new[] { "doctor", "--config", configFile.Path, "--format", "json" });

        Assert.Equal(0, result);
        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        Assert.Equal("stdio", payload!["effectiveTransport"]?.GetValue<string>());
    }

    [Fact]
    public async Task DoctorCommand_WithTransportEnvironmentVariable_ShouldUseEnvironmentValue()
    {
        using var configFile = new TemporaryConfigFile();
        using var capture = new ConsoleCapture();
        using var transportScope = new EnvironmentVariableScope(TransportEnvVar, "http");

        var result = await Program.Main(new[] { "doctor", "--config", configFile.Path, "--format", "json" });

        Assert.Equal(0, result);
        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        Assert.Equal("http", payload!["effectiveTransport"]?.GetValue<string>());
        Assert.Equal("env", payload["effectiveTransportSource"]?.GetValue<string>());
    }

    [Fact]
    public async Task DoctorCommand_WithCliTransportStdio_ShouldOverrideEnvironmentTransport()
    {
        using var configFile = new TemporaryConfigFile();
        using var capture = new ConsoleCapture();
        using var transportScope = new EnvironmentVariableScope(TransportEnvVar, "http");

        var result = await Program.Main(new[] { "doctor", "--config", configFile.Path, "--transport", "stdio", "--format", "json" });

        Assert.Equal(0, result);
        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        Assert.Equal("stdio", payload!["effectiveTransport"]?.GetValue<string>());
        Assert.Equal("cli", payload["effectiveTransportSource"]?.GetValue<string>());
    }

    [Fact]
    public async Task DoctorCommand_WithCliTransportHttp_ShouldUseCliValue()
    {
        using var configFile = new TemporaryConfigFile();
        using var capture = new ConsoleCapture();
        using var transportScope = new EnvironmentVariableScope(TransportEnvVar, null);

        var result = await Program.Main(new[] { "doctor", "--config", configFile.Path, "--transport", "http", "--format", "json" });

        Assert.Equal(0, result);
        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        Assert.Equal("http", payload!["effectiveTransport"]?.GetValue<string>());
        Assert.Equal("cli", payload["effectiveTransportSource"]?.GetValue<string>());
    }

    [Fact]
    public async Task DoctorCommand_WithSessionModeAndMcpPathEnvironmentVariables_ShouldUseEnvironmentValues()
    {
        using var configFile = new TemporaryConfigFile();
        using var capture = new ConsoleCapture();
        using var transportScope = new EnvironmentVariableScope(TransportEnvVar, null);
        using var sessionModeScope = new EnvironmentVariableScope(SessionModeEnvVar, "multi");
        using var mcpPathScope = new EnvironmentVariableScope(McpPathEnvVar, "/env-mcp");

        var result = await Program.Main(new[] { "doctor", "--config", configFile.Path, "--format", "json" });

        Assert.Equal(0, result);
        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        Assert.Equal("multi", payload!["effectiveSessionMode"]?.GetValue<string>());
        Assert.Equal("env", payload["effectiveSessionModeSource"]?.GetValue<string>());
        Assert.Equal("/env-mcp", payload["effectiveMcpPath"]?.GetValue<string>());
        Assert.Equal("env", payload["effectiveMcpPathSource"]?.GetValue<string>());
    }

    [Fact]
    public async Task DoctorCommand_WithCliSessionModeAndMcpPath_ShouldOverrideEnvironmentValues()
    {
        using var configFile = new TemporaryConfigFile();
        using var capture = new ConsoleCapture();
        using var transportScope = new EnvironmentVariableScope(TransportEnvVar, null);
        using var sessionModeScope = new EnvironmentVariableScope(SessionModeEnvVar, "single");
        using var mcpPathScope = new EnvironmentVariableScope(McpPathEnvVar, "/env-mcp");

        var result = await Program.Main(new[]
        {
            "doctor",
            "--config", configFile.Path,
            "--session-mode", "multi",
            "--mcp-path", "/cli-mcp",
            "--format", "json"
        });

        Assert.Equal(0, result);
        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        Assert.Equal("multi", payload!["effectiveSessionMode"]?.GetValue<string>());
        Assert.Equal("cli", payload["effectiveSessionModeSource"]?.GetValue<string>());
        Assert.Equal("/cli-mcp", payload["effectiveMcpPath"]?.GetValue<string>());
        Assert.Equal("cli", payload["effectiveMcpPathSource"]?.GetValue<string>());
    }

    [Fact]
    public async Task DoctorCommand_WithOutOfProcessRuntimeAndConfiguredModulePath_ShouldReportConfiguredPathInDiagnostics()
    {
        using var moduleDirectory = new TemporaryDirectory("poshmcp-oop-module-path-tests");
        using var configFile = new TemporaryConfigFile(runtimeMode: "OutOfProcess", modulePaths: new[] { moduleDirectory.Path });
        using var capture = new ConsoleCapture();
        using var transportScope = new EnvironmentVariableScope(TransportEnvVar, null);

        var result = await Program.Main(new[] { "doctor", "--config", configFile.Path, "--format", "json" });

        Assert.Equal(0, result);
        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        Assert.Equal("OutOfProcess", payload!["effectiveRuntimeMode"]?.GetValue<string>());
        Assert.True(
            PayloadContainsConfiguredModulePath(payload, moduleDirectory.Path),
            $"Expected doctor output to include configured OOP module path '{moduleDirectory.Path}' in diagnostics output.");
    }

    [Fact]
    public async Task DoctorCommand_WithOutOfProcessRuntimeOverrideAndConfiguredModulePath_ShouldReportConfiguredPathInDiagnostics()
    {
        using var moduleDirectory = new TemporaryDirectory("poshmcp-oop-module-path-tests");
        using var configFile = new TemporaryConfigFile(modulePaths: new[] { moduleDirectory.Path });
        using var capture = new ConsoleCapture();
        using var transportScope = new EnvironmentVariableScope(TransportEnvVar, null);

        var result = await Program.Main(new[]
        {
            "doctor",
            "--config", configFile.Path,
            "--runtime-mode", "out-of-process",
            "--format", "json"
        });

        Assert.Equal(0, result);
        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        Assert.Equal("OutOfProcess", payload!["effectiveRuntimeMode"]?.GetValue<string>());
        Assert.Equal("cli", payload["effectiveRuntimeModeSource"]?.GetValue<string>());
        Assert.True(
            PayloadContainsConfiguredModulePath(payload, moduleDirectory.Path),
            $"Expected doctor output to include configured OOP module path '{moduleDirectory.Path}' in diagnostics output.");
    }

    private static bool PayloadContainsConfiguredModulePath(JsonObject payload, string expectedPath)
    {
        var normalizedExpected = NormalizePath(expectedPath);

        var configuredModulePaths = payload["effectivePowerShellConfiguration"]?["Environment"]?["ModulePaths"]?.AsArray()
            ?.Select(n => n?.GetValue<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Cast<string>()
            .Select(NormalizePath)
            .ToList()
            ?? new();

        var oopModulePaths = payload["oopModulePaths"]?.AsArray()
            ?.Select(n => n?.GetValue<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Cast<string>()
            .Select(NormalizePath)
            .ToList()
            ?? new();

        if (!configuredModulePaths.Contains(normalizedExpected))
        {
            return false;
        }

        return oopModulePaths.Contains(normalizedExpected);
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
    }

    private sealed class TemporaryConfigFile : IDisposable
    {
        public string Path { get; }

        public TemporaryConfigFile(string? runtimeMode = null, string[]? modulePaths = null)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"poshmcp-transport-tests-{Guid.NewGuid():N}.json");

            var runtimeModeJson = string.IsNullOrWhiteSpace(runtimeMode)
                ? string.Empty
                : $",\n    \"RuntimeMode\": \"{runtimeMode}\"";

            var modulePathEntries = (modulePaths ?? Array.Empty<string>())
                .Select(p => $"\"{p.Replace("\\", "\\\\")}\"")
                .ToArray();
            var modulePathsJson = string.Join(", ", modulePathEntries);

            var json = $@"{{
  ""PowerShellConfiguration"": {{
    ""FunctionNames"": [""Get-Date""],
    ""Modules"": [],
    ""ExcludePatterns"": [],
    ""IncludePatterns"": []{runtimeModeJson},
    ""Environment"": {{
      ""ModulePaths"": [{modulePathsJson}]
    }}
  }}
}}";

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

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; }

        public TemporaryDirectory(string namePrefix)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{namePrefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
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
        public string StandardError => _capturedError.ToString();

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