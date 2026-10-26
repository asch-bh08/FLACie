using System.Text.Json.Nodes;
using FLACie.Core;

namespace FLACie.Server.Dj;

/// <summary>The DJ's JSON (docs/spec/ai-dj.md, HTTP): camelCase names, enums as lower-case strings, every field type-checked (a wrong type is the caller's mistake, 400).</summary>
public static class DjJson
{
    static readonly string[] Switches = ["enabled", "phone", "auto", "web", "windows", "backup", "talkAfterSkips"];

    public static string StyleName(DjStyle s) => s switch { DjStyle.Chill => "chill", DjStyle.Hype => "hype", _ => "classic" };
    public static DjStyle? StyleOf(string? s) => (s ?? "").Trim().ToLowerInvariant() switch { "classic" => DjStyle.Classic, "chill" => DjStyle.Chill, "hype" => DjStyle.Hype, _ => null };
    public static string BondName(DjBond b) => b switch { DjBond.Loved => "loved", DjBond.NewFind => "new", _ => "unknown" };

    /// <summary>GET /api/dj/settings: <c>{ enabled, chatty: "rare"|"often", phone, auto, web, windows, backup, talkAfterSkips, style: "classic"|"chill"|"hype", playlists: { id: bool }, voiceReady }</c>.</summary>
    public static JsonObject Settings(DjSettings s, bool voiceReady)
    {
        var lists = new JsonObject();
        foreach (var (k, v) in s.Playlists) lists[k] = v;
        return new JsonObject
        {
            ["enabled"] = s.Enabled, ["chatty"] = s.Chatty == DjChatty.Rare ? "rare" : "often", ["phone"] = s.Phone, ["auto"] = s.Auto, ["web"] = s.Web, ["windows"] = s.Windows,
            ["backup"] = s.Backup, ["talkAfterSkips"] = s.TalkAfterSkips, ["style"] = StyleName(s.Style), ["playlists"] = lists, ["voiceReady"] = voiceReady,
        };
    }

