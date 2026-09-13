using Microsoft.Data.Sqlite;
using IpodSync.Core.ItunesDb;

namespace IpodSync.Core.Itlp;

/// <summary>
/// Track mirror: brings item rows (and everything hanging off them) in the staged
/// SQLite bundle into line with the CDB's track list. Runs before
/// <see cref="ItlpSync.SyncPlaylists"/> so that new items can join playlists.
///
/// - removed tracks: item + avformat/store/video/podcast rows, playlist entries,
///   Dynamic.itdb stats, Extras.itdb lyrics/chapters, Locations.itdb location.
///   Album/artist rows left without items are kept (iTunes itself leaves such
///   orphans: 30 albums on the real device).
/// - changed tracks (title, artist, album, album artist, genre, composer): text,
///   sort text, sort ranks and the entity links (track_artist / artist / album /
///   genre_map / composer), creating entity rows as needed.
/// - new tracks: item, avformat_info, location, item_stats, entities.
///
/// Every value convention (NULL vs 0, 4CC codes, unknown-entity rows, rank scales)
/// is copied from rows iTunes wrote on the device; see OVERNIGHT-STATUS.md.
/// </summary>
public static class ItlpTrackSync
{
    private const int FourCcFile = 0x46494C45;   // 'FILE' -- location_type on every real row

