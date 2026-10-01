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
		if (Platforms.Windows.FlacieHost.Available)
		{
			modeBar.IsVisible = true;
			// the player is the app; the iPod manager is one click away, and the last choice is remembered
			_ = ShowAsync(Preferences.Get("mode", "flacie") == "flacie");
		}
		else Paint(false);
#endif
	}

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
