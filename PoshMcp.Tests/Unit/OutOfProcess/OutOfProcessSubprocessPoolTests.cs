using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.OutOfProcess;
using Xunit;

namespace PoshMcp.Tests.Unit.OutOfProcess;

/// <summary>
/// Unit tests for <see cref="OutOfProcessSubprocessPool"/> internals that don't
/// require a live pwsh subprocess: constructor validation, state transitions
/// before <c>StartAsync</c>, environment fingerprint stability, and the
/// <see cref="SubprocessHostMode"/> string detection.
/// </summary>
[Trait("Category", "OutOfProcess")]
public class OutOfProcessSubprocessPoolTests
{
    [Fact]
    public void Constructor_NullPwsh_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new OutOfProcessSubprocessPool(
                string.Empty, "x.ps1",
                new OutOfProcessSubprocessPoolOptions { PoolSize = 1 }));
    }

    [Fact]
    public void Constructor_NullScript_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new OutOfProcessSubprocessPool(
                "pwsh", string.Empty,
                new OutOfProcessSubprocessPoolOptions { PoolSize = 1 }));
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new OutOfProcessSubprocessPool("pwsh", "x.ps1", null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_InvalidPoolSize_Throws(int poolSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new OutOfProcessSubprocessPool(
                "pwsh", "x.ps1",
                new OutOfProcessSubprocessPoolOptions { PoolSize = poolSize }));
    }

    [Fact]
    public void Constructor_MinHealthyExceedsPoolSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new OutOfProcessSubprocessPool(
                "pwsh", "x.ps1",
                new OutOfProcessSubprocessPoolOptions { PoolSize = 2, MinHealthyForStartup = 5 }));
    }

    [Fact]
    public async Task DisposeAsync_BeforeStart_DoesNotThrow()
    {
        var pool = new OutOfProcessSubprocessPool(
            "pwsh", "x.ps1",
            new OutOfProcessSubprocessPoolOptions { PoolSize = 1 });
        await pool.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var pool = new OutOfProcessSubprocessPool(
            "pwsh", "x.ps1",
            new OutOfProcessSubprocessPoolOptions { PoolSize = 1 });
        await pool.DisposeAsync();
        await pool.DisposeAsync();
    }

    [Fact]
    public async Task InvokeAsync_BeforeStart_Throws()
    {
        var pool = new OutOfProcessSubprocessPool(
            "pwsh", "x.ps1",
            new OutOfProcessSubprocessPoolOptions { PoolSize = 1 });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pool.InvokeAsync("Get-Date", new Dictionary<string, object?>(), CancellationToken.None));

        await pool.DisposeAsync();
    }

    [Fact]
    public void HealthyCount_BeforeStart_IsZero()
    {
        var pool = new OutOfProcessSubprocessPool(
            "pwsh", "x.ps1",
            new OutOfProcessSubprocessPoolOptions { PoolSize = 4 });

        Assert.Equal(0, pool.HealthyCount);
        Assert.Equal(4, pool.PoolSize);
    }

    // ---- ComputeEnvironmentFingerprint ----

    [Fact]
    public void Fingerprint_IdenticalConfigs_ProduceSameHash()
    {
        var c1 = new EnvironmentConfiguration
        {
            ImportModules = { "Pester", "PSReadLine" },
            ModulePaths = { "/a", "/b" },
            TrustPSGallery = true,
        };
        var c2 = new EnvironmentConfiguration
        {
            ImportModules = { "Pester", "PSReadLine" },
            ModulePaths = { "/a", "/b" },
            TrustPSGallery = true,
        };

        var f1 = OutOfProcessSubprocessPool.ComputeEnvironmentFingerprint(c1, null);
        var f2 = OutOfProcessSubprocessPool.ComputeEnvironmentFingerprint(c2, null);
        Assert.Equal(f1, f2);
        Assert.NotEqual(string.Empty, f1);
        Assert.Equal(64, f1.Length); // SHA-256 hex
    }

    [Fact]
    public void Fingerprint_OrderingDifferences_ProduceSameHash()
    {
        var c1 = new EnvironmentConfiguration
        {
            ImportModules = { "Pester", "PSReadLine" },
            ModulePaths = { "/a", "/b" },
        };
        var c2 = new EnvironmentConfiguration
        {
            ImportModules = { "PSReadLine", "Pester" },
            ModulePaths = { "/b", "/a" },
        };

        var f1 = OutOfProcessSubprocessPool.ComputeEnvironmentFingerprint(c1, null);
        var f2 = OutOfProcessSubprocessPool.ComputeEnvironmentFingerprint(c2, null);
        Assert.Equal(f1, f2);
    }

    [Fact]
    public void Fingerprint_DiscoveryModulesIncluded()
    {
        var config = new EnvironmentConfiguration { ImportModules = { "Pester" } };
        var f1 = OutOfProcessSubprocessPool.ComputeEnvironmentFingerprint(config, null);
        var f2 = OutOfProcessSubprocessPool.ComputeEnvironmentFingerprint(
            config, new[] { "Az.Accounts" });
        Assert.NotEqual(f1, f2);
    }

    [Fact]
    public void Fingerprint_StartupScriptChange_ProducesDifferentHash()
    {
        var c1 = new EnvironmentConfiguration { StartupScript = "Write-Host 'a'" };
        var c2 = new EnvironmentConfiguration { StartupScript = "Write-Host 'b'" };
        Assert.NotEqual(
            OutOfProcessSubprocessPool.ComputeEnvironmentFingerprint(c1, null),
            OutOfProcessSubprocessPool.ComputeEnvironmentFingerprint(c2, null));
    }

    [Fact]
    public void Fingerprint_TrustPSGalleryChange_ProducesDifferentHash()
    {
        var c1 = new EnvironmentConfiguration { TrustPSGallery = true };
        var c2 = new EnvironmentConfiguration { TrustPSGallery = false };
        Assert.NotEqual(
            OutOfProcessSubprocessPool.ComputeEnvironmentFingerprint(c1, null),
            OutOfProcessSubprocessPool.ComputeEnvironmentFingerprint(c2, null));
    }

    [Fact]
    public void Fingerprint_NullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => OutOfProcessSubprocessPool.ComputeEnvironmentFingerprint(null!, null));
    }
}
