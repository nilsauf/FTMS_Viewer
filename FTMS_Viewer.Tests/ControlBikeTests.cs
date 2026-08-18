namespace FTMS_Viewer.Tests;

using CommunityToolkit.Mvvm.Input;
using FTMS_Viewer.Pages;
using FTMS_Viewer.Tests.Harness;

using static FTMS_Viewer.Tests.Harness.ControlPageTestHelpers;

/// <summary>
/// Tests for the Bike group of the Control page: the indoor bike simulation parameters (wind
/// speed, grade, rolling-resistance coefficient, wind-resistance coefficient) encoded at send
/// time with the spec's factors, the wheel circumference encoded by ten and prefilled from the
/// machine with the dirty-flag rule, and the spin-down calibration whose start shows the returned
/// target speed bounds in the status line and whose ignore sends the ignore control. Cards whose
/// feature flag is off are tagged but stay sendable, and entered values persist across reconnects.
/// </summary>
public sealed class ControlBikeTests : TestBase
{
	[Fact]
	public void IndoorBikeSimulationCard_RendersFourEntryFields()
	{
		var (vm, _, _, _, _) = CreateConnectedViewModel();

		SimulationTargetItem simulation = SimulationItem(vm);

		Assert.Equal(4, simulation.Entries.Count);
		Assert.Equal(["Wind Speed", "Grade", "Rolling Resistance", "Wind Resistance"], simulation.Entries.Select(e => e.Label));
		Assert.All(simulation.Entries, e => Assert.Equal(string.Empty, e.Value));
	}

	[Fact]
	public async Task IndoorBikeSimulation_EnteredInHumanUnits_EncodedWithSpecFactorsAtSendTime()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		SimulationTargetItem simulation = SimulationItem(vm);
		simulation.Entries[0].Value = "5";
		simulation.Entries[1].Value = "2";
		simulation.Entries[2].Value = "0.005";
		simulation.Entries[3].Value = "0.4";

		await SendAsync(simulation.Command, connection, [0x80, 0x11, 0x01]);