    public static ItlpSync.Result Sync(string stagedItlpDir, ItunesDatabase cdb, DateTimeOffset now)
    {
        var result = new ItlpSync.Result();
        int stamp = ItlpSync.AppleSeconds(now);

        using var lib = Open(Path.Combine(stagedItlpDir, "Library.itdb"));
        using var dyn = Open(Path.Combine(stagedItlpDir, "Dynamic.itdb"));
        using var loc = Open(Path.Combine(stagedItlpDir, "Locations.itdb"));
        using var ext = Open(Path.Combine(stagedItlpDir, "Extras.itdb"));
        using var tl = lib.BeginTransaction();
        using var td = dyn.BeginTransaction();
        using var tlo = loc.BeginTransaction();
        using var te = ext.BeginTransaction();
        var L = new Db(lib, tl);

        var existing = new Dictionary<long, ItemRow>();
        foreach (var r in L.Rows("SELECT pid, title, artist, album, album_artist, genre_id, composer, track_artist_pid, artist_pid, album_pid, composer_pid FROM item"))
            existing[(long)r[0]!] = new ItemRow((string?)r[1], (string?)r[2], (string?)r[3], (string?)r[4], (long)r[5]!, (string?)r[6], (long)r[7]!, (long)r[8]!, (long)r[9]!, (long)r[10]!);

        var cdbByPid = cdb.Tracks.ToDictionary(t => unchecked((long)t.PersistentId));
        var entities = new Entities(L);

        // ---------------------------------------------------------------- removed
        foreach (var pid in existing.Keys.Where(p => !cdbByPid.ContainsKey(p)).ToList())
        {
            foreach (var t in new[] { "avformat_info", "video_info", "video_characteristics", "store_info", "podcast_info", "item_to_container", "container_seed" })
                L.Exec($"DELETE FROM {t} WHERE item_pid = $p", ("$p", pid));
            L.Exec("DELETE FROM item WHERE pid = $p", ("$p", pid));
            new Db(dyn, td).Exec("DELETE FROM item_stats WHERE item_pid = $p", ("$p", pid));
            new Db(dyn, td).Exec("DELETE FROM rental_info WHERE item_pid = $p", ("$p", pid));
            new Db(ext, te).Exec("DELETE FROM lyrics WHERE item_pid = $p", ("$p", pid));
            new Db(ext, te).Exec("DELETE FROM chapter WHERE item_pid = $p", ("$p", pid));
            new Db(loc, tlo).Exec("DELETE FROM location WHERE item_pid = $p", ("$p", pid));
            result.Actions.Add($"remove item 0x{unchecked((ulong)pid):X16} '{existing[pid].Artist} - {existing[pid].Title}'");
            result.TouchedTables.UnionWith([
                "Library.itdb:item", "Library.itdb:avformat_info", "Library.itdb:video_info", "Library.itdb:video_characteristics",
                "Library.itdb:store_info", "Library.itdb:podcast_info", "Library.itdb:item_to_container", "Library.itdb:container_seed",
                "Dynamic.itdb:item_stats", "Dynamic.itdb:rental_info", "Extras.itdb:lyrics", "Extras.itdb:chapter", "Locations.itdb:location"]);
            existing.Remove(pid);
        }

        // ---------------------------------------------------------------- changed + new
        var masterOrder = new Dictionary<long, int>();
        if (cdb.MasterPlaylist is { } master)
        {
            var idToPid = cdb.Tracks.ToDictionary(t => t.Id, t => unchecked((long)t.PersistentId));
            int i = 0;
            foreach (var id in master.TrackIds) if (idToPid.TryGetValue(id, out var p) && !masterOrder.ContainsKey(p)) masterOrder[p] = i++;
        }

        foreach (var t in cdb.Tracks)
        {
            long pid = unchecked((long)t.PersistentId);
            bool isNew = !existing.TryGetValue(pid, out var cur);
            if (!isNew && Same(cur!, t, entities)) continue;

            var m = entities.Resolve(t, pid, result);
            if (!result.Ok) break;

            if (isNew)
            {
                if (!AddItem(L, new Db(dyn, td), new Db(loc, tlo), t, pid, m, stamp, masterOrder, result)) break;
                result.Actions.Add($"add item 0x{t.PersistentId:X16} '{t.Artist} - {t.Title}' ({t.RelativePath})");
                result.TouchedTables.UnionWith(["Library.itdb:item", "Library.itdb:avformat_info", "Dynamic.itdb:item_stats", "Locations.itdb:location"]);
            }
            else
            {
                L.Exec("""
                    UPDATE item SET
                      title = $title, sort_title = $st, title_order = $to,
                      artist = $artist, sort_artist = $sa, artist_order = $ao, track_artist_pid = $tap,
                      album_artist = $aa, sort_album_artist = $saa, album_artist_order = $aao, artist_pid = $ap,
                      album = $album, sort_album = $sal, album_order = $alo, album_pid = $alp,
                      genre_id = $gid, genre_order = $go,
                      composer = $composer, sort_composer = $sc, composer_order = $co, composer_pid = $cp,
                      date_modified = $now
                    WHERE pid = $pid
                    """, [.. m.Params(), ("$now", stamp), ("$pid", pid), ("$composer", (object?)t.Composer ?? DBNull.Value)]);
                result.Actions.Add($"update item 0x{t.PersistentId:X16}: '{cur!.Artist} - {cur.Title}' -> '{t.Artist} - {t.Title}' (album '{cur.Album}' -> '{t.Album}')");
                result.TouchedTables.Add("Library.itdb:item");
            }
            entities.RecordItem(m);
        }

        // item.physical_order == position in the master playlist on every real row.
        if (result.Ok)
        {
            foreach (var r in L.Rows("SELECT pid, physical_order FROM item"))
            {
                long pid = (long)r[0]!;
                if (masterOrder.TryGetValue(pid, out int want) && (r[1] is null || (long)r[1]! != want))
                {
                    L.Exec("UPDATE item SET physical_order = $o WHERE pid = $p", ("$o", want), ("$p", pid));
                    result.TouchedTables.Add("Library.itdb:item");
                }
            }
            result.TouchedTables.UnionWith(entities.Touched);
        }

        if (!result.Ok) { tl.Rollback(); td.Rollback(); tlo.Rollback(); te.Rollback(); return result; }
        tl.Commit(); td.Commit(); tlo.Commit(); te.Commit();
        return result;
    }

    private static bool Same(ItemRow cur, Track t, Entities e) =>
        (cur.Title ?? "") == (t.Title ?? "") && (cur.Artist ?? "") == (t.Artist ?? "") && (cur.Album ?? "") == (t.Album ?? "") &&
        (cur.AlbumArtist ?? "") == (t.AlbumArtist ?? "") && (cur.Composer ?? "") == (t.Composer ?? "") &&
        (e.GenreName(cur.GenreId) ?? "") == (t.Genre ?? "");

