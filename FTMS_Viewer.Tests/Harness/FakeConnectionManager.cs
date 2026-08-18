namespace FTMS_Viewer.Tests.Harness;

using DynamicData;
using FTMS.NET;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.FTMS;
using System.Reactive.Subjects;

/// <summary>
/// A fake <see cref="IConnectionManager"/> that exposes the current
/// <see cref="IFitnessMachineServiceConnection"/> through a subject, so tests can push
/// connections in and out and the view models observe them through the same seam they use in
/// the app.
/// </summary>
public sealed class FakeConnectionManager : IConnectionManager, IDisposable
{
	private readonly BehaviorSubject<IFitnessMachineServiceConnection?> connectionSubject = new(null);
	private readonly BehaviorSubject<IDevice?> connectedDeviceSubject = new(null);
	private readonly BehaviorSubject<bool> bluetoothAvailabilitySubject = new(true);
	private readonly SourceCache<IDevice, Guid> devicesCache = new(device => device.Id);

	public FakeConnectionManager()
	{
		this.Devices = this.devicesCache.AsObservableCache();
	}

	public IObservableCache<IDevice, Guid> Devices { get; }

	public IDevice? ConnectedDevice => this.connectedDeviceSubject.Value;

	/// <summary>Emits every connection change, including the current connection.</summary>
	public IObservable<IFitnessMachineServiceConnection?> ObserveConnection()
		=> this.connectionSubject;

	/// <summary>Connects the given machine connection and reflects it as the connected device.</summary>
	public void PushConnection(IFitnessMachineServiceConnection? connection)
	{
		this.connectionSubject.OnNext(connection);
		this.connectedDeviceSubject.OnNext(connection is null ? null : new FakeDevice());
	}

	/// <summary>Sets the advertised Bluetooth availability.</summary>
	public void SetBluetoothAvailability(bool available)
		=> this.bluetoothAvailabilitySubject.OnNext(available);

	/// <summary>Adds a device to the scan-result cache.</summary>
	public void AddDevice(IDevice device)
		=> this.devicesCache.AddOrUpdate(device);

	public Task<bool> Connect(Guid deviceId)
		=> Task.FromResult(true);

	public Task Disconnect()
	{
		this.PushConnection(null);
		return Task.CompletedTask;
	}

	public IObservable<bool> ObserveBluetoothAvailability()
		=> this.bluetoothAvailabilitySubject;

	public IObservable<IDevice?> ObserveConnectedDevice()
		=> this.connectedDeviceSubject;

	public IObservable<T> ObserveCurrentServiceConnection<T>(
		Func<IObservable<IFitnessMachineServiceConnection?>, IObservable<T>> sourceFactory,
		int maxExceptionCountTillDisconnect = -1)
		=> sourceFactory(this.connectionSubject);

	public void StartScanning(ScanFilterOptions? scanFilterOptions = null, Func<IDevice, bool>? deviceFilter = null)
	{
	}

	public void StopScanning()
	{
	}

	public void Dispose()
	{
		this.connectionSubject.Dispose();
		this.connectedDeviceSubject.Dispose();
		this.bluetoothAvailabilitySubject.Dispose();
		this.devicesCache.Dispose();
	}
}
