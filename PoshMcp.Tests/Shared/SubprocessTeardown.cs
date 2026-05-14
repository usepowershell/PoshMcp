using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PoshMcp.Tests;

/// <summary>
/// Centralized subprocess teardown for the test suite. Implements the contract
/// required by spec 009 FR-410/FR-412: kill the entire process tree, wait for
/// full process exit, and on Windows poll for handle release before declaring
/// teardown complete.
///
/// Every <see cref="Process"/> spawned by tests (directly or via helper/fixture)
/// MUST be torn down through this helper so the algorithm stays in one place.
/// All methods are exception-safe and never throw — they are designed to be
/// called from <c>finally</c> and <c>Dispose</c> paths.
/// </summary>
internal static class SubprocessTeardown
{
    /// <summary>
    /// Default time to wait for a killed process to exit before logging a warning
    /// and proceeding to the handle-release poll. Generous enough to absorb a
    /// loaded CI host yet bounded so a hung child cannot block the whole run.
    /// </summary>
    public static readonly TimeSpan DefaultGracefulTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Default time to wait on Windows for the OS to release the process handle
    /// after <see cref="Process.WaitForExitAsync"/> returns. On Windows, file and
    /// pipe handles owned by the child can outlive process exit by a short window
    /// and cause subsequent tests to fail with locked-file or port-in-use errors.
    /// </summary>
    public static readonly TimeSpan DefaultHandleReleasePollTimeout = TimeSpan.FromSeconds(2);

    private const int HandleReleasePollIntervalMs = 50;

    /// <summary>
    /// Asynchronously tear down a subprocess: kill its tree, wait for full exit,
    /// poll for handle release on Windows, unregister from
    /// <see cref="TestProcessRegistry"/>, and dispose the <see cref="Process"/>.
    /// Safe to call with a null process or one that has already exited.
    /// Never throws — diagnostic detail is logged when <paramref name="logger"/>
    /// is supplied.
    /// </summary>
    public static async Task TeardownAsync(
        Process? process,
        ILogger? logger = null,
        TimeSpan? gracefulTimeout = null,
        TimeSpan? handleReleasePollTimeout = null,
        CancellationToken cancellationToken = default)
    {
        if (process is null)
        {
            return;
        }

        var graceful = gracefulTimeout ?? DefaultGracefulTimeout;
        var handlePoll = handleReleasePollTimeout ?? DefaultHandleReleasePollTimeout;

        var capturedPid = TryCapturePid(process);

        try
        {
            if (!HasExitedSafe(process))
            {
                TryKillTree(process, logger, capturedPid);

                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(graceful);
                    await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    logger?.LogWarning(
                        "Subprocess pid {Pid} did not exit within {Timeout}; proceeding to handle-release poll.",
                        capturedPid, graceful);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "WaitForExitAsync failed for pid {Pid}.", capturedPid);
                }
            }

            if (capturedPid.HasValue && OperatingSystem.IsWindows())
            {
                await WaitForHandleReleaseAsync(capturedPid.Value, handlePoll, logger, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            SafeUnregisterAndDispose(process);
        }
    }

    /// <summary>
    /// Synchronous teardown for code paths that cannot be async (e.g. existing
    /// <see cref="IDisposable.Dispose"/> implementations). Mirrors the algorithm
    /// of <see cref="TeardownAsync"/> using blocking calls. Never throws.
    /// </summary>
    public static void Teardown(
        Process? process,
        ILogger? logger = null,
        TimeSpan? gracefulTimeout = null,
        TimeSpan? handleReleasePollTimeout = null)
    {
        if (process is null)
        {
            return;
        }

        var graceful = gracefulTimeout ?? DefaultGracefulTimeout;
        var handlePoll = handleReleasePollTimeout ?? DefaultHandleReleasePollTimeout;

        var capturedPid = TryCapturePid(process);

        try
        {
            if (!HasExitedSafe(process))
            {
                TryKillTree(process, logger, capturedPid);

                try
                {
                    process.WaitForExit((int)graceful.TotalMilliseconds);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "WaitForExit failed for pid {Pid}.", capturedPid);
                }
            }

            if (capturedPid.HasValue && OperatingSystem.IsWindows())
            {
                WaitForHandleReleaseSync(capturedPid.Value, handlePoll, logger);
            }
        }
        finally
        {
            SafeUnregisterAndDispose(process);
        }
    }

    private static int? TryCapturePid(Process process)
    {
        try
        {
            return process.Id;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasExitedSafe(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return true;
        }
    }

    private static void TryKillTree(Process process, ILogger? logger, int? capturedPid)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited between HasExited check and Kill — benign race.
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Process.Kill(entireProcessTree:true) failed for pid {Pid}.", capturedPid);
        }
    }

    private static async Task WaitForHandleReleaseAsync(
        int pid,
        TimeSpan timeout,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!ProcessHandleStillResolvable(pid))
            {
                return;
            }

            try
            {
                await Task.Delay(HandleReleasePollIntervalMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        logger?.LogWarning(
            "Subprocess pid {Pid} handle still resolvable after {Timeout}; continuing.",
            pid, timeout);
    }

    private static void WaitForHandleReleaseSync(int pid, TimeSpan timeout, ILogger? logger)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!ProcessHandleStillResolvable(pid))
            {
                return;
            }

            Thread.Sleep(HandleReleasePollIntervalMs);
        }

        logger?.LogWarning(
            "Subprocess pid {Pid} handle still resolvable after {Timeout}; continuing.",
            pid, timeout);
    }

    private static bool ProcessHandleStillResolvable(int pid)
    {
        try
        {
            using var probe = Process.GetProcessById(pid);
            // On Windows, GetProcessById succeeds while the kernel object lingers
            // even after process exit. Treat HasExited as "released enough" for
            // teardown purposes — the file/pipe handles drain alongside it.
            return !probe.HasExited;
        }
        catch (ArgumentException)
        {
            // Process no longer exists — fully released.
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch
        {
            // Any other failure: assume released so we do not stall teardown.
            return false;
        }
    }

    private static void SafeUnregisterAndDispose(Process process)
    {
        try
        {
            TestProcessRegistry.Unregister(process);
        }
        catch
        {
            // Tolerate races during AppDomain shutdown.
        }

        try
        {
            process.Dispose();
        }
        catch
        {
            // Tolerate double dispose.
        }
    }
}
