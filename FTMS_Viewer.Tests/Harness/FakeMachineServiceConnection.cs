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

		this.Feature.ReadValue = DefaultFeatureData;
		this[FtmsUuids.SupportedSpeedRange].ReadValue = DefaultSpeedRange;
		this[FtmsUuids.SupportedInclinationRange].ReadValue = DefaultInclinationRange;
		this[FtmsUuids.SupportedResistanceLevelRange].ReadValue = DefaultResistanceLevelRange;
		this[FtmsUuids.SupportedPowerRange].ReadValue = DefaultPowerRange;
		this[FtmsUuids.SupportedHeartRateRange].ReadValue = DefaultHeartRateRange;
	}

	/// <summary>
	/// Advertises every target setting the Targets and Workout Targets groups send as supported:
	/// bits 0-12 of the target-settings field (speed through five-zone time) and bit 16 (cadence)
	/// set, the measurement-features field all zero.
	/// </summary>
	public static readonly byte[] DefaultFeatureData =
		[0x00, 0x00, 0x00, 0x00, 0xFF, 0x1F, 0x01];

	/// <summary>Raw speed range: 1.0-25.0 km/h in 0.1 km/h increments.</summary>
	public static readonly byte[] DefaultSpeedRange =
		[0x64, 0x00, 0xC4, 0x09, 0x0A, 0x00];

	/// <summary>Raw inclination range: -10.0-10.0 % in 0.5 % increments.</summary>
	public static readonly byte[] DefaultInclinationRange =
		[0x9C, 0xFF, 0x64, 0x00, 0x05, 0x00];

	/// <summary>Raw resistance-level range: 1-40 in steps of 1.</summary>
	public static readonly byte[] DefaultResistanceLevelRange =
		[0x01, 0x28, 0x01];

	/// <summary>Raw power range: 20-1000 W in 5 W increments.</summary>
	public static readonly byte[] DefaultPowerRange =
		[0x14, 0x00, 0xE8, 0x03, 0x05, 0x00];

	/// <summary>Raw heart-rate range: 60-220 bpm in 1 bpm increments.</summary>
	public static readonly byte[] DefaultHeartRateRange =
		[0x3C, 0xDC, 0x01];

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
