namespace FTMS_Viewer.Pages;
public sealed partial class FeaturesPage : ContentPage
{
	public FeaturesPage(FeaturesViewModel viewModel)
	{
		this.InitializeComponent();
		this.BindingContext = viewModel;
	}
}