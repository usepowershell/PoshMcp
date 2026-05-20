using System.Threading;

namespace PoshMcp.Server.McpResources;

/// <summary>
/// Captures the eligibility-aware <see cref="NounRegistry"/> discovered for the
/// current async flow so CLI doctor can reuse the live noun-resource surface
/// without widening the discovery return contract.
/// </summary>
/// <remarks>
/// <para><c>McpToolSetupService.DiscoverToolsAsync</c> only returns the materialized
/// tool list, but doctor noun-resource reporting also needs the registry produced
/// from command metadata during discovery. This capture preserves that registry for
/// the current async flow until <c>DoctorService.BuildDoctorReportForCliAsync</c>
/// consumes it.</para>
/// <para>Callers should reset before discovery to avoid reusing a stale registry on
/// subsequent discovery attempts in the same async flow.</para>
/// </remarks>
internal static class DiscoveredNounRegistryCapture
{
    private static readonly AsyncLocal<NounRegistry?> _current = new();

    /// <summary>Reads the captured registry for the current async flow.</summary>
    public static NounRegistry? Current => _current.Value;

    /// <summary>Stores the discovered registry for the current async flow.</summary>
    public static void Set(NounRegistry? nounRegistry) => _current.Value = nounRegistry;

    /// <summary>Clears the captured registry for the current async flow.</summary>
    public static void Reset() => _current.Value = null;
}