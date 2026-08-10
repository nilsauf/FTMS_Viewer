namespace FTMS_Viewer.Pages;

public partial class StatePage : ContentPage
{
	public StatePage(StateViewModel viewModel)
	{
		this.InitializeComponent();
		this.BindingContext = viewModel;
	}
}
