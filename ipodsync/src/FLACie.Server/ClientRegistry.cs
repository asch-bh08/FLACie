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
    /// <summary>What the dashboard calls this client: "Edge on Windows" for a browser, "FLACie for Windows" for the desktop app's own embedded view.</summary>
    public string Label => ClientLabel.Of(Browser, Os, ClientLabel.InDesktopShell, IPAddress.TryParse(Ip, out var ip) && IPAddress.IsLoopback(ip));
}

/// <summary>The names shown for a client. The Jellyfin device name ("FLACie Web") and the client id (ipodplayer) never change; only what this server's own pages say.</summary>
public static class ClientLabel
{
    public const string DesktopApp = "FLACie for Windows";
    /// <summary>This server runs inside the Windows app (it was started with the app's process id).</summary>
    public static bool InDesktopShell => ChartsService.InDesktopShell;

    /// <summary>"Edge on Windows" for a browser. In the desktop app's copy of the server only the app's own WebView2 (Edge's engine, on this computer, reached over the loopback
    /// address: the server listens on nothing else there) is the app; any other browser keeps its own name.</summary>
    public static string Of(string browser, string os, bool desktopShell, bool loopback)
    {
        if (desktopShell && loopback && browser.Equals("Edge", StringComparison.OrdinalIgnoreCase) && os.Equals("Windows", StringComparison.OrdinalIgnoreCase)) return DesktopApp;
        return browser + (os.Length > 0 ? " on " + os : "");
    }

    /// <summary>The device name Jellyfin gives this server's own player ("FLACie Web") shown as the app's name inside the Windows app.</summary>
    public static string Device(string device, bool isSelf, bool desktopShell) => desktopShell && isSelf && device == "FLACie Web" ? DesktopApp : device;
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