    private static bool AddItem(Db L, Db D, Db Lo, Track t, long pid, Entities.Links m, int stamp,
        Dictionary<long, int> masterOrder, ItlpSync.Result result)
    {
        string ext = Path.GetExtension(t.RelativePath ?? "").TrimStart('.').ToLowerInvariant();
        string kind = t.FileTypeDescription ?? (ext == "mp3" ? "MPEG audio file" : "AAC audio file");
        int audioFormat = kind.Contains("Lossless", StringComparison.OrdinalIgnoreCase) ? 601
                        : kind.StartsWith("MPEG", StringComparison.OrdinalIgnoreCase) ? 301
                        : kind.StartsWith("AAC", StringComparison.OrdinalIgnoreCase) ? 502 : 0;
        if (audioFormat == 0) { result.Problems.Add($"no known avformat code for '{kind}' ({t.RelativePath}); refusing to guess"); return false; }

        string? rel = t.RelativePath;
        const string musicPrefix = "iPod_Control/Music/";
        if (rel is null || !rel.StartsWith(musicPrefix, StringComparison.OrdinalIgnoreCase))
        {
            result.Problems.Add($"track {t.Id} location '{rel}' is not under {musicPrefix}; refusing to guess base_location");
            return false;
        }
        long baseId = Lo.Scalar("SELECT id FROM base_location WHERE path = 'iPod_Control/Music'") is long b ? b : 0;
        if (baseId == 0) { result.Problems.Add("Locations.itdb has no base_location 'iPod_Control/Music'"); return false; }

        long kindId = L.Scalar("SELECT id FROM location_kind_map WHERE kind = $k", ("$k", kind)) is long k ? k : 0;
        if (kindId == 0)
        {
            kindId = (long)(L.Scalar("SELECT COALESCE(MAX(id), 0) + 1 FROM location_kind_map") ?? 1L);
            L.Exec("INSERT INTO location_kind_map (id, kind) VALUES ($i, $k)", ("$i", kindId), ("$k", kind));
            result.Actions.Add($"create location_kind_map {kindId} '{kind}'");
            result.TouchedTables.Add("Library.itdb:location_kind_map");
        }

        masterOrder.TryGetValue(pid, out int physical);
        L.Exec("""
            INSERT INTO item
              (pid, revision_level, media_kind, is_song, is_audio_book, is_music_video, is_movie, is_tv_show, is_home_video,
               is_ringtone, is_tone, is_voice_memo, is_book, is_rental, is_itunes_u, is_digital_booklet, is_podcast,
               date_modified, year, content_rating, content_rating_level, is_compilation, is_user_disabled, remember_bookmark,
               exclude_from_shuffle, part_of_gapless_album, chosen_by_auto_fill, artwork_status, artwork_cache_id,
               start_time_ms, stop_time_ms, total_time_ms, total_burn_time_ms, track_number, track_count, disc_number, disc_count,
               bpm, relative_volume, eq_preset, radio_stream_status, genius_id, genre_id, category_id, album_pid, artist_pid,
               composer_pid, title, artist, album, album_artist, composer, sort_title, sort_artist, sort_album,
               sort_album_artist, sort_composer, title_order, artist_order, album_order, genre_order, composer_order,
               album_artist_order, album_by_artist_order, series_name_order, comment, grouping, description,
               description_long, collection_description, copyright, track_artist_pid, physical_order, has_lyrics, date_released)
            VALUES
              ($pid, NULL, 1, 1, 0, 0, 0, 0, 0,
               0, 0, 0, 0, 0, 0, 0, 0,
               $now, $year, 0, 0, $comp, 0, 0,
               0, 0, 0, 2, 0,
               0, 0, $len, NULL, $tn, $tc, $dn, $dc,
               0, 0, NULL, NULL, 0, $gid, 0, $alp, $ap,
               $cp, $title, $artist, $album, $aa, $composer, $st, $sa, $sal,
               $saa, $sc, $to, $ao, $alo, $go, $co,
               $aao, NULL, 100, NULL, NULL, NULL,
               NULL, NULL, NULL, $tap, $phys, 0, 0)
            """, [.. m.Params(), ("$pid", pid), ("$now", stamp), ("$year", t.Year), ("$comp", t.Compilation ? 1 : 0),
                  ("$len", (double)t.LengthMs), ("$tn", t.TrackNumber), ("$tc", t.TotalTracks), ("$dn", t.DiscNumber),
                  ("$dc", t.TotalDiscs), ("$composer", (object?)t.Composer ?? DBNull.Value), ("$phys", physical)]);

        int sampleRate = t.SampleRate > 0 ? t.SampleRate : 44100;
        L.Exec("""
            INSERT INTO avformat_info
              (item_pid, sub_id, audio_format, bit_rate, channels, sample_rate, duration, gapless_heuristic_info,
               gapless_encoding_delay, gapless_encoding_drain, gapless_last_frame_resynch, analysis_inhibit_flags,
               audio_fingerprint, volume_normalization_energy)
            VALUES ($pid, 0, $fmt, $br, 0, $sr, $dur, 0, 0, 0, 0, 0, 0, 0)
            """, ("$pid", pid), ("$fmt", audioFormat), ("$br", t.Bitrate), ("$sr", (double)sampleRate),
                 ("$dur", (long)t.LengthMs * sampleRate / 1000));

        string extFourCc = (ext.ToUpperInvariant() + "    ")[..4];
        int extCode = (extFourCc[0] << 24) | (extFourCc[1] << 16) | (extFourCc[2] << 8) | extFourCc[3];
        Lo.Exec("""
            INSERT INTO location
              (item_pid, sub_id, base_location_id, location_type, location, extension, kind_id, date_created, file_size,
               file_creator, file_type, num_dir_levels_file, num_dir_levels_lib)
            VALUES ($pid, 0, $base, $type, $loc, $ext, $kind, $now, $size, NULL, NULL, NULL, NULL)
            """, ("$pid", pid), ("$base", baseId), ("$type", FourCcFile), ("$loc", rel[musicPrefix.Length..]),
                 ("$ext", extCode), ("$kind", kindId), ("$now", stamp), ("$size", t.SizeBytes));

        D.Exec("""
            INSERT INTO item_stats
              (item_pid, has_been_played, date_played, play_count_user, play_count_recent, date_skipped, skip_count_user,
               skip_count_recent, bookmark_time_ms, bookmark_time_ms_common, user_rating, user_rating_common, rental_expired,
               play_count_user_original, skip_count_user_original)
            VALUES ($pid, 0, 0, 0, 0, 0, 0, 0, 0.0, 0.0, 0, 0, 0, 0, 0)
            """, ("$pid", pid));
        return true;
    }

