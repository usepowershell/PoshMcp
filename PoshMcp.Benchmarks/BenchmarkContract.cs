using System.Reflection;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using PoshMcp.Benchmarks.Scenarios;

namespace PoshMcp.Benchmarks;

internal static class BenchmarkContract
{
    private static readonly string[] RequiredHttpSessionMethods =
    [
        nameof(HttpSessionBenchmark.FirstHttpSessionLatency),
        nameof(HttpSessionBenchmark.WarmSessionToolLatency),
        nameof(HttpSessionBenchmark.ConcurrentWarmSessionThroughput),
        nameof(HttpSessionBenchmark.BoundedCapacityRejection)
    ];

    public static void VerifyHttpSessionContract()
    {
        var discoveredMethods = typeof(HttpSessionBenchmark)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<BenchmarkAttribute>() is not null)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var expectedMethods = RequiredHttpSessionMethods.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        if (!discoveredMethods.SequenceEqual(expectedMethods, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"HTTP session benchmark contract mismatch. Expected: {string.Join(", ", expectedMethods)}. "
                + $"Discovered: {string.Join(", ", discoveredMethods)}.");
        }

        var capacityProperty = typeof(HttpSessionBenchmark).GetProperty(nameof(HttpSessionBenchmark.SessionRunspaceCapacity));
        if (capacityProperty?.GetCustomAttribute<ParamsAttribute>() is null)
        {
            throw new InvalidOperationException("HTTP session benchmark must expose SessionRunspaceCapacity as a BenchmarkDotNet parameter.");
        }

        using var rejectedResponse = JsonDocument.Parse("""{"result":{"isError":true}}""");
        using var topLevelErrorResponse = JsonDocument.Parse("""{"error":{"code":-32603}}""");
        using var successfulResponse = JsonDocument.Parse("""{"result":{"isError":false}}""");
        if (!BenchmarkMcpClient.IsMcpError(rejectedResponse.RootElement)
            || !BenchmarkMcpClient.IsMcpError(topLevelErrorResponse.RootElement)
            || BenchmarkMcpClient.IsMcpError(successfulResponse.RootElement))
        {
            throw new InvalidOperationException("Bounded-capacity MCP error validation is not configured correctly.");
        }

        foreach (var method in RequiredHttpSessionMethods)
        {
            Console.WriteLine($"HTTP session benchmark contract: {method}");
        }

        Console.WriteLine(
            $"Bounded capacity metadata: SessionRunspaceCapacity={HttpSessionBenchmark.DefaultSessionRunspaceCapacity}; "
            + "overflow MCP error (validated).");
    }
}
