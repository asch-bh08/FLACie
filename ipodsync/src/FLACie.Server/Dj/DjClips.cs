using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FLACie.Core;

namespace FLACie.Server.Dj;

/// <summary>
/// The DJ's spoken clips: made in the background by <see cref="CloudflareVoice"/> and kept in <c>{data}/dj-clips</c> as &lt;id&gt;.mp3, where the id is a hash of the voice
/// and the exact text, so the same words in the same voice are never made twice, whoever they are for. <c>index.json</c> beside them says when each was last used;
/// past <see cref="MaxBytes"/> the least recently used go. A pooled line (<see cref="DjPool"/>) is its parts' clips, each kept on its own and joined into one MP3:
/// a part with a first name in it is kept for that person only, everything else is shared by the family. A clip that failed is remembered for a while,
/// so asking again gives no clip rather than another failing call.
/// </summary>
public sealed class DjClips
{
    /// <summary>The most the clips may take on disk (about 11,000 Aura lines).</summary>
    public const long MaxBytes = 200L * 1024 * 1024;
    /// <summary>How long a line whose clip failed (after the voice's own retry) is not asked for again: only that line, and not for long.</summary>
    static readonly TimeSpan RememberFailure = TimeSpan.FromSeconds(30);

    readonly string dir, indexFile; readonly CloudflareVoice voice; readonly ILogger<DjClips> log; readonly Func<DateTime> clock;
    readonly object gate = new();
    readonly Dictionary<string, Task<bool>> jobs = new();
    readonly ConcurrentDictionary<string, DateTime> failed = new();
    /// <summary>When each clip was last used (handed out or made), by id; written to index.json.</summary>
    readonly ConcurrentDictionary<string, DateTime> used = new();
    DateTime savedAt = DateTime.MinValue;