    // ================================================================ entities

    private sealed record ItemRow(string? Title, string? Artist, string? Album, string? AlbumArtist, long GenreId,
        string? Composer, long TrackArtistPid, long ArtistPid, long AlbumPid, long ComposerPid);

    private sealed class Entities(Db L)
    {
        public HashSet<string> Touched { get; } = [];
        private readonly Random _rnd = new();

        public sealed record Links(
            string? Title, string? SortTitle, long TitleOrder,
            string? Artist, string? SortArtist, long ArtistOrder, long TrackArtistPid,
            string? AlbumArtist, string? SortAlbumArtist, long AlbumArtistOrder, long ArtistPid,
            string? Album, string? SortAlbum, long AlbumOrder, long AlbumPid,
            long GenreId, long GenreOrder,
            string? SortComposer, long ComposerOrder, long ComposerPid)
        {
            public (string, object?)[] Params() =>
            [
                ("$title", N(Title)), ("$st", N(SortTitle)), ("$to", TitleOrder),
                ("$artist", N(Artist)), ("$sa", N(SortArtist)), ("$ao", ArtistOrder), ("$tap", TrackArtistPid),
                ("$aa", N(AlbumArtist)), ("$saa", N(SortAlbumArtist)), ("$aao", AlbumArtistOrder), ("$ap", ArtistPid),
                ("$album", N(Album)), ("$sal", N(SortAlbum)), ("$alo", AlbumOrder), ("$alp", AlbumPid),
                ("$gid", GenreId), ("$go", GenreOrder),
                ("$sc", N(SortComposer)), ("$co", ComposerOrder), ("$cp", ComposerPid),
            ];
            private static object N(string? s) => (object?)s ?? DBNull.Value;
        }

