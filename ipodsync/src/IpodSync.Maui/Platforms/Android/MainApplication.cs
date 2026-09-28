using Android.App;
using Android.Runtime;
using IpodSync.Maui.Platforms.Android;

namespace IpodSync.Maui;

[Application]
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
		CrashLog.Init(this);
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	// Lets ipodplayer (on this same phone) browse and edit a USB-connected iPod through this app's verified
	// engine -- see LocalApiServer. Loopback only.
	public override void OnCreate()
	{
		base.OnCreate();
		LocalApiServer.Start(IPlatformApplication.Current!.Services.GetRequiredService<IpodSync.Shared.Backend.IIpodSyncBackend>());
	}
}
