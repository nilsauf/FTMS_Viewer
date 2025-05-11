namespace FTMS_Viewer.Pages;

public partial class DataPage : ContentPage
{
	public DataPage(DataViewModel viewModel)
	{
		this.InitializeComponent();
		this.BindingContext = viewModel;
	}
}