        public string? GenreName(long id) =>
            L.Rows("SELECT genre, is_unknown FROM genre_map WHERE id = $i", ("$i", id)).Select(r => (long)r[1]! == 1 ? null : (string?)r[0]).FirstOrDefault();

        public void RecordItem(Links m) { /* ranks are re-read from the tables on each Resolve */ }

        public Links Resolve(Track t, long pid, ItlpSync.Result result)
        {
            // ---- title (item-level rank over sort_title)
            string? sortTitle = ItlpSorting.SortName(t.Title);
            long titleOrder = Rank("SELECT sort_title, title_order FROM item WHERE pid != $p", pid, sortTitle ?? "");

            // ---- track artist (item.artist)
            var (taPid, taOrder) = TrackArtist(t.Artist, result);

            // ---- album-artist entity: named by album artist, else by track artist
            string entityArtist = t.AlbumArtist ?? t.Artist ?? "";
            var (apPid, apOrder) = t.AlbumArtist is null && t.Artist is null ? Unknown("artist") : Artist(entityArtist, result);

            // ---- album
            var (alPid, alOrder) = string.IsNullOrEmpty(t.Album) ? UnknownAlbumForItems() : Album(t.Album!, apPid, apOrder, pid, result);

            // ---- genre
            var (gId, gOrder) = Genre(t.Genre, result);

            // ---- composer
            var (cPid, cOrder) = Composer(t.Composer, result);

            return new Links(
                t.Title, sortTitle, titleOrder,
                t.Artist, ItlpSorting.SortName(t.Artist), taOrder, taPid,
                t.AlbumArtist, ItlpSorting.SortName(t.AlbumArtist), apOrder, apPid,
                t.Album, ItlpSorting.SortName(t.Album), alOrder, alPid,
                gId, gOrder,
                ItlpSorting.SortName(t.Composer), cOrder, cPid);
        }

        private long Rank(string sql, long pid, string key) =>
            ItlpSorting.NeighbourRank(L.Rows(sql, ("$p", pid)).Where(r => r[0] is not null && r[1] is not null)
                .Select(r => ((string)r[0]!, Convert.ToInt64(r[1]))), key);

        private long EntityRank(string table, string key, string where = "is_unknown = 0") =>
            ItlpSorting.NeighbourRank(L.Rows($"SELECT sort_name, name_order FROM {table} WHERE {where}")
                .Where(r => r[0] is not null && r[1] is not null).Select(r => ((string)r[0]!, Convert.ToInt64(r[1]))), key);

        private (long, long) Unknown(string table)
        {
            var r = L.Rows($"SELECT pid, name_order FROM {table} WHERE is_unknown = 1").FirstOrDefault();
            return r is null ? (0, ItlpSorting.UnknownOrder) : ((long)r[0]!, (long)r[1]!);
        }

        private (long, long) TrackArtist(string? name, ItlpSync.Result result)
        {
            if (name is null) return Unknown("track_artist");
            var r = L.Rows("SELECT pid, name_order FROM track_artist WHERE name = $n AND is_unknown = 0", ("$n", name)).FirstOrDefault();
            if (r is not null) return ((long)r[0]!, (long)r[1]!);
            long pid = (long)(L.Scalar("SELECT COALESCE(MAX(pid), 0) + 1 FROM track_artist") ?? 1L);
            string sort = ItlpSorting.SortName(name)!;
            long order = EntityRank("track_artist", sort);
            L.Exec("""
                INSERT INTO track_artist (pid, name, name_order, sort_name, has_songs, has_music_videos, has_non_compilation_tracks, is_unknown, album_count)
                VALUES ($p, $n, $o, $s, 1, 0, 1, 0, 1)
                """, ("$p", pid), ("$n", name), ("$o", order), ("$s", sort));
            result.Actions.Add($"create track_artist {pid} '{name}' name_order {order}");
            Touched.Add("Library.itdb:track_artist");
            return (pid, order);
        }

