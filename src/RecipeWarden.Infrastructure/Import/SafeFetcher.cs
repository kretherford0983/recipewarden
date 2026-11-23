using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using RecipeWarden.Core.Security;

namespace RecipeWarden.Infrastructure.Import;

/// <summary>Import settings, bound from the "Import" config section (ADR 0024).</summary>
public sealed class ImportOptions
{
    public const string SectionName = "Import";

    /// <summary>ADR 0024: a current desktop-browser User-Agent (owner's choice).</summary>
    public string UserAgent { get; set; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36";

    /// <summary>SEC-42: 10 s per fetch, redirects included.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>SEC-42: 5 MB for pages.</summary>
    public long MaxPageBytes { get; set; } = 5L * 1024 * 1024;

    /// <summary>SEC-42: 10 MB for images.</summary>
    public long MaxImageBytes { get; set; } = 10L * 1024 * 1024;
}

/// <summary>What is being fetched; decides the allowed content types and size (SEC-42).</summary>
public enum FetchPurpose
{
    Page,
    Image,
}

/// <summary>Why a fetch did not return a body.</summary>
public enum FetchFailure
{
    None,

    /// <summary>The address breaks SEC-40 (scheme, port, credentials, literal private IP).</summary>
    Refused,

    /// <summary>The host resolves to a private or special address (SEC-41); nothing was connected.</summary>
    Blocked,

    /// <summary>The site answered 401, 403, 429 or 503, often a bot check (ADR 0024 offers paste).</summary>
    SiteRefused,
    NotFound,
    TooBig,
    WrongType,
    Timeout,
    TooManyRedirects,
    Network,
}

public sealed record FetchResult(Uri? FinalUri, string? ContentType, string? Charset, byte[]? Body, FetchFailure Failure, string? Error)
{
    public bool Ok => Failure == FetchFailure.None && Body is not null;

    /// <summary>ADR 0024: failures where pasting the page is the way forward.</summary>
    public bool OfferPaste => Failure is FetchFailure.SiteRefused or FetchFailure.Timeout or FetchFailure.Network or FetchFailure.WrongType;

    public static FetchResult Fail(FetchFailure failure, string error) => new(null, null, null, null, failure, error);
}

/// <summary>Resolves host names. A seam so tests can answer with any address.</summary>
public interface IDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

/// <summary>Opens the TCP connection to an address the fetcher has already vetted. A seam for tests.</summary>
public interface ISocketConnector
{
    ValueTask<Stream> ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken);
}

internal sealed class SystemDnsResolver : IDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}

internal sealed class TcpSocketConnector : ISocketConnector
{
    public async ValueTask<Stream> ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

/// <summary>
/// SEC-40 to SEC-43, AC-13, ADR 0024: the only way the app fetches anything from the internet (URL import and
/// copied images; the AI parser joins in M8). For each hop it checks the address (<see cref="UrlRules"/>), resolves
/// DNS itself, refuses the request if any resolved address is private or special, and then connects to that vetted
/// address only (so DNS cannot change between the check and the connection). Redirects are followed by hand, at
/// most 3, each checked again. No proxy, no cookies; 10 s in all; 5 MB for pages, 10 MB for images; only the
/// allowed content types. Every refusal is logged.
/// </summary>
public sealed partial class SafeFetcher : IDisposable
{
    public const string SiteRefusedMessage = "The site refused the request (it may block automated access). Paste the page text instead.";
    public const string NotFoundMessage = "The page was not found";
    public const string TooBigMessage = "The page is too large to import";
    public const string WrongTypeMessage = "The address is not a web page";
    public const string WrongImageTypeMessage = "The image is not a JPEG, PNG or WebP";
    public const string TimeoutMessage = "The site took too long to answer";
    public const string NetworkMessage = "The site could not be reached";
    public const string UnknownHostMessage = "That site could not be found";

    private static readonly HttpRequestOptionsKey<IPAddress> VettedAddress = new("RecipeWarden.VettedAddress");
    private static readonly string[] PageTypes = ["text/html", "application/xhtml+xml", "application/ld+json"];
    private static readonly string[] ImageTypes = ["image/jpeg", "image/jpg", "image/pjpeg", "image/png", "image/webp"];

    private readonly HttpClient _client;
    private readonly IDnsResolver _dns;
    private readonly ImportOptions _options;
    private readonly ILogger<SafeFetcher> _logger;

    public SafeFetcher(ImportOptions options, IDnsResolver dns, ISocketConnector connector, ILogger<SafeFetcher> logger)
    {
        ArgumentNullException.ThrowIfNull(connector);
        _options = options;
        _dns = dns;
        _logger = logger;
#pragma warning disable CA2000 // the client owns and disposes the handler
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
            ConnectTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
            MaxResponseHeadersLength = 64,
            ConnectCallback = async (context, ct) =>
            {
                // Only ever connect to the address that was vetted for this request.
                if (!context.InitialRequestMessage.Options.TryGetValue(VettedAddress, out var address))
                {
                    throw new HttpRequestException("No vetted address for this request");
                }

                return await connector.ConnectAsync(address, context.DnsEndPoint.Port, ct).ConfigureAwait(false);
            },
        };
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
#pragma warning restore CA2000
    }

