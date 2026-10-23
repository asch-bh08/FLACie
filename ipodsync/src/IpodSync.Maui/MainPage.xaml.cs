namespace IpodSync.Maui;

public partial class MainPage : ContentPage
{
	static readonly Color Accent = Color.FromArgb("#ff4d73");
	static readonly Color Quiet = Color.FromArgb("#1e1e25");
	bool flacieLoaded;

	public MainPage()
	{
		InitializeComponent();
#if WINDOWS
		MigrateOldSettingsFolder();
		if (Platforms.Windows.FlacieHost.Available)
		{
			modeBar.IsVisible = true;
			// the player is the app; the iPod manager is one click away, and the last choice is remembered
			_ = ShowAsync(Preferences.Get("mode", "flacie") == "flacie");
		}
		else Paint(false);
#endif
	}

#if WINDOWS
	/// <summary>
	/// Preferences used to live in %LOCALAPPDATA%\User Name\dev.ashley.ipodsync (MAUI's template placeholder publisher); the
	/// publisher is now FLACie. Copy the old file across once so the remembered mode survives.
	/// </summary>
	static void MigrateOldSettingsFolder()
	{
		try
		{
			var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			var old = Path.Combine(root, "User Name", "dev.ashley.ipodsync", "Settings", "preferences.dat");
			var now = Path.Combine(root, "FLACie", "dev.ashley.ipodsync", "Settings", "preferences.dat");
			if (!File.Exists(old) || File.Exists(now)) return;
			Directory.CreateDirectory(Path.GetDirectoryName(now)!);
			File.Copy(old, now);
		}
		catch (Exception) { }
	}
#endif

	// Retro is not a tab up here any more: the player has it in its own bar and menu, and this tab sent the view to Retro with no way back (FLACie stayed on it)
	void OnFlacieClicked(object? sender, EventArgs e) => _ = ShowAsync(true);

	void OnIpodClicked(object? sender, EventArgs e) => _ = ShowAsync(false);

	void Paint(bool flacie)
	{
		flacieButton.BackgroundColor = flacie ? Accent : Quiet; flacieButton.TextColor = Colors.White;
		ipodButton.BackgroundColor = flacie ? Quiet : Accent; ipodButton.TextColor = Colors.White;
	}

	async Task ShowAsync(bool flacie)
	{
		Preferences.Set("mode", flacie ? "flacie" : "ipod");
		Paint(flacie);
		blazorWebView.IsVisible = !flacie;
		flacieView.IsVisible = flacie;
#if WINDOWS
		if (flacie && !flacieLoaded)
		{
			modeStatus.Text = "Starting FLACie…";
			try
			{
				flacieView.Source = await Platforms.Windows.FlacieHost.StartAsync();
				flacieLoaded = true; modeStatus.Text = "";
			}
			catch (Exception ex) { modeStatus.Text = ex.Message; }
		}
#endif
	}

	/// <summary>
	/// Windows only: hand the WebView to WindowsMediaSource so it can map the iPod (and the
	/// playback conversion cache) to virtual host names — a page can't load file:// URLs, so
	/// that is how the &lt;audio&gt; element reaches the iPod's own files.
	/// </summary>
	private void OnWebViewInitialized(object? sender, EventArgs e)
	{
#if WINDOWS
		if (e is Microsoft.AspNetCore.Components.WebView.BlazorWebViewInitializedEventArgs args)
			Platforms.Windows.WindowsMediaSource.Attach(args.WebView.CoreWebView2);
#endif
	}
}