        private (long, long) Artist(string name, ItlpSync.Result result)
        {
            var r = L.Rows("SELECT pid, name_order FROM artist WHERE name = $n AND is_unknown = 0", ("$n", name)).FirstOrDefault();
            if (r is not null) return ((long)r[0]!, (long)r[1]!);
            long pid = FreshPid("artist");
            string sort = ItlpSorting.SortName(name)!;
            long order = EntityRank("artist", sort);
            L.Exec("""
                INSERT INTO artist (pid, kind, artwork_status, artwork_album_pid, name, name_order, sort_name, is_unknown, has_songs, has_music_videos)
                VALUES ($p, 2, 0, 0, $n, $o, $s, 0, 1, 0)
                """, ("$p", pid), ("$n", name), ("$o", order), ("$s", sort));
            result.Actions.Add($"create artist 0x{unchecked((ulong)pid):X16} '{name}' name_order {order}");
            Touched.Add("Library.itdb:artist");
            return (pid, order);
        }

        private (long, long) Album(string name, long artistPid, long artistOrder, long itemPid, ItlpSync.Result result)
        {
            var r = L.Rows("SELECT pid, name_order FROM album WHERE name = $n AND artist_pid = $a AND is_unknown = 0", ("$n", name), ("$a", artistPid)).FirstOrDefault();
            if (r is not null) return ((long)r[0]!, (long)r[1]!);
            long pid = FreshPid("album");
            string sort = ItlpSorting.SortName(name)!;
            long order = EntityRank("album", sort);
            L.Exec("""
                INSERT INTO album (pid, kind, artwork_status, artwork_item_pid, artist_pid, user_rating, name, name_order, all_compilations,
                                   feed_url, season_number, is_unknown, has_songs, has_music_videos, sort_order, artist_order,
                                   has_any_compilations, sort_name, artist_count_calc)
                VALUES ($p, 2, 0, $item, $a, 0, $n, $o, 0, NULL, 0, 0, 1, 0, $o, $ao, 0, $s, 1)
                """, ("$p", pid), ("$item", itemPid), ("$a", artistPid), ("$n", name), ("$o", order), ("$ao", artistOrder), ("$s", sort));
            result.Actions.Add($"create album 0x{unchecked((ulong)pid):X16} '{name}' name_order {order}");
            Touched.Add("Library.itdb:album");
            return (pid, order);
        }

        // Items without an album point at the unknown album, with the album_order the
        // device's existing album-less items carry.
        private (long, long) UnknownAlbumForItems()
        {
            var (pid, _) = Unknown("album");
            var r = L.Rows("SELECT album_order FROM item WHERE album_pid = $p LIMIT 1", ("$p", pid)).FirstOrDefault();
            long order = r?[0] is long o ? o : (long)(L.Scalar("SELECT COALESCE(MAX(album_order), 0) + 100 FROM item") ?? 100L);
            return (pid, order);
        }