    static bool IsBool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out _);
    static bool IsString(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out _);

    /// <summary>
    /// POST /api/dj/settings: a partial update with the GET names; <c>{ "playlist": { "id": "...", "on": true|false|null } }</c> sets or clears one playlist and
    /// <c>playlists</c> replaces them all. Everything is checked before anything changes. Returns the error, or null when [s] was updated.
    /// </summary>
    public static string? Apply(JsonObject body, DjSettings s)
    {
        foreach (var k in Switches) if (body.ContainsKey(k) && !IsBool(body[k])) return $"\"{k}\" must be true or false.";
        DjChatty? chatty = null;
        if (body.ContainsKey("chatty"))
        {
            chatty = body["chatty"] is JsonValue cv && cv.TryGetValue<string>(out var c) ? c.Trim().ToLowerInvariant() switch { "rare" => DjChatty.Rare, "often" => DjChatty.Often, _ => null } : null;
            if (chatty is null) return "\"chatty\" must be \"rare\" or \"often\".";
        }
        DjStyle? style = null;
        if (body.ContainsKey("style"))
        {
            style = body["style"] is JsonValue stv && stv.TryGetValue<string>(out var sn) ? StyleOf(sn) : null;
            if (style is null) return "\"style\" must be \"classic\", \"chill\" or \"hype\".";
        }
        Dictionary<string, bool>? all = null;
        if (body.ContainsKey("playlists"))
        {
            if (body["playlists"] is not JsonObject po || po.Count > 1000 || po.Any(kv => kv.Key.Trim().Length is 0 or > 200 || !IsBool(kv.Value))) return "\"playlists\" must be an object of playlist id: true or false.";
            // ids that are the same once trimmed ("a" and " a ") are one playlist: the last one wins
            all = new();
            foreach (var (k, v) in po) all[k.Trim()] = v!.GetValue<bool>();
        }
        (string Id, bool? On)? one = null;
        if (body.ContainsKey("playlist"))
        {
            if (body["playlist"] is not JsonObject p || p["id"] is not JsonValue iv || !iv.TryGetValue<string>(out var id) || id.Trim().Length is 0 or > 200
                || (p["on"] is not null && !IsBool(p["on"])))
                return "\"playlist\" must be { \"id\": \"...\", \"on\": true, false or null }.";
            one = (id.Trim(), p["on"] is JsonValue ov ? ov.GetValue<bool>() : null);
        }
        bool B(string k, bool was) => body.ContainsKey(k) ? body[k]!.GetValue<bool>() : was;
        s.Enabled = B("enabled", s.Enabled); s.Phone = B("phone", s.Phone); s.Auto = B("auto", s.Auto); s.Web = B("web", s.Web); s.Windows = B("windows", s.Windows); s.Backup = B("backup", s.Backup); s.TalkAfterSkips = B("talkAfterSkips", s.TalkAfterSkips);
        if (chatty is { } ch) s.Chatty = ch;
        if (style is { } sy) s.Style = sy;
        if (all is not null) s.Playlists = all;
        if (one is { } o) { if (o.On is { } on) s.Playlists[o.Id] = on; else s.Playlists.Remove(o.Id); }
        return null;
    }

    sealed class Reader(JsonObject o, string where)
    {
        public string? Error;
        void Bad(string k, string what) => Error ??= $"\"{where}{k}\" must be {what}.";
        public string Str(string k, int max)
        {
            var n = o[k];
            if (n is null) return "";
            if (n is not JsonValue v || !v.TryGetValue<string>(out var s)) { Bad(k, "a string"); return ""; }
            s = s.Trim();
            return s.Length > max ? s[..max] : s;
        }
        public string? StrOrNull(string k, int max) => o[k] is null ? null : Str(k, max) is { Length: > 0 } s ? s : null;
        public int Int(string k) { var n = o[k]; if (n is null) return 0; if (n is JsonValue v && v.TryGetValue<int>(out var i)) return i; Bad(k, "a whole number"); return 0; }
        public int? IntOrNull(string k) => o[k] is null ? null : Int(k);
        public long Long(string k) { var n = o[k]; if (n is null) return 0; if (n is JsonValue v && v.TryGetValue<long>(out var i)) return i; Bad(k, "a whole number"); return 0; }
        public bool Bool(string k, bool dflt) { var n = o[k]; if (n is null) return dflt; if (n is JsonValue v && v.TryGetValue<bool>(out var b)) return b; Bad(k, "true or false"); return dflt; }
        public bool? BoolOrNull(string k) => o[k] is null ? null : Bool(k, false);
    }

    static DjSong? Song(JsonNode? n, string where, ref string? error)
    {
        if (n is not JsonObject o) { error ??= $"\"{where}\" must be an object."; return null; }
        var r = new Reader(o, where + ".");
        var s = new DjSong(r.Str("title", 300), r.Str("artist", 300), r.Str("album", 300), r.Str("albumArtist", 300), r.Int("year"), r.Str("genre", 100),
            Math.Max(0, r.Long("durationMs")), r.Str("path", 2000), r.StrOrNull("jellyfinId", 100), r.Int("trackNo"), r.Int("discNo"));
        error ??= r.Error;
        return s;
    }

    /// <summary>POST /api/dj/next's body (camelCase of <see cref="DjRequest"/>). Returns the error, or null with [req] set.</summary>
    public static string? Request(JsonObject body, out DjRequest? req)
    {
        req = null;
        DjSurface? surface = body["surface"] is JsonValue sv && sv.TryGetValue<string>(out var sn)
            ? sn.Trim().ToLowerInvariant() switch { "phone" => DjSurface.Phone, "auto" => DjSurface.Auto, "web" => DjSurface.Web, "windows" => DjSurface.Windows, _ => null } : null;
        if (surface is null) return "\"surface\" must be \"phone\", \"auto\", \"web\" or \"windows\".";
        string? error = null;
        var next = Song(body["next"], "next", ref error);
        var prev = body["prev"] is null ? null : Song(body["prev"], "prev", ref error);
        var r = new Reader(body, "");
        var built = new DjRequest(surface.Value, next ?? new DjSong("", ""), prev, r.Bool("prevPlayedOut", true), r.Bool("skippedRecently", false),
            r.StrOrNull("playlistId", 200), r.BoolOrNull("queueDj"), r.IntOrNull("localHour"), r.IntOrNull("weekday"), r.StrOrNull("why", 500),
            r.Str("bond", 20).ToLowerInvariant() switch { "loved" => DjBond.Loved, "new" => DjBond.NewFind, _ => DjBond.Unknown });
        error ??= r.Error;
        if (error is not null) return error;
        req = built;
        return null;
    }

    /// <summary>POST /api/dj/next's answer: <c>{ "speak": false }</c> or <c>{ "speak": true, text, caption, clip, backup, introMs }</c> (introMs: null when not known).</summary>
    public static JsonObject Cue(DjCue? c) => c is null
        ? new JsonObject { ["speak"] = false }
        : new JsonObject { ["speak"] = true, ["text"] = c.Text, ["caption"] = c.Caption, ["clip"] = c.ClipUrl, ["backup"] = c.Backup, ["introMs"] = c.IntroMs };

    // ---- the other way round: the Windows app asking the family's FLACie Web (HomeDj) ----

    static JsonObject SongBody(DjSong t) => new()
    {
        ["title"] = t.Title, ["artist"] = t.Artist, ["album"] = t.Album, ["albumArtist"] = t.AlbumArtist, ["year"] = t.Year, ["genre"] = t.Genre,
        ["durationMs"] = t.DurationMs, ["path"] = t.Path, ["jellyfinId"] = t.JellyfinId, ["trackNo"] = t.TrackNo, ["discNo"] = t.DiscNo,
    };

    /// <summary>POST /api/dj/next's body for [r]: what <see cref="Request"/> reads back.</summary>
    public static JsonObject RequestBody(DjRequest r) => new()
    {
        ["surface"] = r.Surface.ToString().ToLowerInvariant(), ["next"] = SongBody(r.Next), ["prev"] = r.Prev is null ? null : SongBody(r.Prev),
        ["prevPlayedOut"] = r.PrevPlayedOut, ["skippedRecently"] = r.SkippedRecently, ["playlistId"] = r.PlaylistId, ["queueDj"] = r.QueueDj,
        ["localHour"] = r.LocalHour, ["weekday"] = r.Weekday, ["why"] = r.Why, ["bond"] = BondName(r.Bond),
    };

    /// <summary>Reads POST /api/dj/next's answer (what <see cref="Cue"/> writes): null for <c>"speak": false</c>, or for an answer that says nothing.</summary>
    public static DjCue? ReadCue(JsonObject o)
    {
        if (o["speak"] is not JsonValue sv || !sv.TryGetValue<bool>(out var speak) || !speak) return null;
        string S(string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var x) ? x : "";
        var text = S("text");
        if (text.Trim().Length == 0) return null;
        return new DjCue(text, S("caption"), S("clip") is { Length: > 0 } clip ? clip : null, o["backup"] is JsonValue b && b.TryGetValue<bool>(out var backup) && backup,
            o["introMs"] is JsonValue iv && iv.TryGetValue<int>(out var intro) && intro >= 0 ? intro : null);
    }

    /// <summary>Reads GET /api/dj/settings' answer (what <see cref="Settings"/> writes): null when it isn't one.</summary>
    public static DjSettings? ReadSettings(JsonObject o, out bool voiceReady)
    {
        voiceReady = o["voiceReady"] is JsonValue v && v.TryGetValue<bool>(out var ready) && ready;
        var s = new DjSettings();
        return Apply(o, s) is null ? s : null;
    }

    /// <summary>The partial update (POST /api/dj/settings) that turns [was] into [now]: only what differs, one playlist as <c>playlist</c> and more as
    /// <c>playlists</c>. Empty when they are the same.</summary>
    public static JsonObject Patch(DjSettings was, DjSettings now)
    {
        var p = new JsonObject();
        void B(string k, bool a, bool b) { if (a != b) p[k] = b; }
        B("enabled", was.Enabled, now.Enabled); B("phone", was.Phone, now.Phone); B("auto", was.Auto, now.Auto); B("web", was.Web, now.Web);
        B("windows", was.Windows, now.Windows); B("backup", was.Backup, now.Backup); B("talkAfterSkips", was.TalkAfterSkips, now.TalkAfterSkips);
        if (was.Chatty != now.Chatty) p["chatty"] = now.Chatty == DjChatty.Rare ? "rare" : "often";
        if (was.Style != now.Style) p["style"] = StyleName(now.Style);
        var changed = was.Playlists.Keys.Union(now.Playlists.Keys)
            .Where(k => was.Playlists.TryGetValue(k, out var a) != now.Playlists.TryGetValue(k, out var b) || a != b).ToList();
        if (changed.Count == 1) p["playlist"] = new JsonObject { ["id"] = changed[0], ["on"] = now.Playlists.TryGetValue(changed[0], out var on) ? on : null };
        else if (changed.Count > 1)
        {
            var all = new JsonObject();
            foreach (var (k, v) in now.Playlists) all[k] = v;
            p["playlists"] = all;
        }
        return p;
    }
}
