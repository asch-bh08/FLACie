using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using IpodSync.Maui.Platforms.Android;

namespace IpodSync.Maui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        SafBridge.HandleActivityResult(requestCode, resultCode, data);
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Surface any crash from the previous run as a plain native dialog --
        // deliberately not routed through Blazor, so it still shows even if the
        // WebView itself is what crashed last time.
        string? crash = CrashLog.TryReadAndClear(this);
        if (crash is not null)
        {
            var builder = new AlertDialog.Builder(this);
            builder.SetTitle("ipodsync crashed last time");
            builder.SetMessage(crash.Length > 3000 ? crash[..3000] + "\n...(truncated)" : crash);
            builder.SetPositiveButton("OK", (_, _) => { });
            builder.Show();
        }
    }
}
