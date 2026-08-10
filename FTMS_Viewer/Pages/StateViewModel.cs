namespace FTMS_Viewer.Pages;

using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;

using FTMS.NET;
using FTMS.NET.Exceptions;
using FTMS.NET.State;

using Microsoft.Extensions.Logging;

using Plugin.BLE.FTMS;

public sealed partial class StateViewModel : ObservableObject, IDisposable
{
	private const string NotConnected = "Not Connected";
	private const int HistoryCapacity = 3;
	private const double HistoryOpacityStep = 0.25;
	private const int MachineStateLogCapacity = 100;

	private static readonly (EStateOpCode OpCode, string Name)[] CurrentTargetRoster =
	[
		(EStateOpCode.TargetSpeedChanged, "Target Speed"),
		(EStateOpCode.TargetInclineChanged, "Target Incline"),
		(EStateOpCode.TargetResistanceLevelChanged, "Target Resistance Level"),
		(EStateOpCode.TargetPowerChanged, "Target Power"),
		(EStateOpCode.TargetHeartRateChanged, "Target Heart Rate"),
		(EStateOpCode.TargetedExpendedEnergyChanged, "Targeted Expended Energy"),
		(EStateOpCode.TargetedNumberOfStepsChanged, "Targeted Number of Steps"),
		(EStateOpCode.TargetedNumberOfStridesChanged, "Targeted Number of Strides"),
		(EStateOpCode.TargetedDistanceChanged, "Targeted Distance"),
		(EStateOpCode.TargetedTrainingTimeChanged, "Targeted Training Time"),
		(EStateOpCode.TargetedTimeInTwoHeartRateZonesChanged, "Targeted Time in Two Heart Rate Zones"),
		(EStateOpCode.TargetedTimeInThreeHeartRateZonesChanged, "Targeted Time in Three Heart Rate Zones"),
		(EStateOpCode.TargetedTimeInFiveHeartRateZonesChanged, "Targeted Time in Five Heart Rate Zones"),
		(EStateOpCode.IndoorBikeSimulationParametersChanged, "Indoor Bike Simulation Parameters"),
		(EStateOpCode.WheelCircumferenceChanged, "Wheel Circumference"),
		(EStateOpCode.TargetedCadenceChanged, "Targeted Cadence"),
	];

	private readonly ILogger<StateViewModel> logger;
	private readonly IDisposable cleanUp;
	private readonly CompositeDisposable currentProviderSubscriptions = new();
	private readonly List<ETrainingState> previousStates = [];
	private readonly Dictionary<EStateOpCode, CurrentTargetItem> currentTargetIndex = [];

	private ETrainingState? currentState;

	[ObservableProperty]
	public partial string Status { get; private set; } = NotConnected;

