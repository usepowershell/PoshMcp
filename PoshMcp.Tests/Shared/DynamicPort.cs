using System.Net;
using System.Net.Sockets;

namespace PoshMcp.Tests.Shared;

/// <summary>
/// Helper for allocating a TCP port the OS considers free, for use by test
/// fixtures that must hand a numeric port to a child process before the
/// child's HTTP server has bound (e.g. <c>dotnet run -- serve --url
/// http://localhost:N</c>). Mirrors the canonical port-0 + read-back pattern
/// already used by <c>LoopbackHttpServer</c> in
/// <c>OutOfProcessHostConcurrencyTests</c>.
/// </summary>
/// <remarks>
/// <para>
/// Implementation: bind a <see cref="TcpListener"/> on
/// <see cref="IPAddress.Loopback"/> port 0, read the kernel-assigned port,
/// then stop the probe so the child can bind it. This satisfies FR-411 in
/// spec 009 (resource-heavy tests that bind a port must use a dynamically
/// allocated port — port 0 + read back actual port — not a hard-coded port
/// from a small range).
/// </para>
/// <para>
/// There is a small race window between probe close and child bind: another
/// process on the host could grab the same port. In practice this is far
/// more reliable than picking from a hand-picked range (the previous
/// pattern used <c>Random.Shared.Next(6100, 6900)</c>, an 800-port range
/// shared across all concurrent test runs on the machine, which produced
/// observable collisions). When a fixture owns the listener directly (e.g.
/// in-process <see cref="HttpListener"/> or <see cref="TcpListener"/>),
/// prefer to bind to port 0 in-place and read the bound endpoint —
/// <see cref="Allocate"/> is only required when the port number must be
/// passed by argument before the listener exists.
/// </para>
/// </remarks>
internal static class DynamicPort
{
    /// <summary>
    /// Returns a TCP port number the kernel selected as free, then released
    /// the probe socket so a caller (typically a child process) can bind it.
    /// </summary>
    public static int Allocate()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }
}
