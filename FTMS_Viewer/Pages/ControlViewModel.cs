namespace FTMS_Viewer.Pages;

using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using FTMS.NET;
using FTMS.NET.Control;
using FTMS.NET.Exceptions;
using FTMS.NET.Features;
using FTMS.NET.State;
using FTMS.NET.Utils;

using Microsoft.Extensions.Logging;

using Plugin.BLE.FTMS;

/// <summary>The machine's grant to the app to send control requests.</summary>
public enum ControlPermission
{
	NotRequested,
	Granted,
	Lost
}

/// <summary>
/// The view model driving the Control page: a roster of control operations rendered as cards,
/// a status line reporting the last control and its outcome, and a tri-state control-permission
/// indicator. Lives as a DI singleton so entered state survives reconnects; on each new
/// connection it re-creates the control and re-subscribes to machine state.
/// </summary>
public sealed partial class ControlViewModel : ObservableObject, IDisposable
{
	private const string NotConnected = "Not Connected";

	private readonly ILogger<ControlViewModel> logger;
	private readonly IToastService toastService;
	private readonly IDisposable cleanUp;
	private readonly CompositeDisposable currentProviderSubscriptions = new();
	private readonly List<TargetValueItem> targetValueItems = [];
	private readonly List<IAsyncRelayCommand> targetValueCommands = [];
	private readonly List<MultiValueTargetItem> multiValueItems = [];
	private readonly List<IAsyncRelayCommand> multiValueCommands = [];
	private readonly List<SimulationTargetItem> simulationItems = [];
	private readonly List<IAsyncRelayCommand> simulationCommands = [];
	private readonly List<SpinDownItem> spinDownItems = [];
	private readonly List<IAsyncRelayCommand> spinDownCommands = [];
	private IFitnessMachineFeatures? features;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsConnected))]
	[NotifyCanExecuteChangedFor(nameof(RequestControlCommand))]
	[NotifyCanExecuteChangedFor(nameof(ResetCommand))]
	[NotifyCanExecuteChangedFor(nameof(StartOrResumeCommand))]
	[NotifyCanExecuteChangedFor(nameof(StopCommand))]
	[NotifyCanExecuteChangedFor(nameof(PauseCommand))]
	private partial IFitnessMachineControl? Control { get; set; }

	partial void OnControlChanged(IFitnessMachineControl? value)
	{
		foreach (var command in this.targetValueCommands)
			command.NotifyCanExecuteChanged();
		foreach (var command in this.multiValueCommands)
			command.NotifyCanExecuteChanged();
		foreach (var command in this.simulationCommands)
			command.NotifyCanExecuteChanged();
		foreach (var command in this.spinDownCommands)
			command.NotifyCanExecuteChanged();
	}

	public bool IsConnected => this.Control is not null;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(PermissionText))]
	[NotifyPropertyChangedFor(nameof(PermissionColor))]
	public partial ControlPermission Permission { get; private set; } = ControlPermission.NotRequested;

	public string PermissionText => this.Permission switch
	{
		ControlPermission.Granted => "Granted",
		ControlPermission.Lost => "Lost",
		_ => "Not Requested",
	};

	public Color PermissionColor => this.Permission switch
	{
		ControlPermission.Granted => Colors.Green,
		ControlPermission.Lost => Colors.Red,
		_ => Colors.Gray,
	};

	[ObservableProperty]
	public partial string Status { get; private set; } = NotConnected;

	/// <summary>
	/// When false (the default), an entered value is clamped to the machine's advertised range and
	/// snapped to its increment before sending; when true, the value is sent exactly as entered.
	/// Only applies to the ranged target settings.
	/// </summary>
	[ObservableProperty]
	public partial bool IsAllowOutOfRange { get; set; }

	public ObservableCollection<ControlGroupViewModel> ControlGroups { get; } = [];

	public ControlViewModel(
		IConnectionManager connectionManager,
		ILogger<ControlViewModel> logger,
		IToastService toastService)
	{
		this.logger = logger;
		this.toastService = toastService;

		this.BuildControlRoster();

		this.cleanUp = connectionManager.ObserveCurrentServiceConnection(
				s => s.SelectMany(this.CreateSessionAsync),
				10)
			.ObserveOn(SynchronizationContext.Current!)
			.Subscribe(
				this.HandleSession,
				ex => this.LogErrorObservingCurrentServiceConnection(ex));
	}

	private async Task<(IFitnessMachineControl Control, IFitnessMachineStateProvider Provider, IFitnessMachineFeatures? Features)?> CreateSessionAsync(
		IFitnessMachineServiceConnection? connection)
	{
		if (connection is null)
			return null;

		IFitnessMachineControl control = await connection.CreateFitnessMachineControlAsync();
		IFitnessMachineStateProvider provider = await connection.CreateFitnessMachineStateProviderAsync();
		IFitnessMachineFeatures? features = await TryReadFeaturesAsync(connection);
		return (control, provider, features);
	}

	private async Task<IFitnessMachineFeatures?> TryReadFeaturesAsync(IFitnessMachineServiceConnection connection)
	{
		try
		{
			return await connection.ReadFitnessMachineFeaturesAsync();
		}
		catch (Exception ex)
		{
			this.LogFailedToReadFeatures(ex);
			return null;
		}
	}

	private void HandleSession((IFitnessMachineControl Control, IFitnessMachineStateProvider Provider, IFitnessMachineFeatures? Features)? session)
	{
		this.ResetPageState();

		if (session is not { } s)
			return;

		this.Control = s.Control;
		this.features = s.Features;
		this.ApplyFeatures();
		this.Status = string.Empty;

		this.currentProviderSubscriptions.Add(
			s.Provider.ObserveMachineState()
				.ObserveOn(SynchronizationContext.Current!)
				.Subscribe(this.HandleMachineState, this.HandleMachineStateError));
	}

	private void HandleMachineState(IFitnessMachineState state)
	{
		if (state.OpCode == EStateOpCode.ControlPermissionLost)
		{
			this.Permission = ControlPermission.Lost;
			this.Status = "Control permission lost";
			this.LogControlPermissionLost();
			return;
		}

		this.PrefillTarget(state);
	}

	private void PrefillTarget(IFitnessMachineState state)
	{
		TargetValueItem? item = this.targetValueItems
			.FirstOrDefault(i => i.PrefillOpCode == state.OpCode);
		if (item is null || item.IsDirty)
			return;

		var parameter = ReadFirstParameter(state);
		if (parameter is null)
			return;

		item.Prefill(FormatValue(parameter.Value));
	}

	private static FitnessMachineStateParameter? ReadFirstParameter(IFitnessMachineState state)
	{
		try
		{
			return state.ReadParameters()
				.OfType<FitnessMachineStateParameter>()
				.FirstOrDefault();
		}
		catch (KeyNotFoundException)
		{
			return null;
		}
	}

	private static string FormatValue(double value)
		=> value.ToString("0.###", CultureInfo.InvariantCulture);

	/// <summary>Formats a raw spin-down target speed (km/h × 100) in km/h.</summary>
	private static string FormatTargetSpeed(ushort rawValue)
		=> (rawValue / 100.0).ToString("0.#", CultureInfo.InvariantCulture);

	private void HandleMachineStateError(Exception ex)
	{
		if (ex is NeededCharacteristicNotAvailableException)
		{
			this.LogMachineStateNotSupported(ex);
			return;
		}

		this.LogErrorObservingMachineState(ex);
	}

	private void BuildControlRoster()
	{
		var controlGroup = new ControlGroupViewModel("Control");
		controlGroup.Items.Add(new ControlOperationItem("Request Control", this.RequestControlCommand));
		controlGroup.Items.Add(new ControlOperationItem("Reset", this.ResetCommand));
		controlGroup.Items.Add(new ControlOperationItem("Start/Resume", this.StartOrResumeCommand));
		controlGroup.Items.Add(new ControlOperationItem("Stop", this.StopCommand));
		controlGroup.Items.Add(new ControlOperationItem("Pause", this.PauseCommand));
		this.ControlGroups.Add(controlGroup);

		var targetsGroup = new ControlGroupViewModel("Targets");
		targetsGroup.Items.Add(this.CreateTargetValueItem("Target Speed", "km/h", EControlOpCode.SetTargetSpeed, EStateOpCode.TargetSpeedChanged, 100, TargetValueShape.UInt16,
			f => f.SpeedTargetSettingSupported, f => f.SpeedRange));
		targetsGroup.Items.Add(this.CreateTargetValueItem("Target Incline", "%", EControlOpCode.SetTargetInclination, EStateOpCode.TargetInclineChanged, 10, TargetValueShape.Int16,
			f => f.InclinationTargetSettingSupported, f => f.InclinationRange));
		targetsGroup.Items.Add(this.CreateTargetValueItem("Target Resistance Level", string.Empty, EControlOpCode.SetTargetResistanceLevel, EStateOpCode.TargetResistanceLevelChanged, 1, TargetValueShape.Byte,
			f => f.ResistanceTargetSettingSupported, f => f.ResistanceLevelRange));
		targetsGroup.Items.Add(this.CreateTargetValueItem("Target Power", "W", EControlOpCode.SetTargetPower, EStateOpCode.TargetPowerChanged, 1, TargetValueShape.Int16,
			f => f.PowerTargetSettingSupported, f => f.PowerRange));
		targetsGroup.Items.Add(this.CreateTargetValueItem("Target Heart Rate", "bpm", EControlOpCode.SetTargetHeartRate, EStateOpCode.TargetHeartRateChanged, 1, TargetValueShape.Byte,
			f => f.HeartRateTargetSettingSupported, f => f.HeartRateRange));
		targetsGroup.Items.Add(this.CreateTargetValueItem("Target Cadence", "rpm", EControlOpCode.SetTargetedCadence, EStateOpCode.TargetedCadenceChanged, 2, TargetValueShape.UInt16,
			f => f.TargetedCadenceConfigurationSupported, f => null));
		this.ControlGroups.Add(targetsGroup);

		var workoutTargetsGroup = new ControlGroupViewModel("Workout Targets");
		workoutTargetsGroup.Items.Add(this.CreateTargetValueItem("Targeted Expended Energy", "kcal", EControlOpCode.SetTargetedExpendedEnergy, EStateOpCode.TargetedExpendedEnergyChanged, 1, TargetValueShape.UInt16,
			f => f.TargetedExpendedEnergyConfigurationSupported, f => null));
		workoutTargetsGroup.Items.Add(this.CreateTargetValueItem("Targeted Steps", string.Empty, EControlOpCode.SetTargetedNumberOfSteps, EStateOpCode.TargetedNumberOfStepsChanged, 1, TargetValueShape.UInt16,
			f => f.TargetedStepNumberConfigurationSupported, f => null));
		workoutTargetsGroup.Items.Add(this.CreateTargetValueItem("Targeted Strides", string.Empty, EControlOpCode.SetTargetedNumberOfStrides, EStateOpCode.TargetedNumberOfStridesChanged, 1, TargetValueShape.UInt16,
			f => f.TargetedStrideNumberConfigurationSupported, f => null));
		workoutTargetsGroup.Items.Add(this.CreateTargetValueItem("Targeted Distance", "m", EControlOpCode.SetTargetedDistance, EStateOpCode.TargetedDistanceChanged, 1, TargetValueShape.UInt24,
			f => f.TargetedDistanceConfigurationSupported, f => null));
		workoutTargetsGroup.Items.Add(this.CreateTargetValueItem("Targeted Training Time", "s", EControlOpCode.SetTargetedTrainingTime, EStateOpCode.TargetedTrainingTimeChanged, 1, TargetValueShape.UInt16,
			f => f.TargetedTrainingTimeConfigurationSupported, f => null));
		workoutTargetsGroup.Items.Add(this.CreateMultiValueItem("Time in 2 HR Zones", "s", EControlOpCode.SetTargetedTimeInTwoHeartRateZones,
			f => f.TargetedTimeInTwoHeartRateZonesConfigurationSupported, "Fat Burn", "Fitness"));
		workoutTargetsGroup.Items.Add(this.CreateMultiValueItem("Time in 3 HR Zones", "s", EControlOpCode.SetTargetedTimeInThreeHeartRateZones,
			f => f.TargetedTimeInThreeHeartRateZonesConfigurationSupported, "Light", "Moderate", "Hard"));
		workoutTargetsGroup.Items.Add(this.CreateMultiValueItem("Time in 5 HR Zones", "s", EControlOpCode.SetTargetedTimeInFiveHeartRateZones,
			f => f.TargetedTimeInFiveHeartRateZonesConfigurationSupported, "Very Light", "Light", "Moderate", "Hard", "Maximum"));
		this.ControlGroups.Add(workoutTargetsGroup);

		var bikeGroup = new ControlGroupViewModel("Bike");
		bikeGroup.Items.Add(this.CreateSimulationItem(
			"Indoor Bike Simulation",
			f => f.IndoorBikeSimulationParametersSupported,
			new SimulationTargetEntry("Wind Speed", "m/s", 1000, TargetValueShape.Int16),
			new SimulationTargetEntry("Grade", "%", 100, TargetValueShape.Int16),
			new SimulationTargetEntry("Rolling Resistance", string.Empty, 10000, TargetValueShape.Byte),
			new SimulationTargetEntry("Wind Resistance", "kg/m", 100, TargetValueShape.Byte)));
		bikeGroup.Items.Add(this.CreateTargetValueItem("Wheel Circumference", "mm", EControlOpCode.SetWheelCircumference, EStateOpCode.WheelCircumferenceChanged, 10, TargetValueShape.UInt16,
			f => f.WheelCircumferenceConfigurationSupported, f => null));
		bikeGroup.Items.Add(this.CreateSpinDownItem("Spin-Down", f => f.SpinDownControlSupported));
		this.ControlGroups.Add(bikeGroup);
	}

	private TargetValueItem CreateTargetValueItem(
		string name,
		string unit,
		EControlOpCode opCode,
		EStateOpCode prefillOpCode,
		double factor,
		TargetValueShape shape,
		Func<IFitnessMachineFeatures, bool> isSupported,
		Func<IFitnessMachineFeatures, ISupportedRange?> getRange)
	{
		TargetValueItem? item = null;
		var command = new AsyncRelayCommand(
			() => this.SendTargetValueAsync(item!),
			this.CanSendRequest);
		item = new TargetValueItem(
			name, unit, opCode, prefillOpCode, factor, shape, isSupported, getRange, command);
		this.targetValueItems.Add(item);
		this.targetValueCommands.Add(command);
		return item;
	}

	private MultiValueTargetItem CreateMultiValueItem(
		string name,
		string unit,
		EControlOpCode opCode,
		Func<IFitnessMachineFeatures, bool> isSupported,
		params string[] entryLabels)
	{
		MultiValueTargetItem? item = null;
		var command = new AsyncRelayCommand(
			() => this.SendMultiValueAsync(item!),
			this.CanSendRequest);
		item = new MultiValueTargetItem(name, unit, opCode, isSupported, command, entryLabels);
		this.multiValueItems.Add(item);
		this.multiValueCommands.Add(command);
		return item;
	}

	private SimulationTargetItem CreateSimulationItem(
		string name,
		Func<IFitnessMachineFeatures, bool> isSupported,
		params SimulationTargetEntry[] entries)
	{
		SimulationTargetItem? item = null;
		var command = new AsyncRelayCommand(
			() => this.SendSimulationAsync(item!),
			this.CanSendRequest);
		item = new SimulationTargetItem(name, isSupported, command, entries);
		this.simulationItems.Add(item);
		this.simulationCommands.Add(command);
		return item;
	}

	private SpinDownItem CreateSpinDownItem(
		string name,
		Func<IFitnessMachineFeatures, bool> isSupported)
	{
		SpinDownItem? item = null;
		var startCommand = new AsyncRelayCommand(this.SendSpinDownStartAsync, this.CanSendRequest);
		var ignoreCommand = new AsyncRelayCommand(this.SendSpinDownIgnoreAsync, this.CanSendRequest);
		item = new SpinDownItem(name, isSupported, startCommand, ignoreCommand);
		this.spinDownItems.Add(item);
		this.spinDownCommands.Add(startCommand);
		this.spinDownCommands.Add(ignoreCommand);
		return item;
	}

	private void ApplyFeatures()
	{
		foreach (var item in this.targetValueItems)
			item.ApplyFeatures(this.features);
		foreach (var item in this.multiValueItems)
			item.ApplyFeatures(this.features);
		foreach (var item in this.simulationItems)
			item.ApplyFeatures(this.features);
		foreach (var item in this.spinDownItems)
			item.ApplyFeatures(this.features);
	}

	private bool CanSendRequest() => this.IsConnected;

	[RelayCommand(CanExecute = nameof(CanSendRequest))]
	private Task RequestControlAsync()
		=> this.SendRequestAsync("Request Control", EControlOpCode.RequestControl, this.Control!.RequestControl);

	[RelayCommand(CanExecute = nameof(CanSendRequest))]
	private Task ResetAsync()
		=> this.SendRequestAsync("Reset", EControlOpCode.Reset, this.Control!.Reset);

	[RelayCommand(CanExecute = nameof(CanSendRequest))]
	private Task StartOrResumeAsync()
		=> this.SendRequestAsync("Start/Resume", EControlOpCode.StartOrResume, this.Control!.StartOrResume);

	[RelayCommand(CanExecute = nameof(CanSendRequest))]
	private Task StopAsync()
		=> this.SendRequestAsync("Stop", EControlOpCode.StopOrPause, this.Control!.Stop);

	[RelayCommand(CanExecute = nameof(CanSendRequest))]
	private Task PauseAsync()
		=> this.SendRequestAsync("Pause", EControlOpCode.StopOrPause, this.Control!.Pause);

	private async Task SendTargetValueAsync(TargetValueItem item)
	{
		double? humanValue = await this.TryParseHumanValueAsync(item.Name, item.Value);
		if (humanValue is not { } parsed)
			return;

		if (this.IsAllowOutOfRange is false && item.Range is { } range)
			parsed = ClampAndSnap(range, parsed);

		double rawValue = item.Encode(parsed);

		await this.SendRequestAsync(
			item.Name,
			item.OpCode,
			() => SendTargetSettingAsync(this.Control!, item.OpCode, rawValue),
			() => item.IsDirty = false);
	}

	private async Task SendMultiValueAsync(MultiValueTargetItem item)
	{
		var values = new List<ushort>(item.Entries.Count);
		foreach (var entry in item.Entries)
		{
			string fieldName = $"{item.Name} ({entry.Label})";
			double? humanValue = await this.TryParseHumanValueAsync(fieldName, entry.Value);
			if (humanValue is not { } parsed)
				return;

			values.Add((ushort)item.Encode(parsed));
		}

		await this.SendRequestAsync(
			item.Name,
			item.OpCode,
			() => SendZoneTimeSettingAsync(this.Control!, item.OpCode, values));
	}

	private async Task SendSimulationAsync(SimulationTargetItem item)
	{
		var values = new List<double>(item.Entries.Count);
		foreach (var entry in item.Entries)
		{
			string fieldName = $"{item.Name} ({entry.Label})";
			double? humanValue = await this.TryParseHumanValueAsync(fieldName, entry.Value);
			if (humanValue is not { } parsed)
				return;

			values.Add(entry.Encode(parsed));
		}

		await this.SendRequestAsync(
			item.Name,
			EControlOpCode.SetIndoorBikeSimulationParameters,
			() => SendSimulationSettingAsync(this.Control!, values));
	}

	private async Task SendSpinDownStartAsync()
	{
		(ushort low, ushort high) targetSpeeds = default;
		await this.SendRequestAsync(
			"Spin-Down Start",
			EControlOpCode.SpinDownControl,
			async () => targetSpeeds = await this.Control!.StartSpinDownControl(),
			successStatus: () => $"Spin-Down Start: Success — Target speed {FormatTargetSpeed(targetSpeeds.low)}-{FormatTargetSpeed(targetSpeeds.high)} km/h");
	}

	private Task SendSpinDownIgnoreAsync()
		=> this.SendRequestAsync(
			"Spin-Down Ignore",
			EControlOpCode.SpinDownControl,
			() => this.Control!.IgnoreSpinDownControl());

	/// <summary>
	/// Parses an entered human-unit value, or shows an invalid-value toast and returns null.
	/// </summary>
	private async Task<double?> TryParseHumanValueAsync(string fieldName, string entry)
	{
		if (double.TryParse(entry, NumberStyles.Float, CultureInfo.InvariantCulture, out double humanValue))
			return humanValue;

		this.LogInvalidTargetValue(fieldName, entry);
		string message = $"{fieldName}: Invalid value";
		this.Status = message;
		await this.toastService.ShowAsync(message);
		return null;
	}

	private async Task SendRequestAsync(
		string name,
		EControlOpCode opCode,
		Func<Task> send,
		Action? onSuccess = null,
		Func<string>? successStatus = null)
	{
		this.LogSendingControlRequest(opCode);

		try
		{
			await send();

			onSuccess?.Invoke();

			this.Status = successStatus?.Invoke() ?? $"{name}: Success";
			if (opCode == EControlOpCode.RequestControl)
				this.Permission = ControlPermission.Granted;

			this.LogControlRequestSucceeded(opCode);
		}
		catch (ControlRequestException ex)
		{
			if (GetResultCode(ex) is { } resultCode)
			{
				string message = $"{name}: Rejected — {FormatRejectionReason(resultCode)}";
				this.Status = message;
				await this.toastService.ShowAsync(message);
				this.LogControlRequestRejected(opCode, resultCode);
				return;
			}

			this.LogControlRequestFailed(opCode, ex);
			await this.ShowFailureAsync(name);
		}
		catch (Exception ex)
		{
			this.LogErrorExecutingCommand(name, ex);
			await this.ShowFailureAsync(name);
		}
	}

	private static async Task SendTargetSettingAsync(
		IFitnessMachineControl control,
		EControlOpCode opCode,
		double rawValue)
	{
		switch (opCode)
		{
			case EControlOpCode.SetTargetSpeed:
				await control.SetTargetSpeed((ushort)rawValue);
				break;
			case EControlOpCode.SetTargetInclination:
				await control.SetTargetInclination((short)rawValue);
				break;
			case EControlOpCode.SetTargetResistanceLevel:
				await control.SetTargetResistanceLevel((byte)rawValue);
				break;
			case EControlOpCode.SetTargetPower:
				await control.SetTargetPower((short)rawValue);
				break;
			case EControlOpCode.SetTargetHeartRate:
				await control.SetTargetHeartRate((byte)rawValue);
				break;
			case EControlOpCode.SetTargetedCadence:
				await control.SetTargetedCadence((ushort)rawValue);
				break;
			case EControlOpCode.SetTargetedExpendedEnergy:
				await control.SetTargetedExpendedEnergy((ushort)rawValue);
				break;
			case EControlOpCode.SetTargetedNumberOfSteps:
				await control.SetTargetedNumberOfSteps((ushort)rawValue);
				break;
			case EControlOpCode.SetTargetedNumberOfStrides:
				await control.SetTargetedNumberOfStrides((ushort)rawValue);
				break;
			case EControlOpCode.SetTargetedDistance:
				await control.SetTargetedDistance(new UInt24((uint)rawValue));
				break;
			case EControlOpCode.SetTargetedTrainingTime:
				await control.SetTargetedTrainingTime((ushort)rawValue);
				break;
			case EControlOpCode.SetWheelCircumference:
				await control.SetWheelCircumference((ushort)rawValue);
				break;
			default:
				throw new InvalidOperationException($"Unhandled target-setting op code {opCode}");
		}
	}

	private static async Task SendZoneTimeSettingAsync(
		IFitnessMachineControl control,
		EControlOpCode opCode,
		IReadOnlyList<ushort> values)
	{
		switch (opCode)
		{
			case EControlOpCode.SetTargetedTimeInTwoHeartRateZones:
				await control.SetTargetedTimeInTwoHeartRateZones(values[0], values[1]);
				break;
			case EControlOpCode.SetTargetedTimeInThreeHeartRateZones:
				await control.SetTargetedTimeInThreeHeartRateZones(values[0], values[1], values[2]);
				break;
			case EControlOpCode.SetTargetedTimeInFiveHeartRateZones:
				await control.SetTargetedTimeInFiveHeartRateZones(values[0], values[1], values[2], values[3], values[4]);
				break;
			default:
				throw new InvalidOperationException($"Unhandled zone-time op code {opCode}");
		}
	}

	private static Task SendSimulationSettingAsync(
		IFitnessMachineControl control,
		IReadOnlyList<double> values)
		=> control.SetIndoorBikeSimulationParameters(
			(short)values[0],
			(short)values[1],
			(byte)values[2],
			(byte)values[3]);

	private static double ClampAndSnap(ISupportedRange range, double value)
	{
		double clamped = Math.Clamp(value, range.MinimumValue, range.MaximumValue);
		if (range.MinimumIncrement <= 0)
			return clamped;

		double snapped = range.MinimumValue
			+ Math.Round((clamped - range.MinimumValue) / range.MinimumIncrement) * range.MinimumIncrement;
		return Math.Clamp(snapped, range.MinimumValue, range.MaximumValue);
	}

	private async Task ShowFailureAsync(string name)
	{
		string message = $"{name}: Failed to send control request";
		this.Status = message;
		await this.toastService.ShowAsync(message);
	}

	private static string FormatRejectionReason(EControlResultCode resultCode)
		=> resultCode switch
		{
			EControlResultCode.ControlNotPermitted => "control not permitted",
			EControlResultCode.OpCodeNotSupported => "op code not supported",
			EControlResultCode.InvalidParameter => "invalid parameter",
			EControlResultCode.OperationFailed => "operation failed",
			_ => resultCode.ToString(),
		};

	/// <summary>
	/// <see cref="FitnessMachineControl"/> wraps a rejection into an outer
	/// <see cref="ControlRequestException"/> without a result code, so the code has to be read
	/// from the wrapped inner exception.
	/// </summary>
	private static EControlResultCode? GetResultCode(ControlRequestException ex)
		=> ex.ResultCode ?? (ex.InnerException as ControlRequestException)?.ResultCode;

	private void ResetPageState()
	{
		this.Control = null;
		this.Status = NotConnected;
		this.Permission = ControlPermission.NotRequested;
		this.features = null;
		this.ApplyFeatures();
		this.currentProviderSubscriptions.Clear();
	}

	public void Dispose()
	{
		this.currentProviderSubscriptions.Dispose();
		this.cleanUp.Dispose();
	}

	[LoggerMessage(LogLevel.Error, "Error during observing current service connection! Stopping...")]
	private partial void LogErrorObservingCurrentServiceConnection(Exception ex);

	[LoggerMessage(LogLevel.Debug, "Sending control request with op code {OpCode}")]
	private partial void LogSendingControlRequest(EControlOpCode opCode);

	[LoggerMessage(LogLevel.Debug, "Control request with op code {OpCode} succeeded")]
	private partial void LogControlRequestSucceeded(EControlOpCode opCode);

	[LoggerMessage(LogLevel.Warning, "Could not send the {Name} target setting: '{Value}' is not a valid number")]
	private partial void LogInvalidTargetValue(string name, string value);

	[LoggerMessage(LogLevel.Error, "Failed to read the machine's advertised features; target settings will not be tagged or range-validated.")]
	private partial void LogFailedToReadFeatures(Exception ex);

	[LoggerMessage(LogLevel.Warning, "Control request with op code {OpCode} was rejected with result code {ResultCode}")]
	private partial void LogControlRequestRejected(EControlOpCode opCode, EControlResultCode resultCode);

	[LoggerMessage(LogLevel.Error, "Control request with op code {OpCode} failed")]
	private partial void LogControlRequestFailed(EControlOpCode opCode, Exception ex);

	[LoggerMessage(LogLevel.Warning, "Control permission was lost by the connected machine")]
	private partial void LogControlPermissionLost();

	[LoggerMessage(LogLevel.Error, "Machine state is not supported by the connected machine.")]
	private partial void LogMachineStateNotSupported(Exception ex);

	[LoggerMessage(LogLevel.Error, "Error during observing machine state! Stopping machine-state stream...")]
	private partial void LogErrorObservingMachineState(Exception ex);

	[LoggerMessage(LogLevel.Error, "Error while executing command: {CommandName}")]
	private partial void LogErrorExecutingCommand(string commandName, Exception ex);
}

