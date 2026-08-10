namespace FTMS_Viewer.Pages;

using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;

using FTMS.NET;
using FTMS.NET.State;

using Microsoft.Extensions.Logging;

using Plugin.BLE.FTMS;

public sealed partial class StateViewModel : ObservableObject, IDisposable
{
	private const string NotConnected = "Not Connected";

	private readonly ILogger<StateViewModel> logger;
	private readonly IDisposable cleanUp;
	private readonly CompositeDisposable currentProviderSubscriptions = new();

	[ObservableProperty]
	public partial string Status { get; private set; } = NotConnected;

	[ObservableProperty]
	public partial bool IsConnected { get; private set; }

	public StateViewModel(IConnectionManager connectionManager, ILogger<StateViewModel> logger)
	{
		this.logger = logger;
		this.cleanUp = connectionManager.ObserveCurrentServiceConnection(
				s => s
					.Select(CreateStateProviderObservable)
					.Switch(),
				10)
			.ObserveOn(SynchronizationContext.Current!)
			.Subscribe(
				this.HandleStateProvider,
				ex => this.logger.LogError(ex, "Error during observing current service state! Stopping..."));
	}

	private static IObservable<IFitnessMachineStateProvider?> CreateStateProviderObservable(
		IFitnessMachineServiceConnection? connection)
	{
		if (connection is null)
			return Observable.Return<IFitnessMachineStateProvider?>(null);

		return Observable.Create<IFitnessMachineStateProvider?>(observer =>
		{
			var providerDisposable = new SerialDisposable();
			var providerCreationTask = connection.CreateFitnessMachineStateProviderAsync();
			var providerSubscription = providerCreationTask
				.ToObservable()
				.Subscribe(observer.OnNext, observer.OnError);

			_ = providerCreationTask.ContinueWith(
				t =>
				{
					if (t.IsCompletedSuccessfully)
						providerDisposable.Disposable = t.Result;
				},
				CancellationToken.None,
				TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);

			return new CompositeDisposable(providerSubscription, providerDisposable);
		});
	}

	private void HandleStateProvider(IFitnessMachineStateProvider? provider)
	{
		this.ResetState();

		if (provider is null)
			return;

		this.IsConnected = true;
		this.Status = string.Empty;

		this.currentProviderSubscriptions.Add(
			provider.ObserveMachineState()
				.ObserveOn(SynchronizationContext.Current!)
				.Subscribe(this.HandleMachineState, this.HandleMachineStateError));
		this.currentProviderSubscriptions.Add(
			provider.ObserveTrainingState()
				.ObserveOn(SynchronizationContext.Current!)
				.Subscribe(this.HandleTrainingState, this.HandleTrainingStateError));
	}

	private void HandleMachineState(IFitnessMachineState state)
		=> this.logger.LogDebug("Received machine state notification: {OpCode}", state.OpCode);

	private void HandleTrainingState(ITrainingState state)
		=> this.logger.LogDebug("Received training state notification: {State}", state.State);

	private void HandleMachineStateError(Exception ex)
		=> this.logger.LogError(ex, "Error during observing machine state! Stopping machine-state stream...");

	private void HandleTrainingStateError(Exception ex)
		=> this.logger.LogError(ex, "Error during observing training state! Stopping training-state stream...");

	private void ResetState()
	{
		this.IsConnected = false;
		this.Status = NotConnected;
		this.currentProviderSubscriptions.Clear();
	}

	public void Dispose()
	{
		this.currentProviderSubscriptions.Dispose();
		this.cleanUp.Dispose();
	}
}
