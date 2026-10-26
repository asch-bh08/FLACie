using System.Collections.Concurrent;
using FLACie.Core;

namespace FLACie.Server.Dj;

/// <summary>
/// The facts the DJ may add about the next song, all already on this server and never fetched for it: how it feels (<see cref="VibeIndex"/>), what the
/// MusicBrainz cache holds (data/mb-cache), and whether this listener loves it or has never played it (their <see cref="TasteProfile"/>, kept for ten minutes
/// per account so a song change does not rebuild it).
/// </summary>
public sealed class DjFactsSource(VibeIndex? vibes, MusicBrainzClient? mb, ListenLogStore? listens, Func<DateTime>? clock = null)
{
    static readonly TimeSpan TasteLife = TimeSpan.FromMinutes(10);
    readonly Func<DateTime> now = clock ?? (() => DateTime.UtcNow);
    readonly ConcurrentDictionary<string, (DateTime At, TasteProfile Taste)> tastes = new();

    static Track? Find(UserSession s, DjSong? song) => song is null ? null : s.Library.Resolve(song.Path, song.Title, song.Artist);

    TasteProfile Taste(UserSession s)
    {
        var t = now();
        if (tastes.TryGetValue(s.Id, out var k) && t - k.At < TasteLife) return k.Taste;
        var taste = HomeBuilder.Taste(s.Library, s.History, s.Favorites, s.Playlists, t, s.PlayStats, null, listens is not null && listens.Exists(s) ? listens.Read(s) : null);
        tastes[s.Id] = (t, taste);
        return taste;
    }

    public (DjExtra? Extra, DjBond Bond) Of(UserSession s, DjRequest req)
    {
        if (Find(s, req.Next) is not { } t) return (null, DjBond.Unknown);
        var vibe = vibes?.Of(t);
        var prev = Find(s, req.Prev) is { } p ? vibes?.Of(p) : null;
        var extra = DjFactsOf.Brainz(t, mb?.Peek(t), vibe, prev);
        DjBond bond;
        try { bond = DjFactsOf.Bond(Taste(s), t); } catch (Exception) { bond = DjBond.Unknown; }
        return (extra, bond);
    }
}
