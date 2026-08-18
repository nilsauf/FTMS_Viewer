namespace FTMS_Viewer.Tests.Harness;

using FTMS.NET;
using System.Reactive.Linq;

/// <summary>
/// Proves the shared machine-simulation harness works end to end: raw FTMS byte frames fed
/// through a characteristic reach a subscriber, writes to the control point are captured byte
/// for byte, and connections pushed through the fake manager reach observers.
/// </summary>
public sealed class HarnessSmokeTests : TestBase
{
	[Fact]
	public void RawFrameFedThroughControlPoint_ReachesSubscriber()
	{
		var connection = new FakeMachineServiceConnection();
		var controlPoint = connection.ControlPoint;

		byte[]? received = null;
		using var subscription = controlPoint.ObserveValue().Subscribe(frame => received = frame);

		byte[] frame = [0x00, 0x80, 0x01];
		controlPoint.Feed(frame);

		Assert.Equal(frame, received);
	}

	[Fact]
	public async Task WriteToControlPoint_RecordsExactBytes()
	{
		var connection = new FakeMachineServiceConnection();
		var controlPoint = connection.ControlPoint;

		await controlPoint.WriteValueAsync([0x00]);
		await controlPoint.WriteValueAsync([0x07]);
		await controlPoint.WriteValueAsync([0x05, 0x10, 0x27]);

		Assert.Equal(
			[new byte[] { 0x00 }, new byte[] { 0x07 }, new byte[] { 0x05, 0x10, 0x27 }],
			controlPoint.WrittenValues.ToArray());
	}

	[Fact]
	public void ConnectionPushedThroughManager_ReachesConnectionObserver()
	{
		var manager = new FakeConnectionManager();
		var connection = new FakeMachineServiceConnection();

		IFitnessMachineServiceConnection? received = null;
		using var subscription = manager
			.ObserveCurrentServiceConnection(s => s, -1)
			.Subscribe(observed => received = observed);

		manager.PushConnection(connection);

		Assert.Same(connection, received);
	}

	[Fact]
	public void LateConnectionObserver_ReplaysCurrentConnection()
	{
		var manager = new FakeConnectionManager();
		var connection = new FakeMachineServiceConnection();

		manager.PushConnection(connection);

		IFitnessMachineServiceConnection? received = null;
		using var subscription = manager
			.ObserveCurrentServiceConnection(s => s, -1)
			.Subscribe(observed => received = observed);

		Assert.Same(connection, received);
	}

	[Fact]
	public void TestBase_InstallsSynchronizationContext()
	{
		Assert.NotNull(SynchronizationContext.Current);
	}

	[Fact]
	public void ServiceData_IsReadableAsAvailableIndoorBike()
	{
		var connection = new FakeMachineServiceConnection(EFitnessMachineType.IndoorBike);

		connection.EnsureAvailability();
		Assert.Equal(EFitnessMachineType.IndoorBike, connection.ReadType());
	}
}