public abstract class ControlItemViewModel(string name) : ObservableObject
{
	public string Name { get; } = name;

	public abstract ICommand Command { get; }
}

public sealed class ControlOperationItem(string name, ICommand command) : ControlItemViewModel(name)
{
	public override ICommand Command { get; } = command;
}

/// <summary>The raw numeric width a target-setting value is encoded into at send time.</summary>
public enum TargetValueShape
{
	Byte,
	UInt16,
	Int16,
	UInt24,
}

/// <summary>
/// A single-value target-setting card: accepts a human-unit value, encodes it to the machine's
/// raw format at send time, clamps and snaps it against the advertised range unless the
/// "allow out of range" toggle is on, prefills from the machine's current target unless the
/// field is being edited, and tags itself when the machine does not advertise support.
/// </summary>
public sealed partial class TargetValueItem : ControlItemViewModel
{
	private readonly Func<IFitnessMachineFeatures, ISupportedRange?> getRange;
	private readonly Func<IFitnessMachineFeatures, bool> isSupported;

	public TargetValueItem(
		string name,
		string unit,
		EControlOpCode opCode,
		EStateOpCode prefillOpCode,
		double factor,
		TargetValueShape shape,
		Func<IFitnessMachineFeatures, bool> isSupported,
		Func<IFitnessMachineFeatures, ISupportedRange?> getRange,
		ICommand command)
		: base(name)
	{
		this.Unit = unit;
		this.OpCode = opCode;
		this.PrefillOpCode = prefillOpCode;
		this.Factor = factor;
		this.Shape = shape;
		this.isSupported = isSupported;
		this.getRange = getRange;
		this.Command = command;
	}

