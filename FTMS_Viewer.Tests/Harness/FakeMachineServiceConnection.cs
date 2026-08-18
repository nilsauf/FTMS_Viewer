namespace FTMS_Viewer.Tests.Harness;

using FTMS.NET;

/// <summary>
/// A fake <see cref="IFitnessMachineServiceConnection"/> exposing pre-registered
/// <see cref="FakeFitnessMachineCharacteristic"/>s. Service data is built so the connection is
/// read as an available machine of the configured <see cref="MachineType"/>.
/// </summary>
public sealed class FakeMachineServiceConnection : IFitnessMachineServiceConnection
{
	private readonly Dictionary<Guid, FakeFitnessMachineCharacteristic> characteristics;

	public FakeMachineServiceConnection(
		EFitnessMachineType machineType = EFitnessMachineType.IndoorBike,
		byte[]? serviceData = null)
	{
		this.MachineType = machineType;
		this.ServiceData = serviceData ?? CreateServiceData(machineType);
		this.characteristics = new Dictionary<Guid, FakeFitnessMachineCharacteristic>();

		foreach (var id in AllCharacteristicIds)
			this.characteristics.Add(id, new FakeFitnessMachineCharacteristic(id));
	}

	public EFitnessMachineType MachineType { get; }

	public byte[] ServiceData { get; }

	public FakeFitnessMachineCharacteristic ControlPoint => this[FtmsUuids.ControlPoint];

	public FakeFitnessMachineCharacteristic MachineState => this[FtmsUuids.MachineState];

	public FakeFitnessMachineCharacteristic TrainingState => this[FtmsUuids.TrainingState];

	public FakeFitnessMachineCharacteristic Feature => this[FtmsUuids.Feature];

	/// <summary>Returns the pre-registered fake for the given characteristic id.</summary>
	public FakeFitnessMachineCharacteristic this[Guid id] => this.characteristics[id];

	public Task<IFitnessMachineCharacteristic?> GetCharacteristicAsync(Guid id)
		=> Task.FromResult<IFitnessMachineCharacteristic?>(
			this.characteristics.GetValueOrDefault(id));

	/// <summary>
	/// Builds FTMS service data in the layout the factory decodes: the availability bit is bit 0
	/// of the byte at index 2 (<c>EnsureAvailability</c>) and the machine-type bitmask is the
	/// little-endian <see cref="ushort"/> starting at index 3 (<c>ReadType</c>).
	/// </summary>
	private static byte[] CreateServiceData(EFitnessMachineType machineType)
	{
		byte[] data = new byte[5];
		data[2] = 0x01;
		ushort typeBits = (ushort)(1 << (int)machineType);
		data[3] = (byte)typeBits;
		data[4] = (byte)(typeBits >> 8);
		return data;
	}

	private static readonly Guid[] AllCharacteristicIds =
	[
		FtmsUuids.ControlPoint,
		FtmsUuids.MachineState,
		FtmsUuids.TrainingState,
		FtmsUuids.Feature,
		FtmsUuids.SupportedSpeedRange,
		FtmsUuids.SupportedInclinationRange,
		FtmsUuids.SupportedResistanceLevelRange,
		FtmsUuids.SupportedPowerRange,
		FtmsUuids.SupportedHeartRateRange,
	];
}
