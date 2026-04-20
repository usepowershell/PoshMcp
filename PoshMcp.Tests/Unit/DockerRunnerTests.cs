using PoshMcp;
using Xunit;

namespace PoshMcp.Tests.Unit;

public class DockerRunnerTests
{
    [Fact]
    public void BuildDockerBuildArgs_DefaultBuild_ReturnsExpectedArgs()
    {
        var result = DockerRunner.BuildDockerBuildArgs("Dockerfile", "poshmcp:latest");

        Assert.Equal("build -f Dockerfile -t poshmcp:latest .", result);
    }

    [Fact]
    public void BuildDockerBuildArgs_Always_ContainsBuildContext()
    {
        // Regression test for #133: build context "." must always be present
        var result = DockerRunner.BuildDockerBuildArgs("Dockerfile", "poshmcp:latest");

        Assert.Contains(" .", result);
    }

    [Fact]
    public void BuildDockerBuildArgs_CustomTag_ContainsTag()
    {
        var result = DockerRunner.BuildDockerBuildArgs("Dockerfile", "myregistry.io/poshmcp:v1.0");

        Assert.Contains("-t myregistry.io/poshmcp:v1.0", result);
    }

    [Fact]
    public void BuildDockerBuildArgs_SingleModule_ContainsBuildArg()
    {
        var result = DockerRunner.BuildDockerBuildArgs("Dockerfile", "poshmcp:latest", "Pester");

        Assert.Contains("--build-arg INSTALL_PS_MODULES=\"Pester\"", result);
    }

    [Fact]
    public void BuildDockerBuildArgs_MultipleModules_ContainsAllModulesInBuildArg()
    {
        var result = DockerRunner.BuildDockerBuildArgs("Dockerfile", "poshmcp:latest", "Pester Az.Accounts");

        Assert.Contains("--build-arg INSTALL_PS_MODULES=\"Pester Az.Accounts\"", result);
    }

    [Fact]
    public void BuildDockerBuildArgs_CustomDockerfilePath_ContainsPath()
    {
        var result = DockerRunner.BuildDockerBuildArgs("examples/Dockerfile.azure", "poshmcp:latest");

        Assert.Contains("-f examples/Dockerfile.azure", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildDockerBuildArgs_NullOrWhitespaceModules_DoesNotContainBuildArg(string? modules)
    {
        var result = DockerRunner.BuildDockerBuildArgs("Dockerfile", "poshmcp:latest", modules);

        Assert.DoesNotContain("--build-arg", result);
    }

    [Fact]
    public void BuildDockerBuildArgs_ModulesWithCustomTag_ContainsBothTagAndBuildArg()
    {
        var result = DockerRunner.BuildDockerBuildArgs("Dockerfile", "myregistry.io/poshmcp:v2.0", "Pester");

        Assert.Contains("-t myregistry.io/poshmcp:v2.0", result);
        Assert.Contains("--build-arg INSTALL_PS_MODULES=\"Pester\"", result);
    }

    [Fact]
    public void BuildDockerBuildArgs_AllOptionsCombined_ReturnsExpectedFullOutput()
    {
        var result = DockerRunner.BuildDockerBuildArgs("Dockerfile.custom", "registry.io/poshmcp:latest", "Pester Az.Accounts");

        Assert.Equal("build -f Dockerfile.custom -t registry.io/poshmcp:latest . --build-arg INSTALL_PS_MODULES=\"Pester Az.Accounts\"", result);
    }
}
