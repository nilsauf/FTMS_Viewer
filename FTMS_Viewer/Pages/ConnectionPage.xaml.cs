namespace FTMS_Viewer.Pages;

public partial class ConnectionPage : ContentPage
{
	public ConnectionViewModel ViewModel => (ConnectionViewModel)this.BindingContext;

	public ConnectionPage(ConnectionViewModel viewModel)
	{
		this.InitializeComponent();
		this.BindingContext = viewModel;
	}

	protected override async void OnAppearing()
	{
		await this.ViewModel.EnsurePermissionsAsync();
		this.ViewModel.StartScanning();
	}

	protected override void OnDisappearing()
	{
		this.ViewModel.StopScanning();
	}
}