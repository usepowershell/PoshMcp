using Microsoft.Extensions.Logging.Abstractions;
using PoshMcp;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.OutOfProcess;
using Xunit;

namespace PoshMcp.Tests.Unit.Diagnostics;

[Trait("Category", "Unit")]
public class DoctorOutOfProcessSectionTests
{
    // Issue #261: in Pool mode, the ProcessPool sizing knobs are inert. The doctor
    // report previously emitted 0 for both effective values, which looked like a
    // bug. We now render "n/a (Pool mode)" to make the inertness explicit.

    [Fact]
    public void BuildOutOfProcessSection_PoolMode_RendersNaForProcessPoolKnobs()
    {
        var config = new PowerShellConfiguration
        {
            RuntimeMode = RuntimeMode.OutOfProcess,
            SubprocessHostMode = SubprocessHostMode.Pool,
            SubprocessRunspacePoolSize = 0,
            SubprocessPoolSize = 4,
            SubprocessMinHealthyForStartup = 1,
        };

        var section = DoctorService.BuildOutOfProcessSection(config, configurationPath: null, NullLoggerFactory.Instance);

        Assert.True(section.Applicable);
        Assert.Equal(nameof(SubprocessHostMode.Pool), section.HostMode);
        Assert.Equal("n/a (Pool mode)", section.EffectiveProcessPoolSize);
        Assert.Equal("n/a (Pool mode)", section.EffectiveMinHealthyForStartup);
    }

    [Fact]
    public void BuildOutOfProcessSection_ProcessPoolMode_RendersIntegerStringsForEffectiveSizes()
    {
        var config = new PowerShellConfiguration
        {
            RuntimeMode = RuntimeMode.OutOfProcess,
            SubprocessHostMode = SubprocessHostMode.ProcessPool,
            SubprocessPoolSize = 4,
            SubprocessMinHealthyForStartup = 1,
        };

        var section = DoctorService.BuildOutOfProcessSection(config, configurationPath: null, NullLoggerFactory.Instance);

        Assert.True(section.Applicable);
        Assert.Equal(nameof(SubprocessHostMode.ProcessPool), section.HostMode);
        Assert.Equal("4", section.EffectiveProcessPoolSize);
        Assert.Equal("1", section.EffectiveMinHealthyForStartup);
    }

    [Fact]
    public void BuildOutOfProcessSection_ProcessPoolMode_ClampsMinHealthyToPoolSize()
    {
        // SubprocessMinHealthyForStartup > SubprocessPoolSize must clamp down.
        var config = new PowerShellConfiguration
        {
            RuntimeMode = RuntimeMode.OutOfProcess,
            SubprocessHostMode = SubprocessHostMode.ProcessPool,
            SubprocessPoolSize = 2,
            SubprocessMinHealthyForStartup = 10,
        };

        var section = DoctorService.BuildOutOfProcessSection(config, configurationPath: null, NullLoggerFactory.Instance);

        Assert.Equal("2", section.EffectiveProcessPoolSize);
        Assert.Equal("2", section.EffectiveMinHealthyForStartup);
    }

    [Fact]
    public void BuildOutOfProcessSection_ProcessPoolMode_DefaultsPoolSizeWhenZero()
    {
        var config = new PowerShellConfiguration
        {
            RuntimeMode = RuntimeMode.OutOfProcess,
            SubprocessHostMode = SubprocessHostMode.ProcessPool,
            SubprocessPoolSize = 0,
            SubprocessMinHealthyForStartup = 0,
        };

        var section = DoctorService.BuildOutOfProcessSection(config, configurationPath: null, NullLoggerFactory.Instance);

        // Defaults: pool size 4, min healthy 1.
        Assert.Equal("4", section.EffectiveProcessPoolSize);
        Assert.Equal("1", section.EffectiveMinHealthyForStartup);
    }

    [Fact]
    public void BuildOutOfProcessSection_NotOutOfProcess_IsNotApplicable()
    {
        var config = new PowerShellConfiguration
        {
            RuntimeMode = RuntimeMode.InProcess,
        };

        var section = DoctorService.BuildOutOfProcessSection(config, configurationPath: null, NullLoggerFactory.Instance);

        Assert.False(section.Applicable);
    }
}
