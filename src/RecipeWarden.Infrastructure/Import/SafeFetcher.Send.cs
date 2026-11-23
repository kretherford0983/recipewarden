using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using RecipeWarden.Core.Security;

namespace RecipeWarden.Infrastructure.Import;

/// <summary>An outbound call other than an import: the health webhook (FR-OPS-05) or the AI parser (M8).</summary>
/// <param name="Permit">SEC-44: the one Admin-configured endpoint on the home network, matched exactly.</param>
/// <param name="Headers">Extra headers, e.g. an API key. Never logged (SEC-45, SEC-72).</param>
public sealed record OutboundRequest(
    Uri Uri,
    HttpMethod Method,
    byte[]? Body,
    string? ContentType,
    IReadOnlyDictionary<string, string> Headers,
    PermittedEndpoint? Permit,
    TimeSpan Timeout,
    long MaxResponseBytes);

public sealed record OutboundResponse(int Status, string? ContentType, byte[]? Body, FetchFailure Failure, string? Error)
{
    public bool Ok => Failure == FetchFailure.None && Status is >= 200 and < 300;

    public static OutboundResponse Fail(FetchFailure failure, string error, int status = 0) => new(status, null, null, failure, error);
}

public sealed partial class SafeFetcher
{
    /// <summary>
    /// SEC-41, SEC-44 (ADR 0041): one request to a checked endpoint, connected to the vetted address only; no
    /// redirects, a timeout and a response cap. Private addresses are refused unless the request's permitted endpoint
    /// matches the URI exactly.
    /// </summary>
    public async Task<OutboundResponse> SendAsync(OutboundRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var check = OutboundEndpointRules.Check(request.Uri, request.Permit);
        if (!check.IsAllowed)
        {
            LogRefused(_logger, Redact(request.Uri.ToString()), check.Error!);
            return OutboundResponse.Fail(check.Error == UrlRules.PrivateAddress ? FetchFailure.Blocked : FetchFailure.Refused, check.Error!);
        }

        var uri = check.Uri!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);
        try
        {
            var vetted = await VetAsync(uri, allowPrivate: request.Permit is not null && request.Permit.Matches(uri), timeout.Token).ConfigureAwait(false);
            if (vetted.Error is not null)
            {
                return OutboundResponse.Fail(vetted.Error.Failure, vetted.Error.Error!);
            }

            using var message = new HttpRequestMessage(request.Method, uri);
            message.Options.Set(VettedAddress, vetted.Address!);
            message.Headers.TryAddWithoutValidation("User-Agent", "RecipeWarden");
            foreach (var (name, value) in request.Headers)
            {
                message.Headers.TryAddWithoutValidation(name, value);
            }

            if (request.Body is not null)
            {
                message.Content = new ByteArrayContent(request.Body);
                message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType ?? "application/octet-stream");
            }

            using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (status is >= 300 and < 400)
            {
                LogStatus(_logger, uri.IdnHost, status);
                return OutboundResponse.Fail(FetchFailure.Refused, "The endpoint redirected; give its final address.", status);
            }

            if (response.Content.Headers.ContentLength > request.MaxResponseBytes)
            {
                return OutboundResponse.Fail(FetchFailure.TooBig, "The answer was too large.", status);
            }

            var body = await ReadCappedAsync(response.Content, request.MaxResponseBytes, timeout.Token).ConfigureAwait(false);
            if (body is null)
            {
                return OutboundResponse.Fail(FetchFailure.TooBig, "The answer was too large.", status);
            }

            if (status is < 200 or >= 300)
            {
                LogStatus(_logger, uri.IdnHost, status);
                return new OutboundResponse(status, response.Content.Headers.ContentType?.MediaType, body, FetchFailure.Network,
                    $"The endpoint answered with status {status}.");
            }

            return new OutboundResponse(status, response.Content.Headers.ContentType?.MediaType, body, FetchFailure.None, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailed(_logger, uri.IdnHost, "timeout");
            return OutboundResponse.Fail(FetchFailure.Timeout, "The endpoint took too long to answer.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or SocketException or AuthenticationException)
        {
            LogFailedException(_logger, uri.IdnHost, ex);
            return OutboundResponse.Fail(FetchFailure.Network, "The endpoint could not be reached.");
        }
    }
}
