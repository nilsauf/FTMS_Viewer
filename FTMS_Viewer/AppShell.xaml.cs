namespace FTMS_Viewer;

using FTMS_Viewer.Pages;

public partial class AppShell : Shell
{
	public AppShell(FlyoutDebugLogViewModel logViewModel)
	{
		this.InitializeComponent();
		this.FlyoutContent = new FlyoutDebugLogPage(logViewModel);
	}
}