	public string Unit { get; }

	public EControlOpCode OpCode { get; }

	public EStateOpCode PrefillOpCode { get; }

	public double Factor { get; }

	public TargetValueShape Shape { get; }

	public override ICommand Command { get; }

	/// <summary>The machine's advertised range for this setting, or null when it advertises none.</summary>
	public ISupportedRange? Range { get; private set; }

	/// <summary>The human-unit value the user entered; survives reconnects because it lives here.</summary>
	[ObservableProperty]
	public partial string Value { get; set; } = string.Empty;

	/// <summary>
	/// Set while the user is editing the field, so machine-state prefill does not clobber the
	/// entry; cleared by a successful send so the machine's echo re-arms the field.
	/// </summary>
	[ObservableProperty]
	public partial bool IsDirty { get; set; }

	[ObservableProperty]
	public partial bool IsNotSupported { get; set; }

	private bool prefillGuard;

	/// <summary>Marks the field dirty unless the change came from a prefill.</summary>
	public void MarkDirty()
	{
		if (this.prefillGuard)
			return;

		this.IsDirty = true;
	}

	/// <summary>Applies a machine-state value without counting it as a user edit.</summary>
	public void Prefill(string value)
	{
		this.prefillGuard = true;
		try
		{
			this.Value = value;
		}
		finally
		{
			this.prefillGuard = false;
		}
	}

