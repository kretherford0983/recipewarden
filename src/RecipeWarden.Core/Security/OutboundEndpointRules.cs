namespace RecipeWarden.Core.Security;

/// <summary>An exact scheme, host and port an Admin allowed on the home network (SEC-44).</summary>
public sealed record PermittedEndpoint(string Scheme, string Host, int Port)
{
    public bool Matches(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IsAbsoluteUri
            && string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.IdnHost.Trim('[', ']'), Host, StringComparison.OrdinalIgnoreCase)
            && uri.Port == Port;
    }

    public override string ToString() => $"{Scheme}://{Host}:{Port}";
}

/// <summary>
/// SEC-44 (ADR 0041): outbound calls other than imports — the health webhook (FR-OPS-05) and the AI parser (M8). An
/// endpoint is checked like an import address (<see cref="UrlRules"/>), except that one Admin-configured endpoint on
/// the home network may be reached at that exact scheme, host and port only. Nothing else on the LAN opens up.
/// </summary>
public static class OutboundEndpointRules
{
    public const string NotHttp = "Use an http:// or https:// address.";

    /// <summary>Checks <paramref name="uri"/>; the permitted endpoint, when it matches exactly, skips the port and private-address rules.</summary>
    public static UrlCheck Check(Uri uri, PermittedEndpoint? permitted)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (permitted is not null && permitted.Matches(uri))
        {
            return string.IsNullOrEmpty(uri.UserInfo) ? new UrlCheck(uri, null) : new UrlCheck(null, UrlRules.HasCredentials);
        }

        return UrlRules.Check(uri);
    }

    /// <summary>
    /// Reads an endpoint an Admin typed. Returns the URI and, when <paramref name="allowLan"/> is set and the address
    /// would otherwise be refused (a LAN host or a non-standard port), the exact endpoint to permit.
    /// </summary>
    public static (Uri? Uri, PermittedEndpoint? Permit, string? Error) Parse(string? text, bool allowLan)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > UrlRules.MaxUrlLength || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.Host))
        {
            return (null, null, UrlRules.NotAnAddress);
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return (null, null, NotHttp);
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return (null, null, UrlRules.HasCredentials);
        }

        var plain = UrlRules.Check(uri);
        if (plain.IsAllowed)
        {
            return (uri, null, null);
        }

        return allowLan
            ? (uri, new PermittedEndpoint(uri.Scheme, uri.IdnHost.Trim('[', ']'), uri.Port), null)
            : (null, null, plain.Error);
    }
}
