using Android.App;
using Android.Content.PM;

namespace FLACie.Hybrid;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>The app is on screen (the visualisers are only fed then).</summary>
    public static volatile bool Visible;
    protected override void OnCreate(Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // the media notification (Android 13 and later ask once); no microphone: the visualisers read the decoded sound
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Permission.Granted)
            RequestPermissions([Android.Manifest.Permission.PostNotifications], 1);
    }
    protected override void OnResume() { base.OnResume(); Visible = true; }
    protected override void OnPause() { Visible = false; base.OnPause(); }
}

[Application]
public class MainApplication(IntPtr handle, Android.Runtime.JniHandleOwnership ownership) : MauiApplication(handle, ownership)
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
