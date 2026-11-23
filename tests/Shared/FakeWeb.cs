using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using RecipeWarden.Infrastructure.Import;

namespace RecipeWarden.Tests.Shared;

/// <summary>
/// A pretend internet for SafeFetcher tests (AC-13, ADR 0024): answers DNS from a table, records which vetted
/// address the fetcher connected to, and serves canned HTTP/1.1 responses from a listener on loopback. The fetcher
/// itself never sees loopback: it only ever checks the public (or private) addresses in the table.
/// </summary>
internal sealed class FakeWeb : IDnsResolver, ISocketConnector, IAsyncDisposable
{
    public static readonly IPAddress PublicAddress = IPAddress.Parse("93.184.216.34");

    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentDictionary<string, Response> routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Task loop;

    public FakeWeb()
    {
        listener.Start();
        loop = Task.Run(AcceptLoopAsync);
    }

    public sealed record Response(int Status, string? ContentType, byte[] Body, IReadOnlyDictionary<string, string> Headers, bool NoLength);

    public sealed record Request(string Host, string PathAndQuery, IReadOnlyDictionary<string, string> Headers, string Body = "");

    public ConcurrentDictionary<string, IPAddress[]> Hosts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentQueue<IPAddress> Connections { get; } = new();

    public ConcurrentQueue<Request> Requests { get; } = new();

    public ConcurrentQueue<string> Lookups { get; } = new();

    /// <summary>When set, connections never complete (for timeouts).</summary>
    public bool Hang { get; set; }

    /// <summary>Adds a host that resolves to a public address.</summary>
    public FakeWeb Host(string host, params IPAddress[] addresses)
    {
        Hosts[host] = addresses.Length == 0 ? [PublicAddress] : addresses;
        return this;
    }

    public FakeWeb Serve(string url, int status, string? contentType, byte[] body, Dictionary<string, string>? headers = null, bool noLength = false)
    {
        var uri = new Uri(url);
        Hosts.TryAdd(uri.Host, [PublicAddress]);
        routes[uri.Host + uri.PathAndQuery] = new Response(status, contentType, body, headers ?? [], noLength);
        return this;
    }

    public FakeWeb Html(string url, string html) => Serve(url, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));

    public FakeWeb Redirect(string from, string to, int status = 302) =>
        Serve(from, status, null, [], new Dictionary<string, string> { ["Location"] = to });

    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        Lookups.Enqueue(host);
        return Task.FromResult(Hosts.TryGetValue(host, out var a) ? a : []);
    }

    public async ValueTask<Stream> ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        Connections.Enqueue(address);
        if (Hang)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, cancellationToken);
        return client.GetStream();
    }

    private async Task AcceptLoopAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var head = new StringBuilder();
                var buffer = new byte[1];
                while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal) && head.Length < 65536)
                {
                    if (await stream.ReadAsync(buffer, stop.Token) == 0)
                    {
                        return;
                    }

                    head.Append((char)buffer[0]);
                }

                var lines = head.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                var path = lines[0].Split(' ')[1];
                var headers = lines.Skip(1).Select(l => l.Split(':', 2)).Where(p => p.Length == 2)
                    .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);
                var host = headers.TryGetValue("Host", out var h) ? h.Split(':')[0] : string.Empty;
                var body = string.Empty;
                if (headers.TryGetValue("Content-Length", out var lengthText) && int.TryParse(lengthText, CultureInfo.InvariantCulture, out var length) && length is > 0 and < 4_000_000)
                {
                    var bytes = new byte[length];
                    await stream.ReadExactlyAsync(bytes, stop.Token);
                    body = Encoding.UTF8.GetString(bytes);
                }

                Requests.Enqueue(new Request(host, path, headers, body));

                var response = routes.TryGetValue(host + path, out var r) ? r : new Response(404, "text/plain", "missing"u8.ToArray(), new Dictionary<string, string>(), false);
                var sb = new StringBuilder();
                sb.Append(CultureInfo.InvariantCulture, $"HTTP/1.1 {response.Status} Status\r\nConnection: close\r\n");
                if (response.ContentType is not null)
                {
                    sb.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
                }

                if (!response.NoLength)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"Content-Length: {response.Body.Length}\r\n");
                }

                foreach (var (k, v) in response.Headers)
                {
                    sb.Append(k).Append(": ").Append(v).Append("\r\n");
                }

                sb.Append("\r\n");
                await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()), stop.Token);
                await stream.WriteAsync(response.Body, stop.Token);
                await stream.FlushAsync(stop.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // The fetcher may hang up early (too large, wrong type); that is expected.
            }
        }
    }

    private int disposed;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        await stop.CancelAsync();
        listener.Stop();
        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
        }

        stop.Dispose();
    }
}
