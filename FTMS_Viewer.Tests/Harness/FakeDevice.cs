namespace FTMS_Viewer.Tests.Harness;

using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;

/// <summary>
/// A minimal fake <see cref="IDevice"/> carrying an id, name and RSSI.
/// </summary>
public sealed class FakeDevice : IDevice
{
	public FakeDevice(
		string name = "Fake Machine",
		int rssi = -50,
		Guid? id = null)
	{
		this.Name = name;
		this.Rssi = rssi;
		this.Id = id ?? Guid.NewGuid();
	}

	public Guid Id { get; }

	public string Name { get; }

	public int Rssi { get; }

	public object NativeDevice => new();

	public DeviceState State => DeviceState.Connected;

	public IReadOnlyList<AdvertisementRecord> AdvertisementRecords => [];

	public bool IsConnectable => true;

	public bool SupportsIsConnectable => true;

	public DeviceBondState BondState => DeviceBondState.NotBonded;

	public Task<IReadOnlyList<IService>> GetServicesAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<IService>>([]);

	public Task<IService> GetServiceAsync(Guid id, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public Task<bool> UpdateRssiAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult(true);

	public Task<int> RequestMtuAsync(int requestValue, CancellationToken cancellationToken = default)
		=> Task.FromResult(requestValue);

	public bool UpdateConnectionInterval(ConnectionInterval interval)
		=> true;

	public bool UpdateConnectionParameters(ConnectParameters connectParameters)
		=> true;

	public void ClearServices()
	{
	}

	public void Dispose()
	{
	}
}
