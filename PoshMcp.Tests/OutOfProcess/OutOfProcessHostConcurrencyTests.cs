using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PoshMcp.Server.PowerShell.OutOfProcess;
using Xunit;

namespace PoshMcp.Tests.OutOfProcess;

/// <summary>
/// Concurrency tests for <see cref="OutOfProcessHost.SendRequestAsync{T}"/>.
/// Regression coverage for issue #203: parallel invokes against a single host
/// must not corrupt the request/response correlation map and must not surface
/// serialization errors caused by objects whose CLR type shadows a base-class
/// member of the same name (e.g. BasicHtmlWebResponseObject's 'Content').
/// </summary>
[Trait("Category", "OutOfProcess")]
public class OutOfProcessHostConcurrencyTests
{
    [Fact]
    public async Task SendRequestAsync_ConcurrentCallers_AllResponsesCorrelate()
    {
        string pwshPath;
        try
        {
            pwshPath = OutOfProcessCommandExecutor.ResolvePwshPath();
        }
        catch (FileNotFoundException)
        {
            return; // pwsh not installed — acceptable.
        }

        var scriptPath = await ResolveOopHostScriptForTestAsync();
        if (scriptPath is null)
        {
            return;
        }

        await using var host = new OutOfProcessHost(
            pwshPath, scriptPath,
            NullLogger<OutOfProcessHost>.Instance,
            TimeSpan.FromSeconds(30));

        await host.StartAsync(CancellationToken.None);

        // Fire N parallel ping requests. Each call generates a unique request
        // id internally and registers its own TCS in _pending; a race in the
        // request-frame build path or in the correlation map would surface
        // as a thrown exception, a TimeoutException, or a swapped response.
        const int Concurrency = 20;
        var tasks = Enumerable.Range(0, Concurrency).Select(async _ =>
        {
            var result = await host.SendRequestAsync<JsonElement>(
                "ping", null, CancellationToken.None);
            return result.TryGetProperty("status", out var s) ? s.GetString() : null;
        }).ToArray();

        var statuses = await Task.WhenAll(tasks);

        Assert.Equal(Concurrency, statuses.Length);
        Assert.All(statuses, s => Assert.Equal("ok", s));
    }

    [Fact]
    public async Task InvokeAsync_ConcurrentInvokeWebRequest_DoesNotThrowDuplicateKeyError()
    {
        // Regression for issue #203: BasicHtmlWebResponseObject (returned by
        // Invoke-WebRequest -UseBasicParsing) shadows the base-class
        // WebResponseObject.Content property. Default `ConvertTo-Json -Depth 4`
        // throws `An item with the same key has already been added. Key: Content`,
        // which surfaced to the C# client as
        // `OOP error: An item with the same key has already been added. Key: Content`.
        // The fix wraps ConvertTo-Json with a Select-Object * fallback in
        // oop-host.ps1 (and in the user script of oop-host-pool.ps1).
        string pwshPath;
        try
        {
            pwshPath = OutOfProcessCommandExecutor.ResolvePwshPath();
        }
        catch (FileNotFoundException)
        {
            return;
        }

        var scriptPath = await ResolveOopHostScriptForTestAsync();
        if (scriptPath is null)
        {
            return;
        }

        if (!HttpListener.IsSupported)
        {
            return;
        }

        using var server = new LoopbackHttpServer();
        server.Start();

        await using var host = new OutOfProcessHost(
            pwshPath, scriptPath,
            NullLogger<OutOfProcessHost>.Instance,
            TimeSpan.FromSeconds(30));

        await host.StartAsync(CancellationToken.None);

        // Fire N parallel Invoke-WebRequest invokes — the deterministic
        // repro from issue #203 / WarmInvokeThroughputBenchmark.
        const int Concurrency = 10;
        var tasks = Enumerable.Range(0, Concurrency).Select(async _ =>
        {
            return await host.SendRequestAsync<JsonElement>(
                "invoke",
                new
                {
                    command = "Invoke-WebRequest",
                    parameters = new Dictionary<string, object?>
                    {
                        ["Uri"] = server.Url,
                        ["UseBasicParsing"] = true,
                    }
                },
                CancellationToken.None);
        }).ToArray();

        // Pre-fix this throws System.InvalidOperationException with message
        // "OOP error: An item with the same key has already been added. Key: Content".
        // Post-fix all N calls must complete without exception.
        var results = await Task.WhenAll(tasks);

        Assert.Equal(Concurrency, results.Length);
        foreach (var r in results)
        {
            Assert.True(
                r.TryGetProperty("output", out var output),
                $"invoke response missing 'output': {r.GetRawText()}");
            var s = output.GetString();
            Assert.False(string.IsNullOrEmpty(s),
                "output should be non-empty JSON.");
        }
    }

    private static async Task<string?> ResolveOopHostScriptForTestAsync()
    {
        var overridePath = Environment.GetEnvironmentVariable("POSHMCP_OOP_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return overridePath;
        }

        var serverAssembly = typeof(OutOfProcessHost).Assembly;
        var resourceName = Array.Find(
            serverAssembly.GetManifestResourceNames(),
            name => name.EndsWith("oop-host.ps1", StringComparison.OrdinalIgnoreCase));

        if (resourceName is not null)
        {
            using var stream = serverAssembly.GetManifestResourceStream(resourceName);
            if (stream is not null)
            {
                var bytes = new byte[stream.Length];
                await stream.ReadExactlyAsync(bytes);
                var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                var dir = Path.Combine(Path.GetTempPath(), "poshmcp-tests");
                var path = Path.Combine(dir, "oop-host.ps1");
                Directory.CreateDirectory(dir);
                if (!File.Exists(path) || ContentHash(path) != hash)
                {
                    await File.WriteAllBytesAsync(path, bytes);
                }
                return path;
            }
        }

        var basePath = Path.Combine(AppContext.BaseDirectory, "PowerShell", "OutOfProcess", "oop-host.ps1");
        return File.Exists(basePath) ? basePath : null;
    }

    private static string ContentHash(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        return Convert.ToHexStringLower(SHA256.HashData(fs));
    }

    /// <summary>
    /// Minimal HttpListener bound to <c>127.0.0.1</c> on an ephemeral port.
    /// Mirrors the bench's HttpListenerTestServer pattern. Returns a fixed
    /// JSON body so the test does not depend on outbound network access.
    /// </summary>
    private sealed class LoopbackHttpServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private Task? _acceptLoop;

        public string Url { get; private set; } = string.Empty;

        public void Start()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            Url = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();

            _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException) { return; }
                catch (HttpListenerException) { return; }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var bytes = Encoding.UTF8.GetBytes("{\"ok\":true}");
                        context.Response.StatusCode = 200;
                        context.Response.ContentType = "application/json";
                        context.Response.ContentLength64 = bytes.Length;
                        await context.Response.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
                        context.Response.OutputStream.Close();
                    }
                    catch
                    {
                        try { context.Response.Abort(); } catch { /* best effort */ }
                    }
                }, ct);
            }
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { /* best effort */ }
            try { _listener.Stop(); } catch { /* best effort */ }
            try { _listener.Close(); } catch { /* best effort */ }
            _cts.Dispose();
        }
    }
}
