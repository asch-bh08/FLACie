namespace FLACie.Server;

/// <summary>
/// Sends requests for a service's public address to the address this server can reach it at directly, like FLACIE_JELLYFIN_INTERNAL_URL does for Jellyfin.
/// FLACIE_SERVICE_ROUTES is a comma separated list of <c>public=&gt;internal</c> pairs, for example
/// <c>https://files.example.com/filemove=&gt;http://127.0.0.1:8090</c>. The profile (and so the phone app) keeps the public address; only this server's
/// own requests are redirected, so a DNS or funnel problem on the public name cannot stop downloads that run on the same machine as the service.
/// </summary>
public sealed class ServiceRouteHandler(IReadOnlyList<(string Public, string Internal)> routes) : DelegatingHandler
{
    public static List<(string Public, string Internal)> Parse(string? setting)
    {
        var list = new List<(string, string)>();
        foreach (var part in (setting ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var i = part.IndexOf("=>", StringComparison.Ordinal);
            if (i <= 0) continue;
            var pub = part[..i].Trim().TrimEnd('/'); var inner = part[(i + 2)..].Trim().TrimEnd('/');
            if (pub.Length > 0 && inner.Length > 0) list.Add((pub, inner));
        }
        return list;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri is { } u)
        {
            var abs = u.AbsoluteUri;
            foreach (var (pub, inner) in routes)
                if (abs.StartsWith(pub, StringComparison.OrdinalIgnoreCase) && (abs.Length == pub.Length || abs[pub.Length] is '/' or '?'))
                { request.RequestUri = new Uri(inner + abs[pub.Length..]); break; }
        }
        return base.SendAsync(request, ct);
    }
}
