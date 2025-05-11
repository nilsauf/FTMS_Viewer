namespace FTMS_Viewer.Pages;

using System.Reactive.Disposables;
using System.Reactive.Linq;

public sealed partial class FlyoutDebugLogPage : ContentView, IDisposable
{
	private readonly SerialDisposable deselectSubscription = new();

	public FlyoutDebugLogPage()
	{
		this.InitializeComponent();
	}

	public void Dispose()
		=> this.deselectSubscription.Dispose();

	private void MyCollectionView_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (e.CurrentSelection is null || e.CurrentSelection.Count is 0)
			return;

		if (sender is CollectionView cv)
			this.deselectSubscription.Disposable = Observable.Timer(TimeSpan.FromMilliseconds(2400))
				.ObserveOn(SynchronizationContext.Current!)
				.Subscribe(_ => cv.SelectedItem = null);
	}
}