    public async Task<FetchResult> FetchAsync(string url, FetchPurpose purpose, CancellationToken cancellationToken = default)
    {
        var check = UrlRules.Check(url);
        if (!check.IsAllowed)
        {
            LogRefused(_logger, Redact(url), check.Error!);
            return FetchResult.Fail(check.Error == UrlRules.PrivateAddress ? FetchFailure.Blocked : FetchFailure.Refused, check.Error!);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        try
        {
            return await FetchCheckedAsync(check.Uri!, purpose, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailed(_logger, check.Uri!.IdnHost, "timeout");
            return FetchResult.Fail(FetchFailure.Timeout, TimeoutMessage);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or SocketException or AuthenticationException)
        {
            LogFailedException(_logger, check.Uri!.IdnHost, ex);
            return FetchResult.Fail(FetchFailure.Network, NetworkMessage);
        }
    }

    private async Task<FetchResult> FetchCheckedAsync(Uri uri, FetchPurpose purpose, CancellationToken ct)
    {
        for (var hop = 0; ; hop++)
        {
            var vetted = await VetAsync(uri, ct).ConfigureAwait(false);
            if (vetted.Error is not null)
            {
                return vetted.Error;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Options.Set(VettedAddress, vetted.Address!);
            request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", purpose == FetchPurpose.Page
                ? "text/html,application/xhtml+xml;q=0.9,*/*;q=0.8"
                : "image/webp,image/png,image/jpeg;q=0.9,*/*;q=0.5");
            request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");

            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (status is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                if (hop >= UrlRules.MaxRedirects)
                {
                    LogRefused(_logger, uri.IdnHost, UrlRules.TooManyRedirects);
                    return FetchResult.Fail(FetchFailure.TooManyRedirects, UrlRules.TooManyRedirects);
                }

                var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                var nextCheck = UrlRules.Check(next);
                if (!nextCheck.IsAllowed)
                {
                    LogRefused(_logger, Redact(next.ToString()), "redirect: " + nextCheck.Error);
                    return FetchResult.Fail(nextCheck.Error == UrlRules.PrivateAddress ? FetchFailure.Blocked : FetchFailure.Refused, nextCheck.Error!);
                }

                uri = nextCheck.Uri!;
                continue;
            }

            if (status is 401 or 403 or 429 or 503)
            {
                LogStatus(_logger, uri.IdnHost, status);
                return FetchResult.Fail(FetchFailure.SiteRefused, SiteRefusedMessage);
            }

            if (status is 404 or 410)
            {
                return FetchResult.Fail(FetchFailure.NotFound, NotFoundMessage);
            }

            if (status is < 200 or >= 300)
            {
                LogStatus(_logger, uri.IdnHost, status);
                return FetchResult.Fail(FetchFailure.Network, NetworkMessage);
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            var allowed = purpose == FetchPurpose.Page ? PageTypes : ImageTypes;
            if (mediaType is null || !allowed.Contains(mediaType))
            {
                LogRefused(_logger, uri.IdnHost, "content type " + (mediaType ?? "none"));
                return FetchResult.Fail(FetchFailure.WrongType, purpose == FetchPurpose.Page ? WrongTypeMessage : WrongImageTypeMessage);
            }

            var max = purpose == FetchPurpose.Page ? _options.MaxPageBytes : _options.MaxImageBytes;
            if (response.Content.Headers.ContentLength > max)
            {
                LogRefused(_logger, uri.IdnHost, "too large");
                return FetchResult.Fail(FetchFailure.TooBig, TooBigMessage);
            }

            var body = await ReadCappedAsync(response.Content, max, ct).ConfigureAwait(false);
            if (body is null)
            {
                LogRefused(_logger, uri.IdnHost, "too large");
                return FetchResult.Fail(FetchFailure.TooBig, TooBigMessage);
            }

            return new FetchResult(uri, mediaType, response.Content.Headers.ContentType?.CharSet, body, FetchFailure.None, null);
        }
    }

    private async Task<(IPAddress? Address, FetchResult? Error)> VetAsync(Uri uri, CancellationToken ct)
    {
        var host = uri.IdnHost.Trim('[', ']');
        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await _dns.ResolveAsync(host, ct).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                addresses = [];
            }
        }

        if (addresses.Length == 0)
        {
            return (null, FetchResult.Fail(FetchFailure.Network, UnknownHostMessage));
        }

        // Refuse when any address is private, so a name cannot mix a public and a private answer.
        if (addresses.Any(UrlRules.IsBlocked))
        {
            LogRefused(_logger, host, UrlRules.PrivateAddress);
            return (null, FetchResult.Fail(FetchFailure.Blocked, UrlRules.PrivateAddress));
        }

        return (addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).First(), null);
    }

    private static async Task<byte[]?> ReadCappedAsync(HttpContent content, long max, CancellationToken ct)
    {
        var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > max)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
    }

    /// <summary>Logs the host and path only; query strings can carry tokens (SEC-72).</summary>
    private static string Redact(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Scheme + "://" + u.Authority.Split('@')[^1] + u.AbsolutePath : "(not an address)";

    public void Dispose() => _client.Dispose();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Import fetch refused for {Target}: {Reason}")]
    private static partial void LogRefused(ILogger logger, string target, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Import fetch from {Host} failed: {Reason}")]
    private static partial void LogFailed(ILogger logger, string host, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Import fetch from {Host} failed with status {Status}")]
    private static partial void LogStatus(ILogger logger, string host, int status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Import fetch from {Host} failed")]
    private static partial void LogFailedException(ILogger logger, string host, Exception exception);
}
