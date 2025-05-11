namespace FTMS_Viewer;

using FTMS_Viewer.Pages;

public partial class AppShell : Shell
{
	public FlyoutDebugLogViewModel LogViewModel { get; }

	public AppShell(FlyoutDebugLogViewModel logViewModel)
	{
		this.LogViewModel = logViewModel;
		this.BindingContext = this;
		this.InitializeComponent();
	}
}