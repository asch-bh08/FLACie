using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace FLACie.Server.Dj;

/// <summary>
/// The DJ's spoken clips: made in the background by <see cref="CloudflareVoice"/> and kept in <c>{data}/dj-clips</c> as &lt;id&gt;.mp3, where the id is a hash of the voice
/// and the text (the same line in the same voice is made once). The oldest are deleted past <see cref="MaxFiles"/>. A clip that failed is remembered for a while,
/// so asking again gives no clip rather than another failing call.
/// </summary>
public sealed class DjClips
{
    public const int MaxFiles = 3000;
    static readonly TimeSpan RememberFailure = TimeSpan.FromMinutes(10);

    readonly string dir; readonly CloudflareVoice voice; readonly ILogger<DjClips> log; readonly Func<DateTime> clock;
    readonly object gate = new();
    readonly Dictionary<string, Task<bool>> jobs = new();
    readonly ConcurrentDictionary<string, DateTime> failed = new();

    public DjClips(DataPaths paths, CloudflareVoice voice, ILogger<DjClips> log, Func<DateTime>? clock = null)
    {
        dir = Path.Combine(paths.Root, "dj-clips"); this.voice = voice; this.log = log; this.clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>The clip's id: 32 hex characters of SHA-256 over the voice and the text.</summary>
    public static string IdOf(string voiceName, string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CloudflareVoice.NormVoice(voiceName) + "\n" + text)))[..32].ToLowerInvariant();

    /// <summary>A well-formed id (what the clip address may carry).</summary>
    public static bool IsId(string id) => id.Length == 32 && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static string UrlOf(string id) => $"/api/dj/clip/{id}.mp3";

    string FileOf(string id) => Path.Combine(dir, id + ".mp3");

    bool Failed(string id) => failed.TryGetValue(id, out var at) && clock() - at < RememberFailure;

    /// <summary>
    /// The id of the clip for [text] in the saved voice, starting it in the background if it is not made yet; null when no clip can be had: the voice is not set up,
    /// the line would go over today's cap, it failed a moment ago, or Cloudflare failed on any line lately (<see cref="CloudflareVoice.Failing"/>: a refused token
    /// or an outage fails the next lines too, so they go to the backup voice at once instead of handing out a clip that will never come).
    /// </summary>
    public string? Request(string text)
    {
        var v = voice.Current.Voice;
        var id = IdOf(v, text);
        if (Failed(id)) return null;
        var path = FileOf(id);
        if (File.Exists(path)) { try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch (Exception) { } return id; }
        lock (gate)
        {
            if (jobs.ContainsKey(id)) return id;
            if (!voice.Ready || voice.Failing || !voice.CanAfford(v, text)) return null;
            jobs[id] = Task.Run(() => MakeAsync(id, v, text));
            return id;
        }
    }

    async Task<bool> MakeAsync(string id, string v, string text)
    {
        try
        {
            var mp3 = await voice.SynthesizeAsync(text, v);
            if (mp3 is null) { failed[id] = clock(); return false; }
            Write(id, mp3);
            return true;
        }
        catch (Exception e) { failed[id] = clock(); log.LogWarning("DJ clip not saved: {Kind}", e.GetType().Name); return false; }
        finally { lock (gate) jobs.Remove(id); }
    }

    void Write(string id, byte[] mp3)
    {
        Directory.CreateDirectory(dir);
        var tmp = FileOf(id) + "." + Guid.NewGuid().ToString("N")[..8] + ".part";
        File.WriteAllBytes(tmp, mp3);
        File.Move(tmp, FileOf(id), true);
        failed.TryRemove(id, out _);
        Prune();
    }

    /// <summary>Deletes the oldest clips past <see cref="MaxFiles"/> (only this folder's own .mp3 files).</summary>
    public void Prune()
    {
        try
        {
            var files = new DirectoryInfo(dir).GetFiles("*.mp3");
            if (files.Length <= MaxFiles) return;
            foreach (var f in files.OrderBy(f => f.LastWriteTimeUtc).Take(files.Length - MaxFiles)) { try { f.Delete(); } catch (Exception) { } }
        }
        catch (Exception) { }
    }

    /// <summary>The clip's file, waiting up to [wait] while it is being made; null when it failed, is unknown or was pruned.</summary>
    public async Task<string?> WaitAsync(string id, TimeSpan wait, CancellationToken ct = default)
    {
        if (!IsId(id)) return null;
        var path = FileOf(id);
        if (File.Exists(path)) return path;
        Task<bool>? job;
        lock (gate) jobs.TryGetValue(id, out job);
        if (job is null) return null;
        try { await job.WaitAsync(wait, ct); } catch (TimeoutException) { return null; }
        return File.Exists(path) ? path : null;
    }

    /// <summary>Makes [text] now, whether or not it was made before (the admin's Test button): the clip's id, or null and why not.</summary>
    public async Task<(string? Id, string Message)> MakeNowAsync(string text, CancellationToken ct = default)
    {
        if (!voice.Ready) return (null, "Add the Cloudflare account id and API token first.");
        var v = voice.Current.Voice;
        if (!voice.CanAfford(v, text)) return (null, "Today's DJ share of the free Cloudflare allowance is used up (the daily cap).");
        var mp3 = await voice.SynthesizeAsync(text, v, ct);
        if (mp3 is null) return (null, voice.LastResult);
        var id = IdOf(v, text);
        try { Write(id, mp3); } catch (Exception e) { return (null, "The clip could not be saved (" + e.GetType().Name + ")"); }
        return (id, "Made a clip");
    }
}
