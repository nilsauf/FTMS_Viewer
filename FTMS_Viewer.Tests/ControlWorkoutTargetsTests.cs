namespace FTMS_Viewer.Tests;

using CommunityToolkit.Mvvm.Input;
using FTMS_Viewer.Pages;
using FTMS_Viewer.Tests.Harness;

using static FTMS_Viewer.Tests.Harness.ControlPageTestHelpers;

/// <summary>
/// Tests for the Workout Targets group of the Control page: the single-value targets (energy,
/// steps, strides, distance, training time) encode human units at send time (distance as UInt24)
/// and prefill from machine-state frames with the same dirty-flag rule as the Targets group; the
/// zone-time cards render multiple entry fields, start blank, and send all values in a single
/// control request. None of these settings advertise a range, so values are sent as entered with
/// no clamping or snapping; cards whose feature flag is off are tagged and values persist across
/// reconnects.
/// </summary>
public sealed class ControlWorkoutTargetsTests : TestBase
{
	[Fact]
	public async Task TargetedExpendedEnergy_EncodedByFactorOneAtSendTime()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem energy = ValueItem(vm, "Targeted Expended Energy");
		energy.Value = "500";

		await SendAsync(energy.Command, connection, [0x80, 0x09, 0x01]);

		AssertWritten(connection, [0x09, 0xF4, 0x01]);
	}

	[Fact]
	public async Task TargetedSteps_EncodedByFactorOneAtSendTime()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem steps = ValueItem(vm, "Targeted Steps");
		steps.Value = "1000";

		await SendAsync(steps.Command, connection, [0x80, 0x0A, 0x01]);

		AssertWritten(connection, [0x0A, 0xE8, 0x03]);
	}

	[Fact]
	public async Task TargetedStrides_EncodedByFactorOneAtSendTime()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem strides = ValueItem(vm, "Targeted Strides");
		strides.Value = "500";

		await SendAsync(strides.Command, connection, [0x80, 0x0B, 0x01]);

		AssertWritten(connection, [0x0B, 0xF4, 0x01]);
	}

	[Fact]
	public async Task TargetedDistance_EnteredInMeters_EncodedAsUInt24()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem distance = ValueItem(vm, "Targeted Distance");
		distance.Value = "5000";

		await SendAsync(distance.Command, connection, [0x80, 0x0C, 0x01]);

		AssertWritten(connection, [0x0C, 0x88, 0x13, 0x00]);
	}

	[Fact]
	public async Task TargetedTrainingTime_EncodedByFactorOneAtSendTime()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem time = ValueItem(vm, "Targeted Training Time");
		time.Value = "3600";

		await SendAsync(time.Command, connection, [0x80, 0x0D, 0x01]);

		AssertWritten(connection, [0x0D, 0x10, 0x0E]);
	}

	[Fact]
	public void ZoneTimeCards_RenderTheRightNumberOfEntries_StartingBlank()
	{
		var (vm, _, _, _, _) = CreateConnectedViewModel();

		MultiValueTargetItem two = MultiItem(vm, "Time in 2 HR Zones");
		MultiValueTargetItem three = MultiItem(vm, "Time in 3 HR Zones");
		MultiValueTargetItem five = MultiItem(vm, "Time in 5 HR Zones");

		Assert.Equal(2, two.Entries.Count);
		Assert.Equal(3, three.Entries.Count);
		Assert.Equal(5, five.Entries.Count);
		Assert.All(two.Entries, e => Assert.Equal(string.Empty, e.Value));
		Assert.All(three.Entries, e => Assert.Equal(string.Empty, e.Value));
		Assert.All(five.Entries, e => Assert.Equal(string.Empty, e.Value));
	}

	[Fact]
	public async Task TwoZoneTime_SendsAllValuesInASingleRequest()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		MultiValueTargetItem zones = MultiItem(vm, "Time in 2 HR Zones");
		zones.Entries[0].Value = "600";
		zones.Entries[1].Value = "1800";

		await SendAsync(zones.Command, connection, [0x80, 0x0E, 0x01]);

		AssertWritten(connection, [0x0E, 0x58, 0x02, 0x08, 0x07]);
	}

	[Fact]
	public async Task ThreeZoneTime_SendsAllValuesInASingleRequest()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		MultiValueTargetItem zones = MultiItem(vm, "Time in 3 HR Zones");
		zones.Entries[0].Value = "300";
		zones.Entries[1].Value = "600";
		zones.Entries[2].Value = "900";

		await SendAsync(zones.Command, connection, [0x80, 0x0F, 0x01]);

		AssertWritten(connection, [0x0F, 0x2C, 0x01, 0x58, 0x02, 0x84, 0x03]);
	}

	[Fact]
	public async Task FiveZoneTime_SendsAllValuesInASingleRequest()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		MultiValueTargetItem zones = MultiItem(vm, "Time in 5 HR Zones");
		zones.Entries[0].Value = "60";
		zones.Entries[1].Value = "120";
		zones.Entries[2].Value = "180";
		zones.Entries[3].Value = "240";
		zones.Entries[4].Value = "300";

		await SendAsync(zones.Command, connection, [0x80, 0x10, 0x01]);

		AssertWritten(connection, [0x10, 0x3C, 0x00, 0x78, 0x00, 0xB4, 0x00, 0xF0, 0x00, 0x2C, 0x01]);
	}

	[Fact]
	public async Task WorkoutTarget_NoAdvertisedRange_SentAsEntered_WithToggleOff()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem time = ValueItem(vm, "Targeted Training Time");
		time.Value = "10000";

		await SendAsync(time.Command, connection, [0x80, 0x0D, 0x01]);

		AssertWritten(connection, [0x0D, 0x10, 0x27]);
	}

	[Fact]
	public async Task TargetedDistance_NoAdvertisedRange_SentAsEntered_WithToggleOff()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem distance = ValueItem(vm, "Targeted Distance");
		distance.Value = "100000";

		await SendAsync(distance.Command, connection, [0x80, 0x0C, 0x01]);

		AssertWritten(connection, [0x0C, 0xA0, 0x86, 0x01]);
	}

	[Fact]
	public async Task WorkoutTarget_AllowOutOfRangeToggle_HasNoEffect()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		vm.IsAllowOutOfRange = true;
		TargetValueItem time = ValueItem(vm, "Targeted Training Time");
		time.Value = "10000";

		await SendAsync(time.Command, connection, [0x80, 0x0D, 0x01]);

		AssertWritten(connection, [0x0D, 0x10, 0x27]);
	}

	[Fact]
	public async Task ZoneTime_AllowOutOfRangeToggle_HasNoEffect()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		vm.IsAllowOutOfRange = true;
		MultiValueTargetItem zones = MultiItem(vm, "Time in 3 HR Zones");
		zones.Entries[0].Value = "300";
		zones.Entries[1].Value = "600";
		zones.Entries[2].Value = "900";

		await SendAsync(zones.Command, connection, [0x80, 0x0F, 0x01]);

		AssertWritten(connection, [0x0F, 0x2C, 0x01, 0x58, 0x02, 0x84, 0x03]);
	}

	[Fact]
	public void TargetedExpendedEnergyChangedFrame_PrefillsValueInKcal()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();

		connection.MachineState.Feed([0x0A, 0xF4, 0x01]);

		Assert.Equal("500", ValueItem(vm, "Targeted Expended Energy").Value);
	}

	[Fact]
	public void TargetedStepsChangedFrame_PrefillsValue()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();

		connection.MachineState.Feed([0x0B, 0xE8, 0x03]);

		Assert.Equal("1000", ValueItem(vm, "Targeted Steps").Value);
	}

	[Fact]
	public void TargetedStridesChangedFrame_PrefillsValue()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();

		connection.MachineState.Feed([0x0C, 0xF4, 0x01]);

		Assert.Equal("500", ValueItem(vm, "Targeted Strides").Value);
	}

	[Fact]
	public void TargetedDistanceChangedFrame_PrefillsValueInMeters()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();

		connection.MachineState.Feed([0x0D, 0x88, 0x13, 0x00]);

		Assert.Equal("5000", ValueItem(vm, "Targeted Distance").Value);
	}

	[Fact]
	public void TargetedTrainingTimeChangedFrame_PrefillsValueInSeconds()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();

		connection.MachineState.Feed([0x0E, 0x10, 0x0E]);

		Assert.Equal("3600", ValueItem(vm, "Targeted Training Time").Value);
	}

