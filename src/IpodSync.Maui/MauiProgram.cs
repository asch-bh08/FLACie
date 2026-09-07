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
		builder.Services.AddSingleton<IIpodSyncBackend, IpodSync.Maui.Platforms.Android.SafIpodSyncBackend>();
#else
		builder.Services.AddSingleton<IIpodSyncBackend, LocalIpodSyncBackend>();
#endif

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