		AssertWritten(connection, [0x11, 0x88, 0x13, 0xC8, 0x00, 0x32, 0x28]);
	}

	[Fact]
	public async Task IndoorBikeSimulation_NegativeGrade_EncodedAsSignedInt16()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		SimulationTargetItem simulation = SimulationItem(vm);
		simulation.Entries[0].Value = "5";
		simulation.Entries[1].Value = "-3";
		simulation.Entries[2].Value = "0.005";
		simulation.Entries[3].Value = "0.4";

		await SendAsync(simulation.Command, connection, [0x80, 0x11, 0x01]);

		AssertWritten(connection, [0x11, 0x88, 0x13, 0xD4, 0xFE, 0x32, 0x28]);
	}

	[Fact]
	public async Task IndoorBikeSimulation_InvalidEntry_ToastsWithEntryLabel()
	{
		var (vm, _, connection, toasts, _) = CreateConnectedViewModel();
		SimulationTargetItem simulation = SimulationItem(vm);
		simulation.Entries[0].Value = "5";
		simulation.Entries[1].Value = "not-a-number";

		await SendAsync(simulation.Command, connection, [0x80, 0x11, 0x01]);

		Assert.Equal("Indoor Bike Simulation (Grade): Invalid value", vm.Status);
		Assert.Equal(["Indoor Bike Simulation (Grade): Invalid value"], toasts.Messages);
		Assert.Empty(connection.ControlPoint.WrittenValues);
	}

	[Fact]
	public async Task WheelCircumference_EnteredInMillimeters_EncodedByFactorTenAtSendTime()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem wheel = WheelItem(vm);
		wheel.Value = "700";

		await SendAsync(wheel.Command, connection, [0x80, 0x12, 0x01]);

		AssertWritten(connection, [0x12, 0x58, 0x1B]);
	}

	[Fact]
	public void WheelCircumferenceChangedFrame_PrefillsValueInMillimeters()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();

		connection.MachineState.Feed([0x13, 0x58, 0x1B]);

		Assert.Equal("700", WheelItem(vm).Value);
	}

	[Fact]
	public void WheelCircumference_Prefill_DoesNotOverwriteDirtyField()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem wheel = WheelItem(vm);
		connection.MachineState.Feed([0x13, 0x58, 0x1B]);
		Assert.Equal("700", wheel.Value);

		wheel.Value = "710";
		wheel.IsDirty = true;
		connection.MachineState.Feed([0x13, 0x58, 0x1B]);

		Assert.Equal("710", wheel.Value);
	}

	[Fact]
	public async Task WheelCircumference_SuccessfulSend_ClearsDirty_AndEchoReArmsField()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem wheel = WheelItem(vm);
		connection.MachineState.Feed([0x13, 0x58, 0x1B]);
		wheel.Value = "710";
		wheel.IsDirty = true;

		await SendAsync(wheel.Command, connection, [0x80, 0x12, 0x01]);

		Assert.False(wheel.IsDirty);

		connection.MachineState.Feed([0x13, 0x58, 0x1B]);

		Assert.Equal("700", wheel.Value);
	}

	[Fact]
	public async Task SpinDownStart_SendsStartControl_AndShowsTargetSpeedBoundsInStatus()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		SpinDownItem spinDown = SpinDownItem(vm);

		await SendAsync(spinDown.StartCommand, connection, [0x80, 0x13, 0x01, 0xB8, 0x0B, 0x88, 0x13]);

		AssertWritten(connection, [0x13, 0x01]);
		Assert.Equal("Spin-Down Start: Success — Target speed 30-50 km/h", vm.Status);
	}

	[Fact]
	public async Task SpinDownStart_Rejection_ShowsReasonAndToasts()
	{
		var (vm, _, connection, toasts, _) = CreateConnectedViewModel();
		SpinDownItem spinDown = SpinDownItem(vm);

		await SendAsync(spinDown.StartCommand, connection, [0x80, 0x13, 0x04]);

		Assert.Equal("Spin-Down Start: Rejected — operation failed", vm.Status);
		Assert.Equal(["Spin-Down Start: Rejected — operation failed"], toasts.Messages);
	}

	[Fact]
	public async Task SpinDownIgnore_SendsIgnoreControl()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		SpinDownItem spinDown = SpinDownItem(vm);

		await SendAsync(spinDown.IgnoreCommand, connection, [0x80, 0x13, 0x01]);

		AssertWritten(connection, [0x13, 0x02]);
		Assert.Equal("Spin-Down Ignore: Success", vm.Status);
	}

	[Fact]
	public async Task UnsupportedBikeCard_ShowsTag_ButStaysSendable()
	{
		var (vm, manager, connection, toasts, _) = CreateViewModel();
		connection.Feature.ReadValue = FakeMachineServiceConnection.DefaultFeatureData;
		manager.PushConnection(connection);

		Assert.True(SimulationItem(vm).IsNotSupported);
		Assert.True(WheelItem(vm).IsNotSupported);
		Assert.True(SpinDownItem(vm).IsNotSupported);
		Assert.False(TargetsItem(vm, "Target Speed").IsNotSupported);

		TargetValueItem wheel = WheelItem(vm);
		wheel.Value = "700";
		await SendAsync(wheel.Command, connection, [0x80, 0x12, 0x01]);

		AssertWritten(connection, [0x12, 0x58, 0x1B]);
		Assert.Empty(toasts.Messages);
	}

	[Fact]
	public void BikeCard_TagClearsOnReconnectWithSupportingFeatures()
	{
		var (vm, manager, connection, _, _) = CreateViewModel();
		connection.Feature.ReadValue = FakeMachineServiceConnection.DefaultFeatureData;
		manager.PushConnection(connection);
		Assert.True(SimulationItem(vm).IsNotSupported);
		Assert.True(SpinDownItem(vm).IsNotSupported);

		connection.Feature.ReadValue = [0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x01];
		manager.PushConnection(null);
		manager.PushConnection(connection);

		Assert.False(SimulationItem(vm).IsNotSupported);
		Assert.False(WheelItem(vm).IsNotSupported);
		Assert.False(SpinDownItem(vm).IsNotSupported);
	}

	[Fact]
	public void EnteredValues_PersistAcrossDisconnectReconnect()
	{
		var (vm, manager, connection, _, _) = CreateConnectedViewModel();
		SimulationTargetItem simulation = SimulationItem(vm);
		TargetValueItem wheel = WheelItem(vm);
		simulation.Entries[0].Value = "5";
		simulation.Entries[1].Value = "2";
		simulation.Entries[2].Value = "0.005";
		simulation.Entries[3].Value = "0.4";
		wheel.Value = "700";

		manager.PushConnection(null);
		manager.PushConnection(connection);

		Assert.Equal("5", simulation.Entries[0].Value);
		Assert.Equal("2", simulation.Entries[1].Value);
		Assert.Equal("0.005", simulation.Entries[2].Value);
		Assert.Equal("0.4", simulation.Entries[3].Value);
		Assert.Equal("700", wheel.Value);
	}

	[Fact]
	public void BikeCommands_DisabledWhileDisconnected()
	{
		var (vm, _, _, _, _) = CreateViewModel();

		Assert.False(vm.IsConnected);
		Assert.False(((IAsyncRelayCommand)SimulationItem(vm).Command).CanExecute(null));
		Assert.False(((IAsyncRelayCommand)WheelItem(vm).Command).CanExecute(null));
		Assert.False(((IAsyncRelayCommand)SpinDownItem(vm).StartCommand).CanExecute(null));
		Assert.False(((IAsyncRelayCommand)SpinDownItem(vm).IgnoreCommand).CanExecute(null));
	}

	private static TargetValueItem TargetsItem(ControlViewModel vm, string name)
		=> vm.ControlGroups
			.Single(g => g.Title == "Targets")
			.Items
			.OfType<TargetValueItem>()
			.Single(i => i.Name == name);

	private static SimulationTargetItem SimulationItem(ControlViewModel vm)
		=> BikeItem<SimulationTargetItem>(vm, "Indoor Bike Simulation");

	private static TargetValueItem WheelItem(ControlViewModel vm)
		=> BikeItem<TargetValueItem>(vm, "Wheel Circumference");

	private static SpinDownItem SpinDownItem(ControlViewModel vm)
		=> BikeItem<SpinDownItem>(vm, "Spin-Down");

	private static T BikeItem<T>(ControlViewModel vm, string name)
		=> vm.ControlGroups
			.Single(g => g.Title == "Bike")
			.Items
			.OfType<T>()
			.Single(i => (i as ControlItemViewModel)!.Name == name);
}