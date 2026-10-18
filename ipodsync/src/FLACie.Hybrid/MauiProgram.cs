using FLACie.Core;
using FLACie.Server;
using Microsoft.AspNetCore.Components.Authorization;

namespace FLACie.Hybrid;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif
        var data = FileSystem.AppDataDirectory;
        // one client for everything Jellyfin (no timeout: it carries audio); the device id is this install's
        var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = Timeout.InfiniteTimeSpan };
        var jf = new JellyfinClient(http, DeviceId(data), "FLACie Android");
        builder.Services.AddSingleton(jf);
        builder.Services.AddSingleton(new WebCatalog(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }));
        builder.Services.AddSingleton(new LyricsService(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, jf, Path.Combine(data, "lyrics")));
        builder.Services.AddSingleton(new MbOverlay(Path.Combine(data, "mb-overlay.json")));
        builder.Services.AddSingleton(new PlayTracker((_, _) => { }, null, () => false));

        builder.Services.AddSingleton<HybridAccount>();
        builder.Services.AddSingleton<ISessionStore, HybridSessions>();
        builder.Services.AddSingleton<IAutoplayPlanner, HybridAutoplay>();
        builder.Services.AddSingleton<ITrackInfo, HybridTrackInfo>();
        builder.Services.AddAuthorizationCore();
        builder.Services.AddSingleton<HybridAuth>();
        builder.Services.AddSingleton<AuthenticationStateProvider>(sp => sp.GetRequiredService<HybridAuth>());
        builder.Services.AddScoped<PlayerState>();
#if ANDROID
        // songs play in the app's media service (Media3), not in the page: background play, the notification, lock-screen and headset controls
        builder.Services.AddScoped<IPlayerOutput, Platforms.Android.NativeOutput>();
        PlayerHost.Native = true;
#endif

        // the rest of FLACie (FLACie.App): the library, search, playlists, the player bar and the full-screen player
        AppHost.Server = false;
        var mbAgent = MusicBrainzClient.UserAgentFor(AppInfo.Current.VersionString, null);
        var mbHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        builder.Services.AddSingleton(new DataPaths(data));
        builder.Services.AddSingleton<FormatIndex>();
        builder.Services.AddSingleton<MusicBrainzGate>();
        builder.Services.AddSingleton(sp => new MusicBrainzClient(mbHttp, mbAgent, Path.Combine(data, "mb-cache"), sp.GetRequiredService<MusicBrainzGate>()));
        builder.Services.AddSingleton(sp => new CoverArtArchiveClient(mbHttp, mbAgent, sp.GetRequiredService<MusicBrainzGate>()));
        builder.Services.AddSingleton(_ => CoverOverrides.Current = new CoverOverrides(Path.Combine(data, "art-override")));
        builder.Services.AddSingleton<HitsService>();
        builder.Services.AddSingleton<ActivityLog>();
        builder.Services.AddSingleton<IDownloads, NoDownloads>();
        builder.Services.AddSingleton<IPlayerClients, NoClients>();
        builder.Services.AddScoped<Toasts>();
        builder.Services.AddScoped<ExploreState>();

        RetroHost.Version = "FLACie for Android " + AppInfo.Current.VersionString;
        return builder.Build();
    }

    static string DeviceId(string dir)
    {
        var f = Path.Combine(dir, "device-id");
        try { if (File.Exists(f)) return File.ReadAllText(f).Trim(); } catch (Exception) { }
        var id = "flacie-android-" + Guid.NewGuid().ToString("N")[..12];
        try { File.WriteAllText(f, id); } catch (Exception) { }
        return id;
    }
}
