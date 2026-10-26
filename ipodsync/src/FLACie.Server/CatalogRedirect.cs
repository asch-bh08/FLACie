namespace FLACie.Server;

/// <summary>
/// A test seam, off unless <c>FLACIE_CATALOG_REDIRECT</c> names a loopback address: the public music catalogs (Deezer, iTunes, MusicBrainz, ListenBrainz) and the DJ's voice (Cloudflare) are asked there
/// instead, as <c>&lt;address&gt;/&lt;their host&gt;&lt;path and query&gt;</c>, so a test can give search, Autoplay suggestions and charts fixed answers without the internet.
/// Nothing else is rewritten (the download services, Jellyfin and the user's own addresses are never touched).
/// </summary>
public sealed class CatalogRedirectHandler(Uri origin) : DelegatingHandler
{
    static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase)
        { "api.deezer.com", "itunes.apple.com", "musicbrainz.org", "coverartarchive.org", "labs.api.listenbrainz.org", "s3.amazonaws.com", "api.cloudflare.com" };   // s3.amazonaws.com: where Alexa's signing certificate is fetched (see AlexaGuards); api.cloudflare.com: the DJ's voice

    /// <summary>The loopback address from the setting, or null when it is unset or is not a loopback http address (then nothing is redirected).</summary>
    public static Uri? Parse(string? setting) =>
        Uri.TryCreate(setting?.Trim().TrimEnd('/'), UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttp && u.IsLoopback ? u : null;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri is { } r && Hosts.Contains(r.Host))
            request.RequestUri = new Uri(origin, "/" + r.Host + r.PathAndQuery);
        return base.SendAsync(request, ct);
    }
}
