namespace FTMS_Viewer.Pages;

using System;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using FTMS.NET;
using FTMS.NET.Control;

using Microsoft.Extensions.Logging;

using Plugin.BLE.FTMS;

public sealed partial class ControlViewModel : ObservableObject, IDisposable
{
	private readonly IDisposable cleanUp;
	private readonly ILogger<ControlViewModel> logger;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(this.IsConnected))]
	[NotifyCanExecuteChangedFor(nameof(this.RequestControlCommand))]
	[NotifyCanExecuteChangedFor(nameof(this.ResetCommand))]
	[NotifyCanExecuteChangedFor(nameof(this.StartOrResumeCommand))]
	[NotifyCanExecuteChangedFor(nameof(this.StopCommand))]
	[NotifyCanExecuteChangedFor(nameof(this.PauseCommand))]
	[NotifyCanExecuteChangedFor(nameof(this.SetTargetPowerCommand))]
	private partial IFitnessMachineControl? Control { get; set; }

	public bool IsConnected => this.Control is not null;

	public ControlViewModel(IConnectionManager connectionManager, ILogger<ControlViewModel> logger)
	{
		this.logger = logger;
		this.cleanUp = connectionManager.ObserveCurrentServiceConnection(
				s => s.SelectMany(async c => c is not null ? await c.CreateFitnessMachineControlAsync() : null),
				10)
			.ObserveOn(SynchronizationContext.Current!)
			.Subscribe(control => this.Control = control);
	}

	[RelayCommand(CanExecute = nameof(this.CanSendRequest))]
	private async Task RequestControlAsync()
	{
		await this.RunSafely(this.Control!.RequestControl);
	}

	[RelayCommand(CanExecute = nameof(this.CanSendRequest))]
	private async Task ResetAsync()
	{
		await this.RunSafely(this.Control!.Reset);
	}

	[RelayCommand(CanExecute = nameof(this.CanSendRequest))]
	private async Task StartOrResumeAsync()
	{
		await this.RunSafely(this.Control!.StartOrResume);
	}

	[RelayCommand(CanExecute = nameof(this.CanSendRequest))]
	private async Task StopAsync()
	{
		await this.RunSafely(this.Control!.Stop);
	}

	[RelayCommand(CanExecute = nameof(this.CanSendRequest))]
	private async Task PauseAsync()
	{
		await this.RunSafely(this.Control!.Pause);
	}

	[RelayCommand(CanExecute = nameof(this.CanSendRequest))]
	private async Task SetTargetPowerAsync(short power)
	{
		await this.RunSafely(() => this.Control!.SetTargetPower(power));
	}

	private bool CanSendRequest() => this.IsConnected;

	public void Dispose()
	{
		this.cleanUp.Dispose();
	}

	private async Task RunSafely(Func<Task> run, [CallerMemberName] string callerName = "No name set")
	{
		try
		{
			await run();
		}
		catch (Exception ex)
		{
			string commandName = callerName.Replace("Async", string.Empty);
			this.LogErrorExecutingCommand(commandName, ex);
		}
	}

	[LoggerMessage(LogLevel.Error, "Error while executing command: {CommandName}")]
	private partial void LogErrorExecutingCommand(string commandName, Exception ex);
}
