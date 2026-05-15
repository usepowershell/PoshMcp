using System.Threading;

namespace PoshMcp.Server.PowerShell.OutOfProcess;

/// <summary>
/// Spec 011 FR-263-2 / FR-263-10: AsyncLocal capture for the OOP host's
/// <see cref="RemoteModuleImportsPayload"/> from the most recent discovery
/// in the current async flow.
/// </summary>
/// <remarks>
/// <para>The capture exists because <c>McpToolSetupService.DiscoverToolsAsync</c>
/// disposes its OOP executor lease before returning, so the executor's
/// <see cref="ICommandExecutor.LastModuleImports"/> is unavailable to the
/// caller (DoctorService) by the time it builds the doctor moduleImports
/// section. We stash the payload here on the way out and read it back in
/// <c>BuildDoctorReportForCliAsync</c>.</para>
/// <para>Scoped per async flow via <see cref="AsyncLocal{T}"/>; concurrent
/// doctor invocations on different tasks see their own values. CLI doctor
/// invocations are one-shot, so cross-invocation leaks are not a concern,
/// but callers SHOULD invoke <see cref="Reset"/> before discovery to avoid
/// stale captures from previous discovery attempts in the same flow.</para>
/// </remarks>
internal static class OopModuleImportsCapture
{
    private static readonly AsyncLocal<RemoteModuleImportsPayload?> _current = new();

    /// <summary>Reads the current async flow's captured payload, or <c>null</c>
    /// when none has been set in this flow.</summary>
    public static RemoteModuleImportsPayload? Current => _current.Value;

    /// <summary>Stores the payload for the current async flow.</summary>
    public static void Set(RemoteModuleImportsPayload? payload) => _current.Value = payload;

    /// <summary>Clears the current async flow's captured payload.</summary>
    public static void Reset() => _current.Value = null;
}
