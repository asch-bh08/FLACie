namespace IpodSync.Shared.Backend;

/// <summary>
/// File/folder pickers drawn by the app itself (see Components/PickerHost.razor), for
/// hosts whose native pickers don't hand back filesystem paths — Android's return
/// content:// URIs — or have none (the web host). Browses with plain System.IO, so on
/// Android it needs the same "All files access" the write path does.
/// </summary>
public sealed class InAppPickers(Func<IReadOnlyList<(string Label, string Path)>>? roots = null) : IHostPickers
{
    public sealed class Request
    {
        public required PickKind? Kind { get; init; }       // null = pick a folder
        public required bool Multiple { get; init; }
        internal TaskCompletionSource<IReadOnlyList<string>> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public bool Available => true;
    public Request? Current { get; private set; }
    public event Action? Changed;

    public IReadOnlyList<(string Label, string Path)> Roots() => roots?.Invoke() ?? DefaultRoots();

    public Task<IReadOnlyList<string>> PickFilesAsync(PickKind kind, bool multiple) => Open(new Request { Kind = kind, Multiple = multiple });

    public async Task<string?> PickFolderAsync() => (await Open(new Request { Kind = null, Multiple = false })).FirstOrDefault();

    public Task OpenFolderAsync(string path) => Task.CompletedTask;

    private Task<IReadOnlyList<string>> Open(Request r)
    {
        Current?.Done.TrySetResult([]);
        Current = r;
        Changed?.Invoke();
        return r.Done.Task;
    }

    public void Complete(IReadOnlyList<string> paths)
    {
        var r = Current;
        Current = null;
        Changed?.Invoke();
        r?.Done.TrySetResult(paths);
    }

    public static string[] Extensions(PickKind kind) => kind switch
    {
        PickKind.Image => [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".mp3", ".m4a", ".flac"],
        PickKind.Playlist => [".txt", ".m3u", ".m3u8"],
        _ => [.. Core.LocalLibrary.FolderSync.AudioExtensions],
    };

    private static List<(string, string)> DefaultRoots()
    {
        var list = new List<(string, string)>();
        string music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (Directory.Exists(music)) list.Add(("Music", music));
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(home)) list.Add(("Home", home));
        foreach (var d in DriveInfo.GetDrives())
        {
            try { if (d.IsReady) list.Add((d.Name, d.RootDirectory.FullName)); } catch { }
        }
        return list;
    }
}
