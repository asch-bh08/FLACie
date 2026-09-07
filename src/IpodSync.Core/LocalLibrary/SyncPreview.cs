using IpodSync.Core.ItunesDb;

namespace IpodSync.Core.LocalLibrary;

public enum SyncStatus { LocalOnly, DeviceOnly, OnBoth }

public sealed record SyncPreviewItem(string Title, string Artist, SyncStatus Status);

/// <summary>
/// Compares a local folder against a device's library by title+artist and
/// reports which side each track is on. Read-only and informational -- there is
/// no code path from here that writes anything anywhere. Matching by title+
/// artist rather than file identity is a real limitation (two different
/// recordings with the same tags look identical here), but the device stores
/// files under scrambled paths, so there is no better key available without a
/// content hash, which is out of scope for a first preview.
/// </summary>
public static class SyncPreview
{
    public static List<SyncPreviewItem> Compare(IReadOnlyList<LocalTrack> local, IReadOnlyList<Track> device)
    {
        var localByKey = local.ToLookup(Key);
        var deviceByKey = device.ToLookup(t => Key(t.Title, t.Artist));

        var keys = localByKey.Select(g => g.Key).Concat(deviceByKey.Select(g => g.Key)).Distinct();

        var result = new List<SyncPreviewItem>();
        foreach (var key in keys)
        {
            var l = localByKey[key].FirstOrDefault();
            var d = deviceByKey[key].FirstOrDefault();
            var status = l is not null && d is not null ? SyncStatus.OnBoth
                : l is not null ? SyncStatus.LocalOnly
                : SyncStatus.DeviceOnly;
            result.Add(new SyncPreviewItem(l?.DisplayTitle ?? d?.Title ?? "(unknown)", l?.Artist ?? d?.Artist ?? "(unknown)", status));
        }
        return result.OrderBy(r => r.Artist, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Key(LocalTrack t) => Key(t.DisplayTitle, t.Artist);

    private static string Key(string? title, string? artist) =>
        $"{title?.Trim().ToLowerInvariant()}|{artist?.Trim().ToLowerInvariant()}";
}
