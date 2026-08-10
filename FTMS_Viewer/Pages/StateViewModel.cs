namespace FTMS_Viewer.Pages;

using System.Collections.ObjectModel;
using System.Globalization;
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
	private const int HistoryCapacity = 3;
	private const double HistoryOpacityStep = 0.25;
	private const int MachineStateLogCapacity = 100;

	private readonly ILogger<StateViewModel> logger;
	private readonly IDisposable cleanUp;
	private readonly CompositeDisposable currentProviderSubscriptions = new();
	private readonly List<ETrainingState> previousStates = [];

	private ETrainingState? currentState;

	[ObservableProperty]
	public partial string Status { get; private set; } = NotConnected;

	[ObservableProperty]
	public partial bool IsConnected { get; private set; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsDetailsVisible))]
	public partial string CurrentDetails { get; private set; } = string.Empty;

	public ObservableCollection<TrainingStateTileItem> TrainingStateHistory { get; } = [];

	public ObservableCollection<MachineStateLogItem> MachineStateLog { get; } = [];

	public bool IsDetailsVisible => !string.IsNullOrEmpty(this.CurrentDetails);

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
	{
		this.logger.LogDebug("Received machine state notification: {OpCode}", state.OpCode);
		this.AppendMachineStateLogEntry(state);
	}

	private void AppendMachineStateLogEntry(IFitnessMachineState state)
	{
		string opCodeName = state.OpCode.ToString().AddSpacesBetweenWords();
		string parameters;
		try
		{
			parameters = string.Join(", ", state.ReadParameters().Select(FormatParameter));
		}
		catch (KeyNotFoundException)
		{
			this.logger.LogWarning("Received machine-state notification with unknown opcode {OpCode}", state.OpCode);
			opCodeName = $"Unknown Opcode (0x{(byte)state.OpCode:X2})";
			parameters = string.Empty;
		}

		this.MachineStateLog.Add(new MachineStateLogItem(opCodeName, parameters, FormatRawData(state.OpCode, state.RawData)));
		if (this.MachineStateLog.Count > MachineStateLogCapacity)
			this.MachineStateLog.RemoveAt(0);
	}

	private static string FormatParameter(object parameter)
		=> parameter switch
		{
			FitnessMachineStateParameter p => string.Concat(
				p.Name, ": ", FormatValue(p.Value), FormatUnit(p.Unit)),
			Enum e => e.ToString().AddSpacesBetweenWords(),
			_ => parameter.ToString() ?? string.Empty,
		};

	private static string FormatValue(double value)
		=> value.ToString("0.###", CultureInfo.InvariantCulture);

	private static string FormatUnit(FitnessMachineUnit unit) => unit switch
	{
		FitnessMachineUnit.None => string.Empty,
		FitnessMachineUnit.KilometersPerHour => " km/h",
		FitnessMachineUnit.Percent => " %",
		FitnessMachineUnit.Watt => " W",
		FitnessMachineUnit.BeatsPerMinute => " bpm",
		FitnessMachineUnit.Calories => " kcal",
		FitnessMachineUnit.Steps => " steps",
		FitnessMachineUnit.Stride => " strides",
		FitnessMachineUnit.Millimeters => " mm",
		FitnessMachineUnit.Meters => " m",
		FitnessMachineUnit.Seconds => " s",
		FitnessMachineUnit.PerMinute => " rpm",
		FitnessMachineUnit.MetersPerSecond => " m/s",
		FitnessMachineUnit.KilogramPerMeter => " kg/m",
		_ => string.Empty,
	};

	private static string FormatRawData(EStateOpCode opCode, byte[] rawData)
		=> string.Join(" ", rawData.Prepend((byte)opCode).Select(b => b.ToString("X2")));

	private void HandleTrainingState(ITrainingState state)
	{
		this.logger.LogDebug("Received training state notification: {State}", state.State);
		this.UpdateCurrentState(state.State, state.Details);
	}

	private void UpdateCurrentState(ETrainingState state, string? details)
	{
		if (this.currentState == state)
		{
			this.CurrentDetails = details ?? string.Empty;
			return;
		}

		if (this.currentState is { } previous)
			this.AddToHistory(previous);

		this.currentState = state;
		this.Status = FormatStateName(state);
		this.CurrentDetails = details ?? string.Empty;
		this.RebuildHistory();
	}

	private void AddToHistory(ETrainingState state)
	{
		this.previousStates.Add(state);
		if (this.previousStates.Count > HistoryCapacity)
			this.previousStates.RemoveAt(0);
	}

	private void RebuildHistory()
	{
		this.TrainingStateHistory.Clear();
		for (int i = this.previousStates.Count - 1, opacityIndex = 0; i >= 0; i--, opacityIndex++)
		{
			var opacity = 1.0 - (opacityIndex + 1) * HistoryOpacityStep;
			this.TrainingStateHistory.Add(new TrainingStateTileItem(FormatStateName(this.previousStates[i]), opacity));
		}
	}

	private static string FormatStateName(ETrainingState state)
		=> state.ToString().AddSpacesBetweenWords();

	private void HandleMachineStateError(Exception ex)
		=> this.logger.LogError(ex, "Error during observing machine state! Stopping machine-state stream...");

	private void HandleTrainingStateError(Exception ex)
		=> this.logger.LogError(ex, "Error during observing training state! Stopping training-state stream...");

	private void ResetState()
	{
		this.IsConnected = false;
		this.Status = NotConnected;
		this.CurrentDetails = string.Empty;
		this.currentState = null;
		this.previousStates.Clear();
		this.TrainingStateHistory.Clear();
		this.MachineStateLog.Clear();
		this.currentProviderSubscriptions.Clear();
	}

	public void Dispose()
	{
		this.currentProviderSubscriptions.Dispose();
		this.cleanUp.Dispose();
	}
}

public sealed class TrainingStateTileItem(string stateName, double opacity)
{
	public string StateName { get; } = stateName;
	public double Opacity { get; } = opacity;
}

public sealed class MachineStateLogItem(string opCodeName, string parameters, string rawData)
{
	public string OpCodeName { get; } = opCodeName;
	public string Parameters { get; } = parameters;
	public string RawData { get; } = rawData;
	public bool HasParameters => this.Parameters.Length > 0;
}
