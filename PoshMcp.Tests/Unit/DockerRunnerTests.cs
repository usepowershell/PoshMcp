using System;
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

        Assert.EndsWith(" .", result);
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
        var result = DockerRunner.BuildDockerBuildArgs("Dockerfile.custom", "registry.io/poshmcp:latest", "Pester Az.Accounts", "ghcr.io/usepowershell/poshmcp/poshmcp:latest");

        Assert.Equal("build -f Dockerfile.custom -t registry.io/poshmcp:latest --build-arg BASE_IMAGE=\"ghcr.io/usepowershell/poshmcp/poshmcp:latest\" --build-arg INSTALL_PS_MODULES=\"Pester Az.Accounts\" .", result);
    }

    [Fact]
    public void BuildDockerBuildArgs_WithSourceImage_ContainsBaseImageBuildArg()
    {
        var result = DockerRunner.BuildDockerBuildArgs("examples/Dockerfile.user", "poshmcp-custom:latest", sourceImage: "ghcr.io/usepowershell/poshmcp/poshmcp:latest");

        Assert.Contains("--build-arg BASE_IMAGE=\"ghcr.io/usepowershell/poshmcp/poshmcp:latest\"", result);
    }

    [Fact]
    public void BuildDockerBuildArgs_WithModules_KeepsBuildContextAsFinalArgument()
    {
        var result = DockerRunner.BuildDockerBuildArgs("Dockerfile", "poshmcp:latest", "Pester");

        Assert.EndsWith(" .", result);
        AssertBuildArgAppearsBeforeContext(result, "--build-arg INSTALL_PS_MODULES=\"Pester\"");
    }

    [Fact]
    public void BuildDockerBuildArgs_WithSourceImageAndModules_KeepsAllOptionsBeforeContext()
    {
        var result = DockerRunner.BuildDockerBuildArgs(
            "Dockerfile.custom",
            "registry.io/poshmcp:latest",
            "Pester Az.Accounts",
            "ghcr.io/usepowershell/poshmcp/poshmcp:latest");

        Assert.EndsWith(" .", result);
        AssertBuildArgAppearsBeforeContext(result, "--build-arg BASE_IMAGE=\"ghcr.io/usepowershell/poshmcp/poshmcp:latest\"");
        AssertBuildArgAppearsBeforeContext(result, "--build-arg INSTALL_PS_MODULES=\"Pester Az.Accounts\"");
    }

    private static void AssertBuildArgAppearsBeforeContext(string result, string buildArg)
    {
        var buildArgIndex = result.IndexOf(buildArg, StringComparison.Ordinal);
        Assert.True(buildArgIndex >= 0, $"Expected to find build arg: {buildArg}");

        var contextIndex = result.LastIndexOf(" .", StringComparison.Ordinal);
        Assert.True(contextIndex >= 0, "Expected to find build context argument '.'.");
        Assert.True(buildArgIndex < contextIndex, $"Expected '{buildArg}' to appear before final context '.'. Actual: {result}");
    }

    [Fact]
    public void ResolveSourceImageReference_WhenNoInput_UsesDefaultGhcrLatest()
    {
        var result = DockerRunner.ResolveSourceImageReference(null, null);

        Assert.Equal("ghcr.io/usepowershell/poshmcp/poshmcp:latest", result);
    }

    [Fact]
    public void ResolveSourceImageReference_WhenSourceImageIncludesTag_PreservesTag()
    {
        var result = DockerRunner.ResolveSourceImageReference("myregistry.io/custom/poshmcp:v2", null);

        Assert.Equal("myregistry.io/custom/poshmcp:v2", result);
    }

    [Fact]
    public void ResolveSourceImageReference_WhenTagProvided_OverridesExistingTag()
    {
        var result = DockerRunner.ResolveSourceImageReference("myregistry.io/custom/poshmcp:v2", "stable");

        Assert.Equal("myregistry.io/custom/poshmcp:stable", result);
    }

    [Fact]
    public void ResolveSourceImageReference_WhenSourceImageHasDigestAndTagProvided_UsesTagOnRepository()
    {
        var result = DockerRunner.ResolveSourceImageReference("myregistry.io/custom/poshmcp@sha256:abcdef", "stable");

        Assert.Equal("myregistry.io/custom/poshmcp:stable", result);
    }
}
