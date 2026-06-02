using System;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Threading;

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
    private const int TestPortMin = 10000;
    private const int TestPortMax = 30000;
    private static int _nextUniquePort = InitializeStartingPort();

    private static int InitializeStartingPort()
    {
        var range = TestPortMax - TestPortMin + 1;
        var seed = unchecked((int)(DateTime.UtcNow.Ticks ^ Process.GetCurrentProcess().Id));
        var offset = Math.Abs(seed % range);
        return TestPortMin + offset - 1;
    }

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

    /// <summary>
    /// Returns a likely-unique loopback port for this process by walking a
    /// monotonic candidate sequence in the ephemeral range and probing each
    /// candidate before returning it.
    /// </summary>
    public static int AllocateUnique()
    {
        var candidateRange = TestPortMax - TestPortMin + 1;

        for (var i = 0; i < candidateRange; i++)
        {
            var candidate = Interlocked.Increment(ref _nextUniquePort);
            if (candidate > TestPortMax)
            {
                Interlocked.CompareExchange(ref _nextUniquePort, TestPortMin - 1, candidate);
                candidate = Interlocked.Increment(ref _nextUniquePort);
            }

            if (IsPortAvailable(candidate))
            {
                return candidate;
            }
        }

        return Allocate();
    }

    private static bool IsPortAvailable(int port)
    {
        var probe = new TcpListener(IPAddress.Loopback, port);
        try
        {
            probe.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            try
            {
                probe.Stop();
            }
            catch (SocketException)
            {
                // Ignore stop failures from partially-initialized probes.
            }
        }
    }
}
