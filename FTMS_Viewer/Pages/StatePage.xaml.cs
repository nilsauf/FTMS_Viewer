namespace FTMS_Viewer.Pages;

using System.Collections.Specialized;

public partial class StatePage : ContentPage
{
	public StatePage(StateViewModel viewModel)
	{
		this.InitializeComponent();
		this.BindingContext = viewModel;
		viewModel.MachineStateLog.CollectionChanged += this.HandleMachineStateLogCollectionChanged;
	}

	private void HandleMachineStateLogCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action is not NotifyCollectionChangedAction.Add)
			return;

		this.Dispatcher.Dispatch(() =>
		{
			if (this.MachineStateLogCollectionView.ItemsSource is not System.Collections.ICollection source
				|| source.Count is 0)
				return;

			this.MachineStateLogCollectionView.ScrollTo(source.Count - 1, position: ScrollToPosition.End, animate: false);
		});
	}
}
