using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Collection("TransportSelectionTests")]
public class ProgramTransportSelectionTests
{
    private const string TransportEnvVar = "POSHMCP_TRANSPORT";

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

    private sealed class TemporaryConfigFile : IDisposable
    {
        public string Path { get; }

        public TemporaryConfigFile()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"poshmcp-transport-tests-{Guid.NewGuid():N}.json");

            var json = @"{
  ""PowerShellConfiguration"": {
    ""FunctionNames"": [""Get-Date""],
    ""Modules"": [],
    ""ExcludePatterns"": [],
    ""IncludePatterns"": []
  }
}";

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