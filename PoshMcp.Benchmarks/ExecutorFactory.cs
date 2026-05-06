using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PoshMcp.Server.PowerShell.OutOfProcess;

namespace PoshMcp.Benchmarks;

/// <summary>
/// Builds an <see cref="ICommandExecutor"/> for each <see cref="HostMode"/>
/// using the same wiring path the production server uses
/// (<c>McpToolSetupService.StartOutOfProcessExecutorIfNeededAsync</c>),
/// without pulling in the hosting / configuration / logging stack.
///
/// <para>
/// Skip the executor's <c>SetupAsync</c> step on purpose: benchmarks measure
/// the executor itself, not environment customization (module install /
/// import / startup script). Built-in cmdlets like <c>Get-Date</c>,
/// <c>Get-Random</c>, and <c>Write-Output</c> are callable via
/// <see cref="ICommandExecutor.InvokeAsync"/> without prior discovery.
/// </para>
/// </summary>
internal static class ExecutorFactory
{
    /// <summary>
    /// Default size for the <see cref="HostMode.ProcessPool"/> pool.
    /// Matches <c>PowerShellConfiguration.SubprocessPoolSize</c>'s default.
    /// </summary>
    public const int DefaultProcessPoolSize = 4;

    /// <summary>
    /// Default size for the <see cref="HostMode.Pool"/> runspace pool.
    /// 0 lets the host pick min(ProcessorCount, 8).
    /// </summary>
    public const int DefaultRunspacePoolSize = 0;

    /// <summary>
    /// Constructs and starts an executor for the given <paramref name="mode"/>.
    /// Caller is responsible for <see cref="ICommandExecutor.DisposeAsync"/>.
    /// </summary>
    public static async Task<BenchExecutor> CreateAsync(
        HostMode mode,
        TimeSpan? requestTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var timeout = requestTimeout ?? TimeSpan.FromSeconds(30);

        switch (mode)
        {
            case HostMode.Single:
            {
                var exec = new OutOfProcessCommandExecutor(
                    NullLoggerFactory.Instance,
                    requestTimeout: timeout,
                    hostMode: SubprocessHostMode.Single,
                    runspacePoolSize: 0);
                await exec.StartAsync(cancellationToken).ConfigureAwait(false);
                return new SingleOrPoolBenchExecutor(exec);
            }

            case HostMode.Pool:
            {
                var exec = new OutOfProcessCommandExecutor(
                    NullLoggerFactory.Instance,
                    requestTimeout: timeout,
                    hostMode: SubprocessHostMode.Pool,
                    runspacePoolSize: DefaultRunspacePoolSize);
                await exec.StartAsync(cancellationToken).ConfigureAwait(false);
                return new SingleOrPoolBenchExecutor(exec);
            }

            case HostMode.ProcessPool:
            {
                // Resolve pwsh + the single-runspace host script (ProcessPool
                // hosts run oop-host.ps1, not the pool variant — each subprocess
                // owns one runspace).
                var resolver = new OutOfProcessCommandExecutor(
                    NullLoggerFactory.Instance,
                    hostMode: SubprocessHostMode.Single);
                var hostScript = await resolver.ResolveHostScriptPathAsync().ConfigureAwait(false);
                var pwshPath = OutOfProcessCommandExecutor.ResolvePwshPath();
                await resolver.DisposeAsync().ConfigureAwait(false);

                var pool = new OutOfProcessSubprocessPool(
                    pwshPath,
                    hostScript,
                    new OutOfProcessSubprocessPoolOptions
                    {
                        PoolSize = DefaultProcessPoolSize,
                        MinHealthyForStartup = 1,
                    },
                    NullLoggerFactory.Instance,
                    requestTimeout: timeout);

                await pool.StartAsync(cancellationToken).ConfigureAwait(false);
                return new ProcessPoolBenchExecutor(pool);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown HostMode.");
        }
    }
}

/// <summary>
/// Wraps an <see cref="ICommandExecutor"/> with crash-injection helpers the
/// benchmark scenarios need but that aren't part of the production interface.
/// </summary>
internal abstract class BenchExecutor : IAsyncDisposable
{
    public abstract ICommandExecutor Executor { get; }

    /// <summary>
    /// Forcibly kills one underlying pwsh subprocess to simulate a crash.
    /// Returns the killed PID, or null if no live host could be found.
    /// </summary>
    /// <remarks>
    /// For <see cref="HostMode.Single"/> and <see cref="HostMode.Pool"/> this
    /// kills the only subprocess — the executor will not auto-recover; the
    /// caller is expected to dispose and recreate.
    /// For <see cref="HostMode.ProcessPool"/> this kills one of N hosts; the
    /// pool's reconciler replaces it on its next sweep, and other hosts serve
    /// requests in the meantime.
    /// </remarks>
    public abstract int? KillOneHost();

    public abstract ValueTask DisposeAsync();
}

internal sealed class SingleOrPoolBenchExecutor : BenchExecutor
{
    private OutOfProcessCommandExecutor _exec;
    public override ICommandExecutor Executor => _exec;

    public SingleOrPoolBenchExecutor(OutOfProcessCommandExecutor exec)
    {
        _exec = exec;
    }

    public override int? KillOneHost()
    {
        // Reflect into _host._process — both fields are private; we live in
        // the same friend assembly so we could surface them, but reflection
        // keeps the bench-only crash hook out of the production type.
        var hostField = typeof(OutOfProcessCommandExecutor)
            .GetField("_host", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var host = (OutOfProcessHost?)hostField?.GetValue(_exec);
        if (host is null) return null;

        var procField = typeof(OutOfProcessHost)
            .GetField("_process", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var proc = (Process?)procField?.GetValue(host);
        if (proc is null) return null;

        try
        {
            var pid = proc.Id;
            proc.Kill(entireProcessTree: true);
            return pid;
        }
        catch
        {
            return null;
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await _exec.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed class ProcessPoolBenchExecutor : BenchExecutor
{
    private readonly OutOfProcessSubprocessPool _pool;
    public override ICommandExecutor Executor => _pool;

    public ProcessPoolBenchExecutor(OutOfProcessSubprocessPool pool)
    {
        _pool = pool;
    }

    public override int? KillOneHost()
    {
        // _slots is a ConcurrentDictionary<int, HostSlot> on the pool; HostSlot
        // exposes Host (an OutOfProcessHost). Walk via reflection — HostSlot
        // and the field are internal, but reflecting into private state keeps
        // the benchmark contract loose against future refactors.
        var slotsField = typeof(OutOfProcessSubprocessPool)
            .GetField("_slots", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var slots = slotsField?.GetValue(_pool) as System.Collections.IEnumerable;
        if (slots is null) return null;

        foreach (var kvp in slots)
        {
            var slot = kvp.GetType().GetProperty("Value")?.GetValue(kvp);
            if (slot is null) continue;

            var hostProp = slot.GetType().GetField("Host");
            var host = hostProp?.GetValue(slot) as OutOfProcessHost;
            if (host is null || !host.IsRunning) continue;

            var procField = typeof(OutOfProcessHost)
                .GetField("_process", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var proc = procField?.GetValue(host) as Process;
            if (proc is null) continue;

            try
            {
                var pid = proc.Id;
                proc.Kill(entireProcessTree: true);
                return pid;
            }
            catch
            {
                // Try the next slot.
            }
        }
        return null;
    }

    public override async ValueTask DisposeAsync()
    {
        await _pool.DisposeAsync().ConfigureAwait(false);
    }
}
