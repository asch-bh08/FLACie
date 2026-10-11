using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace IpodSync.Maui.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	// Held for the life of the process: the per-user single-instance guard (released by the OS when the process ends).
	static Mutex? instanceMutex;

	public App()
	{
		if (!ClaimSingleInstance())
		{
			// a second launch only brings the first window to the front, then exits before it starts a window or a server
			BringExistingWindowToFront();
			Environment.Exit(0);
			return;
		}
		SetWebViewProfileFolder();
		this.InitializeComponent();
	}

	static bool ClaimSingleInstance()
	{
		try
		{
			instanceMutex = new Mutex(true, @"Local\FLACie.Windows.App.SingleInstance", out var createdNew);
			return createdNew;
		}
		catch (Exception) { return true; } // never block the app on a guard failure
	}

	/// <summary>
	/// WebView2 keeps its profile (cookies, local storage, caches) next to the exe by default, which here is the install
	/// folder: it survived uninstall and would break a per-machine install. Point it at %LOCALAPPDATA%\FLACie\webview2
	/// (next to the player's data) before any WebView is created. A profile left in the install folder by 0.48/0.49 is moved over
	/// once so the sign-in survives the upgrade.
	/// </summary>
	static void SetWebViewProfileFolder()
	{
		try
		{
			var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FLACie", "webview2");
			var old = Path.Combine(AppContext.BaseDirectory, "IpodSync.Maui.exe.WebView2");
			Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
			if (Directory.Exists(old) && !Directory.Exists(folder))
			{
				try { Directory.Move(old, folder); } catch (Exception) { }
			}
			Directory.CreateDirectory(folder);
			// the legacy folder left behind by 0.49 (the new profile already exists): remove exactly that folder, best effort
			if (Directory.Exists(old) && string.Equals(Path.GetFileName(old), "IpodSync.Maui.exe.WebView2", StringComparison.Ordinal))
			{
				try { Directory.Delete(old, true); } catch (Exception) { }
			}
			Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", folder);
		}
		catch (Exception) { }
	}

	static void BringExistingWindowToFront()
	{
		try
		{
			using var me = Process.GetCurrentProcess();
			// the first instance may still be starting up: give its window a moment to exist
			for (var attempt = 0; attempt < 20; attempt++)
			{
				foreach (var p in Process.GetProcessesByName(me.ProcessName))
				{
					using (p)
					{
						if (p.Id == me.Id) continue;
						var h = p.MainWindowHandle;
						if (h == IntPtr.Zero) continue;
						if (IsIconic(h)) ShowWindow(h, 9); // SW_RESTORE
						SetForegroundWindow(h);
						return;
					}
				}
				Thread.Sleep(250);
			}
		}
		catch (Exception) { }
	}

	[DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
	[DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
	[DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

