namespace FTMS_Viewer.Pages;

using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using FTMS.NET;
using FTMS.NET.Control;
using FTMS.NET.Exceptions;
using FTMS.NET.State;

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

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsConnected))]
	[NotifyCanExecuteChangedFor(nameof(RequestControlCommand))]
	[NotifyCanExecuteChangedFor(nameof(ResetCommand))]
	[NotifyCanExecuteChangedFor(nameof(StartOrResumeCommand))]
	[NotifyCanExecuteChangedFor(nameof(StopCommand))]
	[NotifyCanExecuteChangedFor(nameof(PauseCommand))]
	private partial IFitnessMachineControl? Control { get; set; }

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

	private async Task<(IFitnessMachineControl Control, IFitnessMachineStateProvider Provider)?> CreateSessionAsync(
		IFitnessMachineServiceConnection? connection)
	{
		if (connection is null)
			return null;

		IFitnessMachineControl control = await connection.CreateFitnessMachineControlAsync();
		IFitnessMachineStateProvider provider = await connection.CreateFitnessMachineStateProviderAsync();
		return (control, provider);
	}

	private void HandleSession((IFitnessMachineControl Control, IFitnessMachineStateProvider Provider)? session)
	{
		this.ResetPageState();

		if (session is not { } s)
			return;

		this.Control = s.Control;
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
		}
	}

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
	}

	private bool CanSendRequest() => this.IsConnected;

	[RelayCommand(CanExecute = nameof(CanSendRequest))]
	private Task RequestControlAsync()
		=> this.SendOperationAsync("Request Control", EControlOpCode.RequestControl, this.Control!.RequestControl);

	[RelayCommand(CanExecute = nameof(CanSendRequest))]
	private Task ResetAsync()
		=> this.SendOperationAsync("Reset", EControlOpCode.Reset, this.Control!.Reset);

	[RelayCommand(CanExecute = nameof(CanSendRequest))]
	private Task StartOrResumeAsync()
		=> this.SendOperationAsync("Start/Resume", EControlOpCode.StartOrResume, this.Control!.StartOrResume);

	[RelayCommand(CanExecute = nameof(CanSendRequest))]
	private Task StopAsync()
		=> this.SendOperationAsync("Stop", EControlOpCode.StopOrPause, this.Control!.Stop);

	[RelayCommand(CanExecute = nameof(CanSendRequest))]
	private Task PauseAsync()
		=> this.SendOperationAsync("Pause", EControlOpCode.StopOrPause, this.Control!.Pause);

	private async Task SendOperationAsync(
		string name,
		EControlOpCode opCode,
		Func<Task> send)
	{
		this.LogSendingControlRequest(opCode);

		try
		{
			await send();

			this.Status = $"{name}: Success";
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

public abstract class ControlItemViewModel(string name)
{
	public string Name { get; } = name;

	public abstract ICommand Command { get; }
}

public sealed class ControlOperationItem(string name, ICommand command) : ControlItemViewModel(name)
{
	public override ICommand Command { get; } = command;
}

public sealed class ControlGroupViewModel(string title)
{
	public string Title { get; } = title;

	public ObservableCollection<ControlItemViewModel> Items { get; } = [];
}
