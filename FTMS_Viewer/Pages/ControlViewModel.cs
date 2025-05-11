namespace FTMS_Viewer.Pages;

using System;
using System.Reactive.Linq;

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
	private partial IFitnessMachineControl? Control { get; set; }

	public bool IsConnected => this.Control is not null;

	public ControlViewModel(IConnectionManager connectionManager, ILogger<ControlViewModel> logger)
	{
		this.logger = logger;
		this.cleanUp = connectionManager.ObserveCurrentServiceConnection()
			.SelectMany(async c => c is not null ? await c.CreateFitnessMachineControlAsync() : null)
			.RetryAndDisconnect(connectionManager)
			.ObserveOn(SynchronizationContext.Current!)
			.Subscribe(control => this.Control = control);
	}

	[RelayCommand(CanExecute = nameof(this.CanSendRequest))]
	private async Task RequestControlAsync()
	{
		this.RequestControlCommand
		await this.Control!.RequestControl();
	}

	[RelayCommand(CanExecute = nameof(this.CanSendRequest))]
	private async Task ResetAsync()
	{
		await this.Control!.Reset();
	}

	[RelayCommand(CanExecute = nameof(this.CanSendRequest))]
	private async Task StartOrResumeAsync()
	{
		await this.Control!.StartOrResume();
	}

	[RelayCommand(CanExecute = nameof(this.CanSendRequest))]
	private async Task StopAsync()
	{
		await this.Control!.Stop();
	}

	[RelayCommand(CanExecute = nameof(this.CanSendRequest))]
	private async Task PauseAsync()
	{
		await this.Control!.Pause();
	}

	private bool CanSendRequest() => this.IsConnected;

	public void Dispose()
	{
		this.cleanUp.Dispose();
	}
}