    public DjClips(DataPaths paths, CloudflareVoice voice, ILogger<DjClips> log, Func<DateTime>? clock = null)
    {
        dir = Path.Combine(paths.Root, "dj-clips"); indexFile = Path.Combine(dir, "index.json");
        this.voice = voice; this.log = log; this.clock = clock ?? (() => DateTime.UtcNow);
        try
        {
            if (File.Exists(indexFile) && JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(indexFile)) is { } d)
                foreach (var (k, v) in d) if (IsId(k)) used[k] = v;
        }
        catch (Exception) { }
    }

    /// <summary>The clip's id: 32 hex characters of SHA-256 over the voice and the text.</summary>
    public static string IdOf(string voiceName, string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CloudflareVoice.NormVoice(voiceName) + "\n" + text)))[..32].ToLowerInvariant();

    /// <summary>A part's id: shared parts are plain clips (the same as a whole line of the same words); a personal part is kept apart for [owner].</summary>
    public static string PartId(string voiceName, DjPart p, string owner) => p.Personal ? IdOf(voiceName, "\u0001" + owner + "\n" + p.Text) : IdOf(voiceName, p.Text);

    /// <summary>A pooled line's id: its parts' ids in order.</summary>
    public static string PooledId(string voiceName, IEnumerable<string> partIds) => IdOf(voiceName, "\u0002pooled\n" + string.Join("\n", partIds));

    /// <summary>A well-formed id (what the clip address may carry).</summary>
    public static bool IsId(string id) => id.Length == 32 && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static string UrlOf(string id) => $"/api/dj/clip/{id}.mp3";

    string FileOf(string id) => Path.Combine(dir, id + ".mp3");
    /// <summary>Where the clip of [text] in the saved voice is (or would be) kept.</summary>
    public string PathOf(string text) => FileOf(IdOf(voice.Current.Voice, text));

    bool Failed(string id) => failed.TryGetValue(id, out var at) && clock() - at < RememberFailure;

    /// <summary>The clip is on disk: it counts as used now (so it is the last to go).</summary>
    bool Have(string id)
    {
        if (!File.Exists(FileOf(id))) return false;
        used[id] = clock();
        if (clock() - savedAt > TimeSpan.FromMinutes(1)) SaveIndex();
        return true;
    }

    /// <summary>
    /// The id of the clip for [text] in the saved voice, starting it in the background if it is not made yet; null when no clip can be had: the voice is not set up,
    /// the line would go over today's cap, it failed a moment ago, or Cloudflare failed on several lines in a row just now (<see cref="CloudflareVoice.Failing"/>: a refused
    /// token or an outage, for a short rest, instead of handing out clips that will never come). One failure only costs that line.
    /// </summary>
    public string? Request(string text)
    {
        var v = voice.Current.Voice;
        // always the saved voice: near the cap a line is not made in a cheaper one (two voices in one session sounded like two DJs)
        var id = IdOf(v, text);
        if (Failed(id)) return null;
        if (Have(id)) return id;
        lock (gate)
        {
            if (jobs.ContainsKey(id)) return id;
            if (!voice.Ready || voice.Failing || !voice.CanAfford(v, text)) return null;
            jobs[id] = Task.Run(() => MakeAsync(id, v, text));
            return id;
        }
    }

    /// <summary>
    /// The id of the pooled line made of [parts] (in the saved Aura voice), joining the parts' clips in the background and making only the parts not made yet;
    /// null when there is no Aura voice (MeloTTS lines are cheap enough to say whole), the missing parts do not fit under today's cap, or it failed lately.
    /// </summary>
    public string? RequestPooled(IReadOnlyList<DjPart> parts, string owner)
    {
        var v = voice.Current.Voice;
        if (v == CloudflareVoice.MeloTts || parts.Count == 0) return null;
        var ids = parts.Select(p => PartId(v, p, owner)).ToList();
        var id = PooledId(v, ids);
        if (Failed(id)) return null;
        if (Have(id)) return id;
        lock (gate)
        {
            if (jobs.ContainsKey(id)) return id;
            if (!voice.Ready || voice.Failing) return null;
            var missing = parts.Where((p, i) => !File.Exists(FileOf(ids[i])) && !jobs.ContainsKey(ids[i])).Sum(p => CloudflareVoice.EstimateNeurons(v, p.Text));
            if (!voice.CanSpend(missing)) return null;
            jobs[id] = Task.Run(() => MakePooledAsync(id, v, parts, ids));
            return id;
        }
    }

    /// <summary>Why <see cref="Request"/> gave no clip for [text], for the "line not said" log.</summary>
    public string WhyNot(string text)
    {
        var v = voice.Current.Voice;
        return Failed(IdOf(v, text)) ? "the voice failed on this line a moment ago" : voice.WhyNot(v, text) ?? "no clip";
    }

    /// <summary>The clip of [text] in the saved voice if it is already made (no call, whatever the cap or Cloudflare), else null.</summary>
    public string? Cached(string text)
    {
        var id = IdOf(voice.Current.Voice, text);
        return Have(id) ? id : null;
    }

    /// <summary>The pooled line of [parts] in the saved voice if every part is already made in that voice (joined here, with no call), else null.
    /// Parts made in another voice have other ids, so they are never joined in.</summary>
    public string? CachedPooled(IReadOnlyList<DjPart> parts, string owner)
    {
        var v = voice.Current.Voice;
        if (v == CloudflareVoice.MeloTts || parts.Count == 0) return null;
        var ids = parts.Select(p => PartId(v, p, owner)).ToList();
        var id = PooledId(v, ids);
        if (Have(id)) return id;
        if (Failed(id) || ids.Any(i => !File.Exists(FileOf(i)))) return null;
        lock (gate)
        {
            if (!jobs.ContainsKey(id)) jobs[id] = Task.Run(() => MakePooledAsync(id, v, parts, ids));
            return id;
        }
    }

    /// <summary>The cost of [parts] in the saved voice today: only the parts not made yet.</summary>
    public double CostOf(IReadOnlyList<DjPart> parts, string owner)
    {
        var v = voice.Current.Voice;
        return parts.Where(p => !File.Exists(FileOf(PartId(v, p, owner)))).Sum(p => CloudflareVoice.EstimateNeurons(v, p.Text));
    }

    /// <summary>Makes, once per voice, the shared phrases of pooled lines that are not made yet (while they fit under today's cap). Returns how many were made.</summary>
    public async Task<int> WarmAsync(IEnumerable<string> phrases)
    {
        var v = voice.Current.Voice;
        if (v == CloudflareVoice.MeloTts || !voice.Ready) return 0;
        var made = 0;
        foreach (var p in phrases.Distinct())
        {
            var id = IdOf(v, p);
            if (File.Exists(FileOf(id)) || Failed(id)) continue;
            if (voice.Failing || !voice.CanAfford(v, p)) break;
            if (await Ensure(id, v, p)) made++;
        }
        return made;
    }

    /// <summary>The clip [id] of [text] on disk: there already, being made (waits for it), or made now.</summary>
    Task<bool> Ensure(string id, string v, string text)
    {
        if (File.Exists(FileOf(id))) return Task.FromResult(true);
        lock (gate)
        {
            if (jobs.TryGetValue(id, out var job)) return job;
            var t = Task.Run(() => MakeAsync(id, v, text));
            jobs[id] = t;
            return t;
        }
    }

    async Task<bool> MakeAsync(string id, string v, string text)
    {
        try
        {
            var mp3 = await voice.SynthesizeAsync(text, v);
            if (mp3 is null) { failed[id] = clock(); log.LogWarning("DJ line not said: no clip, the voice failed ({Why})", voice.LastResult); return false; }
            Write(id, mp3);
            return true;
        }
        catch (Exception e) { failed[id] = clock(); log.LogWarning("DJ clip not saved: {Kind}", e.GetType().Name); return false; }
        finally { lock (gate) jobs.Remove(id); }
    }

    async Task<bool> MakePooledAsync(string id, string v, IReadOnlyList<DjPart> parts, IReadOnlyList<string> ids)
    {
        try
        {
            using var all = new MemoryStream();
            for (var i = 0; i < parts.Count; i++)
            {
                if (!await Ensure(ids[i], v, parts[i].Text)) { failed[id] = clock(); return false; }
                var bytes = await File.ReadAllBytesAsync(FileOf(ids[i]));
                all.Write(Frames(bytes));
                used[ids[i]] = clock();
            }
            Write(id, all.ToArray());
            return true;
        }
        catch (Exception e) { failed[id] = clock(); log.LogWarning("DJ pooled clip not saved: {Kind}", e.GetType().Name); return false; }
        finally { lock (gate) jobs.Remove(id); }
    }

    /// <summary>An MP3's audio frames only: a leading ID3v2 tag and a trailing ID3v1 tag are cut, so clips joined end to end play as one.</summary>
    public static ReadOnlySpan<byte> Frames(byte[] mp3)
    {
        var start = 0; var end = mp3.Length;
        if (end >= 10 && mp3[0] == 'I' && mp3[1] == 'D' && mp3[2] == '3')
        {
            var size = (mp3[6] & 0x7f) << 21 | (mp3[7] & 0x7f) << 14 | (mp3[8] & 0x7f) << 7 | (mp3[9] & 0x7f);
            start = Math.Min(end, 10 + size + ((mp3[5] & 0x10) != 0 ? 10 : 0));
        }
        if (end - start >= 128 && mp3[end - 128] == 'T' && mp3[end - 127] == 'A' && mp3[end - 126] == 'G') end -= 128;
        return mp3.AsSpan(start, end - start);
    }

    void Write(string id, byte[] mp3)
    {
        Directory.CreateDirectory(dir);
        var tmp = FileOf(id) + "." + Guid.NewGuid().ToString("N")[..8] + ".part";
        File.WriteAllBytes(tmp, mp3);
        File.Move(tmp, FileOf(id), true);
        failed.TryRemove(id, out _);
        used[id] = clock();
        Prune();
    }

    /// <summary>Deletes the least recently used clips while the folder's .mp3 files take more than [maxBytes] (only this folder's own .mp3 files), then writes the index.</summary>
    public void Prune(long maxBytes = MaxBytes)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            var files = new DirectoryInfo(dir).GetFiles("*.mp3");
            var total = files.Sum(f => f.Length);
            if (total > maxBytes)
            {
                DateTime When(FileInfo f) => used.TryGetValue(Path.GetFileNameWithoutExtension(f.Name), out var u) ? u : f.LastWriteTimeUtc;
                foreach (var f in files.OrderBy(When))
                {
                    if (total <= maxBytes) break;
                    lock (gate) if (jobs.ContainsKey(Path.GetFileNameWithoutExtension(f.Name))) continue;
                    var size = f.Length;
                    try { f.Delete(); total -= size; used.TryRemove(Path.GetFileNameWithoutExtension(f.Name), out _); } catch (Exception) { }
                }
            }
            SaveIndex();
        }
        catch (Exception) { }
    }

    void SaveIndex()
    {
        try
        {
            Directory.CreateDirectory(dir);
            savedAt = clock();
            var tmp = indexFile + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(used.Where(x => File.Exists(FileOf(x.Key))).ToDictionary(x => x.Key, x => x.Value)));
            File.Move(tmp, indexFile, true);
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
    public async Task<(string? Id, string Message)> MakeNowAsync(string text, CancellationToken ct = default, string? voiceName = null)
    {
        if (!voice.Ready) return (null, "Add the Cloudflare account id and API token first.");
        var v = CloudflareVoice.NormVoice(voiceName ?? voice.Current.Voice);
        if (!voice.CanAfford(v, text)) return (null, "Today's DJ share of the free Cloudflare allowance is used up (the daily cap).");
        var mp3 = await voice.SynthesizeAsync(text, v, ct);
        if (mp3 is null) return (null, voice.LastResult);
        var id = IdOf(v, text);
        try { Write(id, mp3); } catch (Exception e) { return (null, "The clip could not be saved (" + e.GetType().Name + ")"); }
        return (id, "Made a clip");
    }
}
