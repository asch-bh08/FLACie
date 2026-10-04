using System.Collections.Concurrent;
using System.Net;
using FLACie.Core;

namespace FLACie.Server;

/// <summary>One open FLACie Web page (a browser tab): who is signed in there, what browser and computer it is, and its player. Jellyfin only sees one "FLACie Web" device per
/// server, so this is how the admin dashboard tells two browsers (or a PC and a laptop) apart.</summary>
public sealed class WebClient(string id, UserSession session, PlayerState player)
{
    public string Id { get; } = id;
    public UserSession Session { get; } = session;
    public PlayerState Player { get; } = player;
    public string Ip { get; set; } = "";
    public string Host { get; set; } = "";
    public string Browser { get; set; } = "Browser";
    public string Os { get; set; } = "";
    public string Ua { get; set; } = "";
    public string Screen { get; set; } = "";
    public string Language { get; set; } = "";
    public DateTime Since { get; } = DateTime.UtcNow;
    public string User => Session.DisplayName;
}

public sealed class ClientRegistry
{
    readonly ConcurrentDictionary<string, WebClient> clients = new();
    public IReadOnlyCollection<WebClient> All => clients.Values.OrderBy(c => c.Since).ToList();
    public void Add(WebClient c) { clients[c.Id] = c; _ = Task.Run(() => Resolve(c)); }
    public void Remove(string id) => clients.TryRemove(id, out _);

    // the computer's name, when the network can say (usually only on the home network)
    static async Task Resolve(WebClient c)
    {
        try
        {
            if (!IPAddress.TryParse(c.Ip, out var ip)) return;
            if (IPAddress.IsLoopback(ip)) { c.Host = Environment.MachineName + " (this computer)"; return; }
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var entry = await Dns.GetHostEntryAsync(ip).WaitAsync(cts.Token);
            c.Host = entry.HostName.Split('.')[0];
        }
        catch (Exception) { }
    }
}
