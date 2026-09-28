namespace IpodSync.Maui;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new MainPage()) { Title = "FLACie", Width = 1400, Height = 900, MinimumWidth = 900, MinimumHeight = 600 };
	}
}
