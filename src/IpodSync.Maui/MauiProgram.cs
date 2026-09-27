using IpodSync.Shared.Backend;
using Microsoft.Extensions.Logging;

namespace IpodSync.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddHttpClient();

		// Windows runs on the same PC the iPod is plugged into, so it can talk to
		// IpodSync.Core directly, exactly like the web host. Android reads through
		// the Storage Access Framework instead (SafIpodSyncBackend): real-hardware
		// testing showed the OS (Samsung One UI, at least) auto-mounts a USB Mass
		// Storage device the moment it's attached, and the original approach --
		// claiming the raw USB interface directly (UsbIpodSyncBackend, still in the
		// tree) -- fights that mount rather than using it, which is what caused a
		// "USB storage device was removed unsafely" notification and a failed
		// transfer during testing. See HANDOFF.md and SafIpodSyncBackend's class
		// comment for the full story.
#if ANDROID
		// Writes through the mounted USB volume once "All files access" is granted; read-only
		// document-picker (SAF) fallback otherwise. See AndroidIpodSyncBackend.
		IpodSync.Core.Artwork.Thumbnailer.Rasterizer = new IpodSync.Maui.Platforms.Android.AndroidRasterizer();
		builder.Services.AddSingleton<IIpodSyncBackend, IpodSync.Maui.Platforms.Android.AndroidIpodSyncBackend>();
		// Android decodes ALAC/AAC/MP3 itself, so playback goes through its media player.
		builder.Services.AddSingleton<IpodSync.Shared.Playback.AudioPlayer, IpodSync.Maui.Platforms.Android.AndroidAudioPlayer>();
		builder.Services.AddSingleton<IHostPickers>(new InAppPickers(IpodSync.Maui.Platforms.Android.AndroidIpodSyncBackend.PickerRoots));
#else
		builder.Services.AddSingleton<IIpodSyncBackend, LocalIpodSyncBackend>();
#if WINDOWS
		builder.Services.AddSingleton<IHostPickers, IpodSync.Maui.Platforms.Windows.WindowsHostPickers>();
		// WebView2 plays the iPod's files through a virtual host mapping (see WindowsMediaSource).
		builder.Services.AddSingleton<IpodSync.Shared.Playback.IMediaSource, IpodSync.Maui.Platforms.Windows.WindowsMediaSource>();
		builder.Services.AddScoped<IpodSync.Shared.Playback.AudioPlayer, IpodSync.Shared.Playback.HtmlAudioPlayer>();
#else
		builder.Services.AddSingleton<IHostPickers, NoHostPickers>();
#endif
#endif
		// One app window, one state: the pending-changes queue survives tab switches.
		builder.Services.AddSingleton<IpodSync.Shared.State.AppState>();
		builder.Services.AddScoped<IpodSync.Shared.Playback.PlayerState>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
