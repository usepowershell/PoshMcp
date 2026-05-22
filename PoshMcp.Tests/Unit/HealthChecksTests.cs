using System;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Moq;
using PoshMcp.Server.Health;
using PoshMcp.Server.PowerShell;
using Xunit;
using PSPowerShell = System.Management.Automation.PowerShell;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public class HealthChecksTests
{
    [Fact]
    public async Task AssemblyGenerationHealthCheck_Healthy_WhenRunspaceReturnsTrue()
    {
        var runspace = new Mock<IPowerShellRunspace>();
        runspace.Setup(r => r.ExecuteThreadSafe(It.IsAny<Func<PSPowerShell, bool>>())).Returns(true);
        var healthCheck = new AssemblyGenerationHealthCheck(runspace.Object, Mock.Of<ILogger<AssemblyGenerationHealthCheck>>());

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("Assembly generation ready", result.Description);
    }

    [Fact]
    public async Task AssemblyGenerationHealthCheck_Degraded_WhenRunspaceReturnsFalse()
    {
        var runspace = new Mock<IPowerShellRunspace>();
        runspace.Setup(r => r.ExecuteThreadSafe(It.IsAny<Func<PSPowerShell, bool>>())).Returns(false);
        var healthCheck = new AssemblyGenerationHealthCheck(runspace.Object, Mock.Of<ILogger<AssemblyGenerationHealthCheck>>());

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal("Cannot introspect PowerShell commands", result.Description);
    }

    [Fact]
    public async Task AssemblyGenerationHealthCheck_Unhealthy_WhenRunspaceThrows()
    {
        var runspace = new Mock<IPowerShellRunspace>();
        runspace.Setup(r => r.ExecuteThreadSafe(It.IsAny<Func<PSPowerShell, bool>>())).Throws(new InvalidOperationException("assembly failure"));
        var healthCheck = new AssemblyGenerationHealthCheck(runspace.Object, Mock.Of<ILogger<AssemblyGenerationHealthCheck>>());

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.IsType<InvalidOperationException>(result.Exception);
    }

    [Fact]
    public async Task AssemblyGenerationHealthCheck_Unhealthy_ContainsExceptionMessage()
    {
        var runspace = new Mock<IPowerShellRunspace>();
        runspace.Setup(r => r.ExecuteThreadSafe(It.IsAny<Func<PSPowerShell, bool>>())).Throws(new InvalidOperationException("assembly failure"));
        var healthCheck = new AssemblyGenerationHealthCheck(runspace.Object, Mock.Of<ILogger<AssemblyGenerationHealthCheck>>());

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("assembly failure", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void AssemblyGenerationHealthCheck_NullRunspace_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new AssemblyGenerationHealthCheck(null!, Mock.Of<ILogger<AssemblyGenerationHealthCheck>>()));

        Assert.Equal("runspace", exception.ParamName);
    }

    [Fact]
    public void AssemblyGenerationHealthCheck_NullLogger_ThrowsArgumentNullException()
    {
        var runspace = new Mock<IPowerShellRunspace>();

        var exception = Assert.Throws<ArgumentNullException>(() => new AssemblyGenerationHealthCheck(runspace.Object, null!));

        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public async Task PowerShellRunspaceHealthCheck_Healthy_WhenRunspaceResponds()
    {
        var runspace = new Mock<IPowerShellRunspace>();
        runspace.Setup(r => r.ExecuteThreadSafe(It.IsAny<Func<PSPowerShell, (bool, string)>>())).Returns((true, "responsive"));
        var healthCheck = new PowerShellRunspaceHealthCheck(runspace.Object, Mock.Of<ILogger<PowerShellRunspaceHealthCheck>>());

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("responsive", result.Description);
    }

    [Fact]
    public async Task PowerShellRunspaceHealthCheck_Unhealthy_WhenRunspaceErrors()
    {
        var runspace = new Mock<IPowerShellRunspace>();
        runspace.Setup(r => r.ExecuteThreadSafe(It.IsAny<Func<PSPowerShell, (bool, string)>>())).Returns((false, "errors"));
        var healthCheck = new PowerShellRunspaceHealthCheck(runspace.Object, Mock.Of<ILogger<PowerShellRunspaceHealthCheck>>());

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("errors", result.Description);
    }

    [Fact]
    public async Task PowerShellRunspaceHealthCheck_Unhealthy_WhenRunspaceThrows()
    {
        var runspace = new Mock<IPowerShellRunspace>();
        runspace.Setup(r => r.ExecuteThreadSafe(It.IsAny<Func<PSPowerShell, (bool, string)>>())).Throws(new InvalidOperationException("runspace failure"));
        var healthCheck = new PowerShellRunspaceHealthCheck(runspace.Object, Mock.Of<ILogger<PowerShellRunspaceHealthCheck>>());

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("runspace failure", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PowerShellRunspaceHealthCheck_Unhealthy_ContainsDescription()
    {
        var runspace = new Mock<IPowerShellRunspace>();
        runspace.Setup(r => r.ExecuteThreadSafe(It.IsAny<Func<PSPowerShell, (bool, string)>>())).Returns((false, "custom description"));
        var healthCheck = new PowerShellRunspaceHealthCheck(runspace.Object, Mock.Of<ILogger<PowerShellRunspaceHealthCheck>>());

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("custom description", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void PowerShellRunspaceHealthCheck_NullRunspace_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new PowerShellRunspaceHealthCheck(null!, Mock.Of<ILogger<PowerShellRunspaceHealthCheck>>()));

        Assert.Equal("runspace", exception.ParamName);
    }

    [Fact]
    public void PowerShellRunspaceHealthCheck_NullLogger_ThrowsArgumentNullException()
    {
        var runspace = new Mock<IPowerShellRunspace>();

        var exception = Assert.Throws<ArgumentNullException>(() => new PowerShellRunspaceHealthCheck(runspace.Object, null!));

        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public async Task PowerShellRunspaceHealthCheck_Unhealthy_WhenCancelled()
    {
        var runspace = new Mock<IPowerShellRunspace>();
        var healthCheck = new PowerShellRunspaceHealthCheck(runspace.Object, Mock.Of<ILogger<PowerShellRunspaceHealthCheck>>());
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), cancellationSource.Token);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Health check cancelled or timed out", result.Description);
        runspace.Verify(r => r.ExecuteThreadSafe(It.IsAny<Func<PSPowerShell, (bool, string)>>()), Times.Never);
    }
}
