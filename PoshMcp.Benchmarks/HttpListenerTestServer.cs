using System.Net;
using System.Text;

namespace PoshMcp.Benchmarks;

/// <summary>
/// Local <see cref="HttpListener"/> bound to <c>127.0.0.1:0</c> (ephemeral port)
/// for the network-shaped benchmark scenario.
///
/// Per the experiment plan §4: bind to <c>127.0.0.1:0</c> to avoid the URL
/// ACL requirement Windows imposes on non-loopback bindings, and to work
/// portably on .NET 10 across platforms. The server responds after a
/// configurable delay so we can model Az/Graph-style latency without an
/// external dependency.
/// </summary>
public sealed class HttpListenerTestServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;

    /// <summary>
    /// Per-request response delay (default 500 ms — matches the Az/Graph
    /// shape called out in the experiment plan).
    /// </summary>
    public TimeSpan ResponseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Body returned for every request.
    /// </summary>
    public string ResponseBody { get; set; } = "{\"ok\":true}";

    /// <summary>
    /// The URL prefix the listener is bound to once <see cref="Start"/> has
    /// returned. Includes the ephemeral port assigned by the OS.
    /// </summary>
    public string Url { get; private set; } = string.Empty;

    public HttpListenerTestServer()
    {
        if (!HttpListener.IsSupported)
        {
            throw new PlatformNotSupportedException(
                "HttpListener is not supported on this platform.");
        }

        _listener = new HttpListener();
    }

    /// <summary>
    /// Bind to <c>127.0.0.1:0</c>, discover the assigned port, and begin
    /// accepting requests on a background task.
    /// </summary>
    public void Start()
    {
        // HttpListener does not expose "bind to ephemeral and ask which port
        // we got". Probe a free TCP port via the loopback adapter, then
        // hand that port to the listener.
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        Url = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(Url);
        _listener.Start();

        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException) { return; }
            catch (HttpListenerException) { return; }

            // Fire-and-forget per-connection handling so a slow response on
            // one request does not block the accept loop.
            _ = Task.Run(async () =>
            {
                try
                {
                    if (ResponseDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(ResponseDelay, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    var bytes = Encoding.UTF8.GetBytes(ResponseBody);
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream
                        .WriteAsync(bytes, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Benchmark harness — swallow per-response errors so a
                    // single failed write does not tear the listener down.
                }
                finally
                {
                    try { context.Response.Close(); } catch { /* ignore */ }
                }
            }, cancellationToken);
        }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { /* ignore */ }
        try
        {
            if (_listener.IsListening)
            {
                _listener.Stop();
            }
            _listener.Close();
        }
        catch { /* ignore */ }
        try { _acceptLoop?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        _cts.Dispose();
    }
}