	public void ApplyFeatures(IFitnessMachineFeatures? features)
	{
		this.Range = features is null ? null : this.getRange(features);
		this.IsNotSupported = features is not null && !this.isSupported(features);
	}

	/// <summary>Encodes a human-unit value into the raw format, bounded by the raw type.</summary>
	public double Encode(double humanValue)
		=> TargetValueEncoding.Encode(humanValue, this.Factor, this.Shape);
}

/// <summary>
/// A multi-value target-setting card (the two/three/five heart-rate zone times): renders one
/// entry field per zone and sends all entered values in a single control request. Starts blank,
/// has no advertised range, and tags itself when the machine does not advertise support.
/// </summary>
public sealed partial class MultiValueTargetItem : ControlItemViewModel
{
	private readonly Func<IFitnessMachineFeatures, bool> isSupported;

	public MultiValueTargetItem(
		string name,
		string unit,
		EControlOpCode opCode,
		Func<IFitnessMachineFeatures, bool> isSupported,
		ICommand command,
		params string[] entryLabels)
		: base(name)
	{
		this.Unit = unit;
		this.OpCode = opCode;
		this.isSupported = isSupported;
		this.Command = command;
		foreach (var label in entryLabels)
			this.Entries.Add(new MultiValueTargetEntry(label));
	}

