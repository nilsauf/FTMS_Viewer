namespace FTMS_Viewer;

public partial class App : Application
{
	public App()
	{
		this.InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		ArgumentNullException.ThrowIfNull(activationState);

		var serivceProvider = activationState.Context.Services;
		var mainWindow = serivceProvider.GetRequiredService<Window>();
		mainWindow.Page = serivceProvider.GetRequiredService<AppShell>();
		return mainWindow;
	}
}