        private (long, long) Genre(string? name, ItlpSync.Result result)
        {
            // item.genre_order is 100 x dense rank of case-insensitively distinct genres;
            // genre_map.genre_order is a plain sequence. Reuse an equal genre's ranks.
            if (name is null)
            {
                var u = L.Rows("SELECT g.id, i.genre_order FROM genre_map g LEFT JOIN item i ON i.genre_id = g.id WHERE g.is_unknown = 1 LIMIT 1").FirstOrDefault();
                return u is null ? (0, 0) : ((long)u[0]!, u[1] is long go ? go : 0);
            }
            var r = L.Rows("SELECT g.id, (SELECT genre_order FROM item WHERE genre_id = g.id LIMIT 1) FROM genre_map g WHERE g.genre = $n", ("$n", name)).FirstOrDefault();
            if (r is not null && r[1] is long existingOrder) return ((long)r[0]!, existingOrder);

            long itemOrder = ItlpSorting.NeighbourRank(
                L.Rows("SELECT g.genre, i.genre_order FROM item i JOIN genre_map g ON g.id = i.genre_id WHERE g.is_unknown = 0")
                 .Select(x => ((string)x[0]!, Convert.ToInt64(x[1]))), name);
            if (r is not null) return ((long)r[0]!, itemOrder);

            long id = (long)(L.Scalar("SELECT COALESCE(MAX(id), 0) + 1 FROM genre_map") ?? 1L);
            long seq = (long)(L.Scalar("SELECT COALESCE(MAX(genre_order), 0) FROM genre_map WHERE is_unknown = 0") ?? 0L) + 1;
            L.Exec("INSERT INTO genre_map (id, genre, genre_order, is_unknown, has_music, artist_count_calc, album_count_calc) VALUES ($i, $n, $o, 0, 1, 1, 1)",
                ("$i", id), ("$n", name), ("$o", seq));
            result.Actions.Add($"create genre {id} '{name}' (item genre_order {itemOrder})");
            Touched.Add("Library.itdb:genre_map");
            return (id, itemOrder);
        }

        private (long, long) Composer(string? name, ItlpSync.Result result)
        {
            if (name is null)
            {
                var (upid, _) = Unknown("composer");
                var r0 = L.Rows("SELECT composer_order FROM item WHERE composer_pid = $p LIMIT 1", ("$p", upid)).FirstOrDefault();
                return (upid, r0?[0] is long o ? o : ItlpSorting.UnknownOrder);
            }
            var r = L.Rows("SELECT pid, name_order FROM composer WHERE name = $n AND is_unknown = 0", ("$n", name)).FirstOrDefault();
            if (r is not null) return ((long)r[0]!, (long)r[1]!);
            long pid = (long)(L.Scalar("SELECT COALESCE(MAX(pid), 0) + 1 FROM composer") ?? 1L);
            string sort = ItlpSorting.SortName(name)!;
            long order = EntityRank("composer", sort);
            L.Exec("INSERT INTO composer (pid, name, name_order, sort_name, is_unknown, has_music) VALUES ($p, $n, $o, $s, 0, 1)",
                ("$p", pid), ("$n", name), ("$o", order), ("$s", sort));
            result.Actions.Add($"create composer {pid} '{name}' name_order {order}");
            Touched.Add("Library.itdb:composer");
            return (pid, order);
        }

        private long FreshPid(string table)
        {
            while (true)
            {
                long pid = _rnd.NextInt64(long.MinValue, long.MaxValue);
                if (pid != 0 && L.Scalar($"SELECT 1 FROM {table} WHERE pid = $p", ("$p", pid)) is null) return pid;
            }
        }
    }

    // ================================================================ sqlite helpers

    private static SqliteConnection Open(string path)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        c.Open();
        return c;
    }

    private sealed class Db(SqliteConnection c, SqliteTransaction tx)
    {
        public void Exec(string sql, params (string Name, object? Value)[] args)
        {
            using var cmd = Make(sql, args);
            cmd.ExecuteNonQuery();
        }

        public object? Scalar(string sql, params (string Name, object? Value)[] args)
        {
            using var cmd = Make(sql, args);
            var v = cmd.ExecuteScalar();
            return v is DBNull ? null : v;
        }

        public List<object?[]> Rows(string sql, params (string Name, object? Value)[] args)
        {
            using var cmd = Make(sql, args);
            using var r = cmd.ExecuteReader();
            var list = new List<object?[]>();
            while (r.Read())
            {
                var row = new object?[r.FieldCount];
                for (int i = 0; i < r.FieldCount; i++) row[i] = r.IsDBNull(i) ? null : r.GetValue(i);
                list.Add(row);
            }
            return list;
        }

        private SqliteCommand Make(string sql, (string Name, object? Value)[] args)
        {
            var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
            return cmd;
        }
    }
}