	public string Unit { get; }

	public EControlOpCode OpCode { get; }

	public override ICommand Command { get; }

	public ObservableCollection<MultiValueTargetEntry> Entries { get; } = [];

	[ObservableProperty]
	public partial bool IsNotSupported { get; set; }

	public void ApplyFeatures(IFitnessMachineFeatures? features)
		=> this.IsNotSupported = features is not null && !this.isSupported(features);

	/// <summary>Encodes a human-unit value into the raw ushort format.</summary>
	public double Encode(double humanValue)
		=> TargetValueEncoding.Encode(humanValue, 1, TargetValueShape.UInt16);
}

/// <summary>One entry field of a <see cref="MultiValueTargetItem"/>.</summary>
public sealed partial class MultiValueTargetEntry(string label) : ObservableObject
{
	public string Label { get; } = label;

	/// <summary>The human-unit value the user entered; survives reconnects because it lives here.</summary>
	[ObservableProperty]
	public partial string Value { get; set; } = string.Empty;
}

/// <summary>
/// The indoor bike simulation card: renders the four simulation parameters (wind speed, grade,
/// rolling-resistance coefficient, wind-resistance coefficient) as entry fields, encodes each in
/// its own spec factor at send time, and sends them in a single control request. Tags itself when
/// the machine does not advertise support; entered values survive reconnects.
/// </summary>
public sealed partial class SimulationTargetItem : ControlItemViewModel
{
	private readonly Func<IFitnessMachineFeatures, bool> isSupported;

