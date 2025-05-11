namespace FTMS_Viewer.Pages;

public partial class ControlPage : ContentPage
{
	public ControlPage(ControlViewModel viewModel)
	{
		this.InitializeComponent();
		this.BindingContext = viewModel;
	}
}