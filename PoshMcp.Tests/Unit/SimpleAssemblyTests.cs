using PoshMcp.Server.PowerShell;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Simple tests for PowerShell dynamic assembly generation
/// </summary>
[Collection("PowerShellRunspaceHolder")]
public class SimpleAssemblyTests : PowerShellTestBase
{
    public SimpleAssemblyTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void CanCreateTestClass()
    {
        // Simple test to verify the test framework is working
        Assert.True(true);
    }

    [Fact]
    public void PowerShellRunspaceIsAvailable()
    {
        // Reset state to allow re-initialization in tests
        PowerShellRunspaceHolder.ResetForTesting();

        try
        {
            // Initialize the PowerShellRunspaceHolder before accessing it
            var config = new PowerShellConfiguration();
            PowerShellRunspaceHolder.Initialize(config, Logger);

            // Test that we can access the PowerShell runspace
            var powerShell = PowerShellRunspaceHolder.Instance;
            Assert.NotNull(powerShell);
        }
        finally
        {
            // Clean up static state after test
            PowerShellRunspaceHolder.ResetForTesting();
        }
    }
}