	public SimulationTargetItem(
		string name,
		Func<IFitnessMachineFeatures, bool> isSupported,
		ICommand command,
		params SimulationTargetEntry[] entries)
		: base(name)
	{
		this.isSupported = isSupported;
		this.Command = command;
		foreach (var entry in entries)
			this.Entries.Add(entry);
	}

	public override ICommand Command { get; }

	public ObservableCollection<SimulationTargetEntry> Entries { get; } = [];

	[ObservableProperty]
	public partial bool IsNotSupported { get; set; }

	public void ApplyFeatures(IFitnessMachineFeatures? features)
		=> this.IsNotSupported = features is not null && !this.isSupported(features);
}

/// <summary>One entry field of a <see cref="SimulationTargetItem"/>.</summary>
public sealed partial class SimulationTargetEntry : ObservableObject
{
	public SimulationTargetEntry(string label, string unit, double factor, TargetValueShape shape)
	{
		this.Label = label;
		this.Unit = unit;
		this.Factor = factor;
		this.Shape = shape;
	}

	public string Label { get; }

	public string Unit { get; }

	public double Factor { get; }

	public TargetValueShape Shape { get; }

	/// <summary>The human-unit value the user entered; survives reconnects because it lives here.</summary>
	[ObservableProperty]
	public partial string Value { get; set; } = string.Empty;

