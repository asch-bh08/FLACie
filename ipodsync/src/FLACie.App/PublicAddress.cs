using System.Net;
using System.Text.Json;
using FLACie.Core;
using Microsoft.Extensions.Configuration;

namespace FLACie.Server;

/// <summary>
/// The one address the phone uses to reach this FLACie Web (downloads, seek-bar shapes, share links, the DJ), saved into each person's profile as
/// <c>services.flacieweb.url</c>. Owner, 9 Nov: it used to be whatever address the web page was last opened at (the server's own, a second route through a proxy,
/// a LAN address), so the phone's address flipped between them and a LAN or temporary one would cut the phone off away from home. Now it is, in order:
/// <c>FLACIE_PUBLIC_URL</c>; the address set in Server admin; else the first tailnet address the page was opened at (a <c>*.ts.net</c> name or a 100.64/10
/// address), kept from then on. A page opened at any other address never changes it. Kept in public-address.json.
/// </summary>
public sealed class PublicAddress
{
    public const string FileName = "public-address.json";

    sealed class Saved
    {
        /// <summary>Set in Server admin; null when not set.</summary>
        public string? Manual { get; set; }
        /// <summary>The first tailnet address the page was opened at.</summary>
        public string? Seen { get; set; }
    }

    readonly string file; readonly IConfiguration conf; readonly object gate = new();
    Saved saved = new();

    public PublicAddress(DataPaths paths, IConfiguration conf)
    {
        this.conf = conf;
        file = Path.Combine(paths.Root, FileName);
        try { if (File.Exists(file)) saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(file)) ?? new(); } catch (Exception) { }
    }

    string? Env => Normalise(conf["FLACIE_PUBLIC_URL"]);
    /// <summary>The address comes from FLACIE_PUBLIC_URL (Server admin can't change it then).</summary>
    public bool FromEnvironment => Env is not null;

    /// <summary>The phone's address for this server, or null while none is known.</summary>
    public string? Current { get { lock (gate) return Env ?? saved.Manual ?? saved.Seen; } }

    /// <summary>Where <see cref="Current"/> comes from, in words for Server admin and Settings.</summary>
    public string Source { get { lock (gate) return Env is not null ? "from FLACIE_PUBLIC_URL" : saved.Manual is not null ? "set in Server admin" : saved.Seen is not null ? "the first tailnet address this page was opened at" : ""; } }

    /// <summary>The page was opened at [pageOrigin]: the first tailnet address is kept; returns the address to hand to the phone (null: none yet).</summary>
    public string? Use(string pageOrigin)
    {
        lock (gate)
        {
            if (Env is null && saved.Manual is null && saved.Seen is null && Normalise(pageOrigin) is { } o && IsTailnet(new Uri(o)))
            {
                saved.Seen = o; Save();
            }
        }
        return Current;
    }

    /// <summary>Sets the address in Server admin (an http or https address); empty clears it, back to the first tailnet address seen. False when not an address.</summary>
    public bool Set(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) { lock (gate) { saved.Manual = null; Save(); } return true; }
        if (Normalise(url) is not { } n) return false;
        lock (gate) { saved.Manual = n; Save(); }
        return true;
    }

    /// <summary>An http(s) address as scheme://host[:port], no path; null when it isn't one.</summary>
    public static string? Normalise(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https") || u.Host.Length == 0) return null;
        return u.GetLeftPart(UriPartial.Authority);
    }

    /// <summary>A Tailscale address: a MagicDNS name (*.ts.net) or one in 100.64.0.0/10.</summary>
    public static bool IsTailnet(Uri u)
    {
        if (u.Host.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(u.Host, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 100 && b[1] >= 64 && b[1] <= 127;
    }

    void Save()
    {
        try { File.WriteAllText(file, JsonSerializer.Serialize(saved)); } catch (Exception) { }
    }
}
