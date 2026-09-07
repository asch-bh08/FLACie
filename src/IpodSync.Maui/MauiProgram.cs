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
		// IpodSync.Core directly, exactly like the web host. Android has no such
		// thing -- a classic iPod only speaks USB, so the phone reaches it (when
		// plugged in via USB-OTG) through the SCSI/FAT32 stack instead. See
		// HANDOFF.md for why these are genuinely different, not two skins on the
		// same code path.
#if ANDROID
		builder.Services.AddSingleton<IIpodSyncBackend, IpodSync.Maui.Platforms.Android.UsbIpodSyncBackend>();
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
