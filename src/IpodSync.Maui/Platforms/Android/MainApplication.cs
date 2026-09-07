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
}
