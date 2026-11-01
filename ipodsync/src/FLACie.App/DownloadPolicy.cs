using System.Text.Json;
using FLACie.Core;
using Microsoft.Extensions.Configuration;

namespace FLACie.Server;

/// <summary>
/// Whether this server downloads music at all, and who may use "Download all" and playlist imports. This is the one place the answer comes from:
/// <see cref="DownloadManager"/> checks it before anything is fetched (so Search, charts, imports, Autoplay, Alexa and the DJ all stop together),
/// and the pages ask it before they show a download button, a "set up downloads" hint, "Download all" or the import page.
/// <para>
/// A fresh install starts with downloads off: FLACie is a music player first, and downloading is something the person who runs the server chooses
/// to switch on in the Dashboard. An install that has downloaded before (its download log has something in it) counts as on, so a server that was
/// already fetching music carries on after the update with nothing to do. The answer is written down the first time, so it stays put afterwards.
/// <c>FLACIE_DOWNLOADS=on</c> or <c>off</c> overrides the saved switch, for someone who sets their server up from a compose file.
/// </para>
/// </summary>
public sealed class DownloadPolicy
{
    public const string FileName = "download-policy.json";

    sealed class Saved
    {
        public bool Allowed { get; set; }
        /// <summary>People who aren't administrators may also use "Download all" on a shared link or a playlist, and import playlist files.</summary>
        public bool EveryoneBulk { get; set; }
    }

    readonly string file; readonly IConfiguration conf; readonly object gate = new();
    Saved saved;

    public DownloadPolicy(DataPaths paths, IConfiguration conf)
    {
        this.conf = conf;
        file = Path.Combine(paths.Root, FileName);
        Saved? s = null;
        try { if (File.Exists(file)) s = JsonSerializer.Deserialize<Saved>(File.ReadAllText(file)); } catch (Exception) { }
        if (s is null) { s = new Saved { Allowed = HasDownloadedBefore(paths.Root) }; saved = s; Save(); }
        saved = s;
    }

    /// <summary>The server's download log has at least one entry: this install was downloading before the switch existed.</summary>
    static bool HasDownloadedBefore(string root)
    {
        try
        {
            var dir = Path.Combine(root, "downloads");
            return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.jsonl").Any(f => new FileInfo(f).Length > 0);
        }
        catch (Exception) { return false; }
    }

    /// <summary>What FLACIE_DOWNLOADS says (on/off, true/false, 1/0, yes/no); null when it is not set or not understood.</summary>
    bool? Host => (conf["FLACIE_DOWNLOADS"] ?? "").Trim().ToLowerInvariant() switch
    {
        "on" or "true" or "1" or "yes" => true,
        "off" or "false" or "0" or "no" => false,
        _ => null,
    };

    /// <summary>The switch is set by FLACIE_DOWNLOADS on this server, so the Dashboard shows it but can't change it.</summary>
    public bool SetByHost => Host is not null;

    /// <summary>This server downloads music: download buttons show, and charts, imports, Autoplay, Alexa and the DJ may fetch.</summary>
    public bool Allowed { get { lock (gate) return Host ?? saved.Allowed; } }

    /// <summary>"Download all" and imports are open to everyone signed in, not only administrators.</summary>
    public bool EveryoneBulk { get { lock (gate) return saved.EveryoneBulk; } }

    /// <summary>Downloads are allowed on this server and this person has download services set up.</summary>
    public bool Ready(UserSession s) => Allowed && s.Services.Any;

    /// <summary>This person may use "Download all" (a shared link, a playlist) and import playlist files: administrators always, others when the switch lets everyone.</summary>
    public bool MayBulk(UserSession s) => Allowed && (EveryoneBulk || AdminAccess.Is(s, conf));

    public void SetAllowed(bool on) { lock (gate) { saved.Allowed = on; Save(); } }
    public void SetEveryoneBulk(bool on) { lock (gate) { saved.EveryoneBulk = on; Save(); } }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!); var tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, file, true);
        }
        catch (Exception) { }
    }
}
