using System.Runtime.CompilerServices;
using FLACie.Core;

namespace FLACie.Server;

public sealed class DownloadJob(string id, string label, string? artKey)
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    public string? ArtKey { get; } = artKey;
    public DownloadStage Stage { get; internal set; } = DownloadStage.Requested;
    public string Message { get; internal set; } = "Requested";
    public bool Finished => Stage is DownloadStage.Done or DownloadStage.Failed;
}

/// <summary>Runs downloads in the background for the signed-in user (they keep going when the tab closes) and keeps their progress for
/// the page. The Soulseek and Lidarr addresses and keys come from the user's profile and never reach the browser.</summary>
public sealed class DownloadManager(IHttpClientFactory hf, WebCatalog catalog)
{
    sealed class Jobs { public readonly List<DownloadJob> List = []; public event Action? Changed; public void Raise() => Changed?.Invoke(); }
    readonly ConditionalWeakTable<UserSession, Jobs> table = new();

    Jobs For(UserSession s) => table.GetValue(s, _ => new Jobs());
    public IReadOnlyList<DownloadJob> JobsOf(UserSession s) { var j = For(s); lock (j.List) return j.List.ToList(); }
    public void Subscribe(UserSession s, Action a) => For(s).Changed += a;
    public void Unsubscribe(UserSession s, Action a) => For(s).Changed -= a;

    public void DismissFinished(UserSession s) { var j = For(s); lock (j.List) j.List.RemoveAll(x => x.Finished); j.Raise(); }

    DownloadJob Add(UserSession s, string label, string? artUrl)
    {
        var j = For(s); var job = new DownloadJob(Guid.NewGuid().ToString("N"), label, artUrl is null ? null : Art.ExternalKey(artUrl));
        lock (j.List) j.List.Insert(0, job);
        j.Raise();
        return job;
    }

    void Run(UserSession s, DownloadJob job, Func<DownloadCoordinator, Action<DownloadStatus>, Task> work)
    {
        var jobs = For(s);
        var coordinator = new DownloadCoordinator(hf.CreateClient("downloads"), catalog, s.Services);
        _ = Task.Run(async () =>
        {
            try
            {
                await work(coordinator, st =>
                {
                    job.Stage = st.Stage; job.Message = st.Message;
                    if (st.NewTracks is { Count: > 0 } t) s.AddDownloaded(t);
                    jobs.Raise();
                });
            }
            catch (Exception e) { job.Stage = DownloadStage.Failed; job.Message = e.Message; jobs.Raise(); }
            if (!job.Finished) { job.Stage = DownloadStage.Failed; job.Message = "Stopped"; jobs.Raise(); }
        });
    }

    public void Song(UserSession s, CatalogSong song) =>
        Run(s, Add(s, $"{song.Title} · {song.Artist}", song.ArtUrl), (c, on) => c.DownloadAsync(song.Artist, song.Title, song.Album, song.ArtUrl, song.DurationMs, on));

    public void Album(UserSession s, CatalogAlbum album) =>
        Run(s, Add(s, $"{album.CleanTitle} · {album.Artist}", album.ArtUrl), (c, on) => c.DownloadAlbumAsync(album, on));
}