[Fact]
	public async Task SuccessfulSend_ClearsDirty_AndMachineEchoReArmsField()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem energy = ValueItem(vm, "Targeted Expended Energy");
		connection.MachineState.Feed([0x0A, 0xF4, 0x01]);
		Assert.Equal("500", energy.Value);
		energy.Value = "700";
		energy.IsDirty = true;

		connection.MachineState.Feed([0x0A, 0xF4, 0x01]);

		Assert.Equal("700", energy.Value);

		await SendAsync(energy.Command, connection, [0x80, 0x09, 0x01]);

		Assert.False(energy.IsDirty);

		connection.MachineState.Feed([0x0A, 0xF4, 0x01]);

		Assert.Equal("500", energy.Value);
	}

	[Fact]
	public async Task UnsupportedWorkoutTarget_ShowsTag_ButStaysSendable()
	{
		var (vm, manager, connection, toasts, _) = CreateViewModel();
		connection.Feature.ReadValue = [0x00, 0x00, 0x00, 0x00, 0x1F, 0x00, 0x01];
		manager.PushConnection(connection);

		Assert.True(ValueItem(vm, "Targeted Expended Energy").IsNotSupported);
		Assert.True(ValueItem(vm, "Targeted Distance").IsNotSupported);
		Assert.True(MultiItem(vm, "Time in 5 HR Zones").IsNotSupported);
		Assert.False(TargetsItem(vm, "Target Speed").IsNotSupported);

		TargetValueItem energy = ValueItem(vm, "Targeted Expended Energy");
		energy.Value = "500";
		await SendAsync(energy.Command, connection, [0x80, 0x09, 0x01]);

		AssertWritten(connection, [0x09, 0xF4, 0x01]);
		Assert.Empty(toasts.Messages);
	}

	[Fact]
	public void WorkoutTarget_TagClearsOnReconnectWithSupportingFeatures()
	{
		var (vm, manager, connection, _, _) = CreateViewModel();
		connection.Feature.ReadValue = [0x00, 0x00, 0x00, 0x00, 0x1F, 0x00, 0x01];
		manager.PushConnection(connection);
		Assert.True(ValueItem(vm, "Targeted Expended Energy").IsNotSupported);

		connection.Feature.ReadValue = FakeMachineServiceConnection.DefaultFeatureData;
		manager.PushConnection(null);
		manager.PushConnection(connection);

		Assert.False(ValueItem(vm, "Targeted Expended Energy").IsNotSupported);
	}

	[Fact]
	public async Task InvalidZoneTimeValue_ToastsWithEntryLabel()
	{
		var (vm, _, connection, toasts, _) = CreateConnectedViewModel();
		MultiValueTargetItem zones = MultiItem(vm, "Time in 2 HR Zones");
		zones.Entries[0].Value = "600";
		zones.Entries[1].Value = "not-a-number";

		await SendAsync(zones.Command, connection, [0x80, 0x0E, 0x01]);

		Assert.Equal("Time in 2 HR Zones (Fitness): Invalid value", vm.Status);
		Assert.Equal(["Time in 2 HR Zones (Fitness): Invalid value"], toasts.Messages);
		Assert.Empty(connection.ControlPoint.WrittenValues);
	}

	[Fact]
	public void EnteredValues_PersistAcrossDisconnectReconnect()
	{
		var (vm, manager, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem energy = ValueItem(vm, "Targeted Expended Energy");
		MultiValueTargetItem zones = MultiItem(vm, "Time in 2 HR Zones");
		energy.Value = "700";
		zones.Entries[0].Value = "600";
		zones.Entries[1].Value = "1800";

		manager.PushConnection(null);
		manager.PushConnection(connection);

		Assert.Equal("700", energy.Value);
		Assert.Equal("600", zones.Entries[0].Value);
		Assert.Equal("1800", zones.Entries[1].Value);
	}

	[Fact]
	public void WorkoutTargetCommands_DisabledWhileDisconnected()
	{
		var (vm, _, _, _, _) = CreateViewModel();

		Assert.False(vm.IsConnected);
		Assert.False(((IAsyncRelayCommand)ValueItem(vm, "Targeted Expended Energy").Command).CanExecute(null));
		Assert.False(((IAsyncRelayCommand)MultiItem(vm, "Time in 5 HR Zones").Command).CanExecute(null));
	}

	private static TargetValueItem TargetsItem(ControlViewModel vm, string name)
		=> vm.ControlGroups
			.Single(g => g.Title == "Targets")
			.Items
			.OfType<TargetValueItem>()
			.Single(i => i.Name == name);

	private static TargetValueItem ValueItem(ControlViewModel vm, string name)
		=> WorkoutItem<TargetValueItem>(vm, name);

	private static MultiValueTargetItem MultiItem(ControlViewModel vm, string name)
		=> WorkoutItem<MultiValueTargetItem>(vm, name);

	private static T WorkoutItem<T>(ControlViewModel vm, string name)
		=> vm.ControlGroups
			.Single(g => g.Title == "Workout Targets")
			.Items
			.OfType<T>()
			.Single(i => (i as ControlItemViewModel)!.Name == name);
}