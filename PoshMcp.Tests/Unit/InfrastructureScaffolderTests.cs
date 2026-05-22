using System;
using System.IO;
using System.Threading.Tasks;
using PoshMcp.Tests.Shared;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class InfrastructureScaffolderTests
{
    private static readonly string[] ExpectedRelativePaths =
    {
        Path.Combine("infra", "azure", "deploy.ps1"),
        Path.Combine("infra", "azure", "validate.ps1"),
        Path.Combine("infra", "azure", "main.bicep"),
        Path.Combine("infra", "azure", "resources.bicep"),
        Path.Combine("infra", "azure", "parameters.json"),
        Path.Combine("infra", "azure", "deploy.appsettings.json.template"),
        Path.Combine("infra", "azure", "parameters.local.json.template")
    };

    [Fact]
    public async Task ScaffoldAzureInfrastructureAsync_WithEmptyDirectory_WritesAllExpectedFiles()
    {
        using var tempDirectory = new TempDirectory();

        var result = await global::PoshMcp.InfrastructureScaffolder.ScaffoldAzureInfrastructureAsync(tempDirectory.Path, force: false);

        Assert.Equal(7, result.FilesWritten);
        Assert.Equal(0, result.FilesOverwritten);
        Assert.All(ExpectedRelativePaths, relativePath => Assert.True(File.Exists(Path.Combine(tempDirectory.Path, relativePath))));
    }

    [Fact]
    public async Task ScaffoldAzureInfrastructureAsync_CreatesInfraAzureSubdirectory()
    {
        using var tempDirectory = new TempDirectory();
        Directory.Delete(tempDirectory.Path, recursive: true);

        await global::PoshMcp.InfrastructureScaffolder.ScaffoldAzureInfrastructureAsync(tempDirectory.Path, force: false);

        Assert.True(Directory.Exists(Path.Combine(tempDirectory.Path, "infra", "azure")));
    }

    [Fact]
    public async Task ScaffoldAzureInfrastructureAsync_WithForceTrue_OverwritesExistingFiles()
    {
        using var tempDirectory = new TempDirectory();
        var existingFilePath = Path.Combine(tempDirectory.Path, ExpectedRelativePaths[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(existingFilePath)!);
        await File.WriteAllTextAsync(existingFilePath, "custom-content");

        var result = await global::PoshMcp.InfrastructureScaffolder.ScaffoldAzureInfrastructureAsync(tempDirectory.Path, force: true);

        Assert.Equal(1, result.FilesOverwritten);
        Assert.Equal(7, result.FilesWritten);
        Assert.NotEqual("custom-content", await File.ReadAllTextAsync(existingFilePath));
    }

    [Fact]
    public async Task ScaffoldAzureInfrastructureAsync_WithForceFalse_ThrowsIOException_WhenFileExists()
    {
        using var tempDirectory = new TempDirectory();
        var existingFilePath = Path.Combine(tempDirectory.Path, ExpectedRelativePaths[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(existingFilePath)!);
        await File.WriteAllTextAsync(existingFilePath, "custom-content");

        var exception = await Assert.ThrowsAsync<IOException>(() => global::PoshMcp.InfrastructureScaffolder.ScaffoldAzureInfrastructureAsync(tempDirectory.Path, force: false));

        Assert.Contains(existingFilePath, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task ScaffoldAzureInfrastructureAsync_WithEmptyOrWhitespacePath_ThrowsArgumentException(string targetProjectPath)
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => global::PoshMcp.InfrastructureScaffolder.ScaffoldAzureInfrastructureAsync(targetProjectPath, force: false));

        Assert.Equal("targetProjectPath", exception.ParamName);
    }

    [Fact]
    public async Task ScaffoldAzureInfrastructureAsync_WithNullPath_ThrowsArgumentException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => global::PoshMcp.InfrastructureScaffolder.ScaffoldAzureInfrastructureAsync(null!, force: false));

        Assert.Equal("targetProjectPath", exception.ParamName);
    }

    [Fact]
    public async Task ScaffoldAzureInfrastructureAsync_ReturnsAbsoluteProjectPathForPathWithDotSegment()
    {
        using var tempRoot = new TempDirectory();
        var relativeProjectPath = Path.Combine(tempRoot.Path, ".", "sample-project");

        var result = await global::PoshMcp.InfrastructureScaffolder.ScaffoldAzureInfrastructureAsync(relativeProjectPath, force: false);

        Assert.Equal(Path.GetFullPath(relativeProjectPath), result.ProjectPath);
    }

    [Fact]
    public async Task ScaffoldAzureInfrastructureAsync_ReturnsRelativeInfraPath()
    {
        using var tempDirectory = new TempDirectory();

        var result = await global::PoshMcp.InfrastructureScaffolder.ScaffoldAzureInfrastructureAsync(tempDirectory.Path, force: false);

        Assert.Equal("infra/azure", result.RelativeInfraPath);
    }

    [Fact]
    public async Task ScaffoldAzureInfrastructureAsync_WritesNonEmptyFiles()
    {
        using var tempDirectory = new TempDirectory();

        await global::PoshMcp.InfrastructureScaffolder.ScaffoldAzureInfrastructureAsync(tempDirectory.Path, force: false);

        Assert.All(ExpectedRelativePaths, relativePath => Assert.True(new FileInfo(Path.Combine(tempDirectory.Path, relativePath)).Length > 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScaffoldAzureInfrastructureAsync_ReflectsForceFlagInResult(bool force)
    {
        using var tempDirectory = new TempDirectory();

        if (force)
        {
            var existingFilePath = Path.Combine(tempDirectory.Path, ExpectedRelativePaths[0]);
            Directory.CreateDirectory(Path.GetDirectoryName(existingFilePath)!);
            await File.WriteAllTextAsync(existingFilePath, "custom-content");
        }

        var result = await global::PoshMcp.InfrastructureScaffolder.ScaffoldAzureInfrastructureAsync(tempDirectory.Path, force);

        Assert.Equal(force, result.Force);
    }

}