	/// <summary>Encodes a human-unit value into the raw format, bounded by the raw type.</summary>
	public double Encode(double humanValue)
		=> TargetValueEncoding.Encode(humanValue, this.Factor, this.Shape);
}

/// <summary>
/// The spin-down calibration card: starts the calibration with the start button (the machine
/// answers with the measured target speed bounds, shown in the status line) or abandons it with
/// the ignore button. Tags itself when the machine does not advertise support.
/// </summary>
public sealed partial class SpinDownItem : ControlItemViewModel
{
	private readonly Func<IFitnessMachineFeatures, bool> isSupported;

	public SpinDownItem(
		string name,
		Func<IFitnessMachineFeatures, bool> isSupported,
		ICommand startCommand,
		ICommand ignoreCommand)
		: base(name)
	{
		this.isSupported = isSupported;
		this.StartCommand = startCommand;
		this.IgnoreCommand = ignoreCommand;
	}

	public ICommand StartCommand { get; }

	public ICommand IgnoreCommand { get; }

	[ObservableProperty]
	public partial bool IsNotSupported { get; set; }

	public void ApplyFeatures(IFitnessMachineFeatures? features)
		=> this.IsNotSupported = features is not null && !this.isSupported(features);

	public override ICommand Command => this.StartCommand;
}

/// <summary>Encodes a human-unit value into the raw format for a value shape.</summary>
internal static class TargetValueEncoding
{
	public static double Encode(double humanValue, double factor, TargetValueShape shape)
		=> Math.Clamp(
			Math.Round(humanValue * factor, MidpointRounding.AwayFromZero),
			GetRawBounds(shape).Min,
			GetRawBounds(shape).Max);

	private static (double Min, double Max) GetRawBounds(TargetValueShape shape) => shape switch
	{
		TargetValueShape.Byte => (byte.MinValue, byte.MaxValue),
		TargetValueShape.UInt16 => (ushort.MinValue, ushort.MaxValue),
		TargetValueShape.Int16 => (short.MinValue, short.MaxValue),
		TargetValueShape.UInt24 => (UInt24.MinValue.Value, UInt24.MaxValue.Value),
		_ => throw new ArgumentOutOfRangeException(nameof(shape)),
	};
}

public sealed class ControlGroupViewModel(string title)
{
	public string Title { get; } = title;

	public ObservableCollection<ControlItemViewModel> Items { get; } = [];
}