	[ObservableProperty]
	public partial bool IsConnected { get; private set; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsDetailsVisible))]
	public partial string CurrentDetails { get; private set; } = string.Empty;

	[ObservableProperty]
	public partial bool IsMachineStateNotSupported { get; private set; }

	[ObservableProperty]
	public partial bool IsTrainingStateNotSupported { get; private set; }

	public ObservableCollection<TrainingStateTileItem> TrainingStateHistory { get; } = [];

	public ObservableCollection<MachineStateLogItem> MachineStateLog { get; } = [];

	public ObservableCollection<CurrentTargetItem> CurrentTargets { get; } = [];

	public bool IsDetailsVisible => !string.IsNullOrEmpty(this.CurrentDetails);

	public StateViewModel(IConnectionManager connectionManager, ILogger<StateViewModel> logger)
	{
		foreach (var (opCode, name) in CurrentTargetRoster)
		{
			var item = new CurrentTargetItem(name);
			this.CurrentTargets.Add(item);
			this.currentTargetIndex.Add(opCode, item);
		}

		this.logger = logger;
		this.cleanUp = connectionManager.ObserveCurrentServiceConnection(
				s => s
					.Select(CreateStateProviderObservable)
					.Switch(),
				10)
			.ObserveOn(SynchronizationContext.Current!)
			.Subscribe(
				this.HandleStateProvider,
				ex => this.LogErrorObservingCurrentServiceState(ex));
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
		this.LogReceivedMachineState(state.OpCode);
		this.UpdateCurrentTarget(state);
		this.AppendMachineStateLogEntry(state);
	}

	private void UpdateCurrentTarget(IFitnessMachineState state)
	{
		if (!this.currentTargetIndex.TryGetValue(state.OpCode, out var item))
			return;

		var parameters = ReadParametersSafe(state);
		if (parameters is null)
		{
			this.LogFailedToDecodeMachineStateParameters(state.OpCode);
			return;
		}

		item.SetParameters(string.Join("\n", parameters.Select(FormatParameter)));
	}

	private void AppendMachineStateLogEntry(IFitnessMachineState state)
	{
		string opCodeName = state.OpCode.ToString().AddSpacesBetweenWords();
		var parameters = ReadParametersSafe(state);
		string parameterText = string.Empty;
		if (parameters is null)
		{
			this.LogReceivedUnknownMachineState(state.OpCode);
			opCodeName = $"Unknown Opcode (0x{(byte)state.OpCode:X2})";
		}
		else
		{
			parameterText = string.Join(", ", parameters.Select(FormatParameter));
		}

		this.MachineStateLog.Add(new MachineStateLogItem(opCodeName, parameterText, FormatRawData(state.OpCode, state.RawData)));
		if (this.MachineStateLog.Count > MachineStateLogCapacity)
			this.MachineStateLog.RemoveAt(0);
	}

	private static IEnumerable<object>? ReadParametersSafe(IFitnessMachineState state)
	{
		try
		{
			return state.ReadParameters();
		}
		catch (KeyNotFoundException)
		{
			return null;
		}
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
		this.LogReceivedTrainingState(state.State);
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
	{
		if (ex is NeededCharacteristicNotAvailableException)
		{
			this.IsMachineStateNotSupported = true;
			this.LogMachineStateNotSupported(ex);
			return;
		}

		this.LogErrorObservingMachineState(ex);
	}

	private void HandleTrainingStateError(Exception ex)
	{
		if (ex is NeededCharacteristicNotAvailableException)
		{
			this.IsTrainingStateNotSupported = true;
			this.LogTrainingStateNotSupported(ex);
			return;
		}

		this.LogErrorObservingTrainingState(ex);
	}

	private void ResetState()
	{
		this.IsConnected = false;
		this.IsMachineStateNotSupported = false;
		this.IsTrainingStateNotSupported = false;
		this.Status = NotConnected;
		this.CurrentDetails = string.Empty;
		this.currentState = null;
		this.previousStates.Clear();
		this.TrainingStateHistory.Clear();
		this.MachineStateLog.Clear();
		foreach (var item in this.CurrentTargets)
			item.Reset();
		this.currentProviderSubscriptions.Clear();
	}

	public void Dispose()
	{
		this.currentProviderSubscriptions.Dispose();
		this.cleanUp.Dispose();
	}

	[LoggerMessage(LogLevel.Error, "Error during observing current service state! Stopping...")]
	private partial void LogErrorObservingCurrentServiceState(Exception ex);

	[LoggerMessage(LogLevel.Debug, "Received machine state notification: {OpCode}")]
	private partial void LogReceivedMachineState(EStateOpCode opCode);

	[LoggerMessage(LogLevel.Warning, "Failed to decode machine-state parameters for opcode {OpCode}")]
	private partial void LogFailedToDecodeMachineStateParameters(EStateOpCode opCode);

	[LoggerMessage(LogLevel.Warning, "Received machine-state notification with unknown opcode {OpCode}")]
	private partial void LogReceivedUnknownMachineState(EStateOpCode opCode);

	[LoggerMessage(LogLevel.Debug, "Received training state notification: {State}")]
	private partial void LogReceivedTrainingState(ETrainingState state);

	[LoggerMessage(LogLevel.Error, "Machine state is not supported by the connected machine; showing 'Not Supported' banner.")]
	private partial void LogMachineStateNotSupported(Exception ex);

	[LoggerMessage(LogLevel.Error, "Error during observing machine state! Stopping machine-state stream...")]
	private partial void LogErrorObservingMachineState(Exception ex);

	[LoggerMessage(LogLevel.Error, "Training state is not supported by the connected machine; showing 'Not Supported' banner.")]
	private partial void LogTrainingStateNotSupported(Exception ex);

	[LoggerMessage(LogLevel.Error, "Error during observing training state! Stopping training-state stream...")]
	private partial void LogErrorObservingTrainingState(Exception ex);
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

public sealed partial class CurrentTargetItem(string name) : ObservableObject
{
	private const string UnsetText = "unset";

	public string Name { get; } = name;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(DisplayValue))]
	public partial string? Parameters { get; private set;}

	public string DisplayValue => this.Parameters ?? UnsetText;

	public void SetParameters(string value) => this.Parameters = value;

	public void Reset() => this.Parameters = null;
}
