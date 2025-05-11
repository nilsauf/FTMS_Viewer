namespace FTMS_Viewer.Pages;

using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DynamicData;
using DynamicData.Binding;

using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.FTMS;

public sealed partial class ConnectionViewModel : ObservableObject, IDisposable
{
	private readonly ObservableCollectionExtended<IDevice> devices = [];
	private readonly CompositeDisposable cleanUp;
	private readonly IConnectionManager connectionManager;

	public IObservableCollection<IDevice> Devices => this.devices;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(this.UserPrompt))]
	[NotifyPropertyChangedFor(nameof(this.SelectionPossible))]
	public partial bool BluetoothAvailable { get; set; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(this.UserPrompt))]
	[NotifyCanExecuteChangedFor(nameof(this.DisconnectCommand))]
	[NotifyCanExecuteChangedFor(nameof(this.ConnectCommand))]
	public partial bool Connected { get; set; }

	public string UserPrompt => this.BluetoothAvailable ?
		this.connectionManager.ConnectedDevice is null ?
			"Select to Connect:" :
			$"Connected to {this.connectionManager.ConnectedDevice.Name}" :
		"Please turn Bluetooth on!";

	[ObservableProperty]
	public partial bool SelectionPossible { get; set; } = true;

	public ConnectionViewModel(IConnectionManager connectionManager)
	{
		this.connectionManager = connectionManager;
		this.cleanUp = new CompositeDisposable(
			this.connectionManager.Devices
				.Connect()
				.SortBy(d => d.Rssi)
				.FilterOnObservable(d => this.connectionManager.ObserveConnectedDevice()
					.Select(cd => cd is null || cd.Id != d.Id))
				.ObserveOn(SynchronizationContext.Current!)
				.Bind(this.devices)
				.Subscribe(),
			this.connectionManager.ObserveBluetoothAvailability()
				.ObserveOn(SynchronizationContext.Current!)
				.Subscribe(ble => this.BluetoothAvailable = ble),
			this.connectionManager.ObserveConnectedDevice()
				.Select(d => d is not null)
				.CombineLatest(this.connectionManager.ObserveBluetoothAvailability(),
					(connected, ble) => connected && ble)
				.ObserveOn(SynchronizationContext.Current!)
				.Subscribe(connected => this.Connected = connected));
	}

	[RelayCommand(CanExecute = nameof(CanConnect))]
	private async Task Connect(IDevice device)
	{
		if (this.connectionManager.ConnectedDevice is not null)
		{
			await this.Disconnect();
		}
		await this.connectionManager.Connect(device.Id);
	}

	private bool CanConnect(IDevice device)
		=> device is not null && this.SelectionPossible && this.Connected is false;

	[RelayCommand(CanExecute = nameof(CanDisconnect))]
	private Task Disconnect()
	{
		return this.connectionManager.Disconnect();
	}

	private bool CanDisconnect()
		=> this.Connected;

	public void StartScanning()
	{
		this.connectionManager.StartScanning(ServiceScanFilter.FitnessMachineService);
		this.SelectionPossible = true;
	}

	public void StopScanning()
	{
		this.SelectionPossible = false;
		this.connectionManager.StopScanning();
	}

	public async Task EnsurePermissionsAsync()
	{
		var result = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();
		if (result is not PermissionStatus.Granted)
		{
			await Permissions.RequestAsync<Permissions.Bluetooth>();
		}
	}

	public void Dispose()
	{
		this.cleanUp.Dispose();
		this.devices.Clear();
	}
}
