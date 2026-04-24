using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Collection("TransportSelectionTests")]
public class ProgramCliScaffoldCommandTests
{
    [Fact]
    public async Task ScaffoldCommand_WithProjectPath_ScaffoldsEmbeddedAzureInfrastructureAssets()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var capture = new ConsoleCapture();

        var projectDirectory = Path.Combine(tempDirectory.Path, "sample-project");
        var result = await Program.Main(new[]
        {
            "scaffold",
            "--project-path", projectDirectory,
            "--format", "json"
        });

        Assert.Equal(0, result);

        var payload = JsonNode.Parse(capture.StandardOutput.Trim())?.AsObject();
        Assert.NotNull(payload);
        Assert.True(payload!["success"]?.GetValue<bool>());
        Assert.Equal(7, payload["filesWritten"]?.GetValue<int>());

        Assert.True(File.Exists(Path.Combine(projectDirectory, "infra", "azure", "deploy.ps1")));
        Assert.True(File.Exists(Path.Combine(projectDirectory, "infra", "azure", "validate.ps1")));
        Assert.True(File.Exists(Path.Combine(projectDirectory, "infra", "azure", "main.bicep")));
        Assert.True(File.Exists(Path.Combine(projectDirectory, "infra", "azure", "resources.bicep")));
        Assert.True(File.Exists(Path.Combine(projectDirectory, "infra", "azure", "parameters.json")));
        Assert.True(File.Exists(Path.Combine(projectDirectory, "infra", "azure", "deploy.appsettings.json.template")));
        Assert.True(File.Exists(Path.Combine(projectDirectory, "infra", "azure", "parameters.local.json.template")));
    }

    [Fact]
    public async Task ScaffoldCommand_WhenFilesExistWithoutForce_ReturnsConfigError()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var capture = new ConsoleCapture();

        var projectDirectory = Path.Combine(tempDirectory.Path, "sample-project");

        var firstResult = await Program.Main(new[]
        {
            "scaffold",
            "--project-path", projectDirectory
        });

        Assert.Equal(0, firstResult);

        var deployScriptPath = Path.Combine(projectDirectory, "infra", "azure", "deploy.ps1");
        await File.WriteAllTextAsync(deployScriptPath, "custom-content");

        var secondResult = await Program.Main(new[]
        {
            "scaffold",
            "--project-path", projectDirectory
        });

        Assert.Equal(0, secondResult);
        Assert.Contains("already exists", capture.StandardError, StringComparison.OrdinalIgnoreCase);

        var contentAfterFailure = await File.ReadAllTextAsync(deployScriptPath);
        Assert.Equal("custom-content", contentAfterFailure);
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; }

        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"poshmcp-cli-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
