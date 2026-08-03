namespace PoshMcp.Server.PowerShell.Pool;

/// <summary>
/// Lifecycle states for a warm-worker runspace in the HTTP pool.
/// </summary>
/// <remarks>
/// Valid transitions:
/// <list type="table">
/// <item><term>Creating → Warm</term><description>Startup script completed without error.</description></item>
/// <item><term>Creating → Evicted</term><description>Startup script threw or timed out; worker never enters the pool.</description></item>
/// <item><term>Warm → Leased</term><description>Worker acquired by <c>IRunspacePool.AcquireAsync</c>.</description></item>
/// <item><term>Warm → Evicted</term><description>Idle-TTL sweep or configuration reload.</description></item>
/// <item><term>Leased → Resetting</term><description>Command completed (success or non-fatal PS error); reset protocol begins.</description></item>
/// <item><term>Leased → Evicted</term><description>Command timeout / <c>Stop()</c> failure / runspace <c>Broken</c>.</description></item>
/// <item><term>Resetting → Warm</term><description>Reset sequence completed cleanly; worker returns to pool queue.</description></item>
/// <item><term>Resetting → Evicted</term><description>Reset encountered a <c>Broken</c> runspace or threw an exception.</description></item>
/// <item><term>Evicted → Disposed</term><description>Resources released after eviction is confirmed.</description></item>
/// </list>
/// </remarks>
public enum RunspaceWorkerState
{
    Creating,
    Warm,
    Leased,
    Resetting,
    Evicted,
    Disposed
}
