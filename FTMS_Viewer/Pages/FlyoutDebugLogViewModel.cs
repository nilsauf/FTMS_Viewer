namespace FTMS_Viewer.Pages;

using System.Reactive.Disposables;
using System.Reactive.Linq;

using CommunityToolkit.Mvvm.Input;

using DynamicData.Binding;

using FTMS_Viewer.DebugLog;

public sealed partial class FlyoutDebugLogViewModel : IDisposable
{
	public readonly IDisposable cleanUp;
	public readonly BooleanDisposable disposedFlag = new();
	public readonly Lock @lock = new();
    private readonly IToastService toastService;

    public ObservableCollectionExtended<DebugLogItem> Items { get; } = [];

	public FlyoutDebugLogViewModel(
		IDebugPageLogProvider debugPageLogProvider,
		IToastService toastService)
	{
		this.toastService = toastService;

		this.cleanUp = debugPageLogProvider.ObserveLogItems()
			.ObserveOn(SynchronizationContext.Current!)
			.Subscribe(item =>
			{
				lock (this.@lock)
				{
					if (this.disposedFlag.IsDisposed)
						return;

					using var not = this.Items.SuspendNotifications();
					this.Items.Insert(0, item);
					if (this.Items.Count > 110)
					{
						this.Items.RemoveRange(this.Items.Count - 10, 10);
					}
				}
			});
    }

	[RelayCommand(CanExecute = nameof(CanShowToast))]
	private Task ShowItemsToastAsync(DebugLogItem item)
	{
		string message = $"{item.LogLevel} - {item.Category}";
		if (string.IsNullOrWhiteSpace(item.ExceptionName) is false)
		{
			message += $"\n{item.ExceptionMessage}";
		}
		return this.toastService.ShowAsync(message);
	}

	private static bool CanShowToast(DebugLogItem item)
		=> item is not null;

	public void Dispose()
	{
		this.disposedFlag.Dispose();
		this.cleanUp.Dispose();
	}
}
