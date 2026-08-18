namespace FTMS_Viewer.Tests;

using CommunityToolkit.Mvvm.Input;
using FTMS_Viewer.Pages;
using FTMS_Viewer.Tests.Harness;
using System.Windows.Input;

using static FTMS_Viewer.Tests.Harness.ControlPageTestHelpers;

/// <summary>
/// Tests for the Targets group of the Control page: the five (plus cadence) value-based target
/// settings encode human units at send time, clamp and snap against the advertised range with the
/// "allow out of range" toggle off (and pass through raw with it on), tag cards whose feature flag
/// is off, prefill from machine-state frames without clobbering an edited field, and keep entered
/// values across reconnects.
/// </summary>
public sealed class ControlTargetsTests : TestBase
{
	[Fact]
	public async Task TargetSpeed_EnteredInKmh_EncodedByFactorOneHundredAtSendTime()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem speed = Item(vm, "Target Speed");
		speed.Value = "12.3";

		await SendAsync(speed.Command, connection, [0x80, 0x02, 0x01]);

		AssertWritten(connection, [0x02, 0xCE, 0x04]);
		Assert.Equal("Target Speed: Success", vm.Status);
	}

	[Fact]
	public async Task TargetIncline_EnteredInPercent_EncodedByFactorTenAtSendTime()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem incline = Item(vm, "Target Incline");
		incline.Value = "-5.5";

		await SendAsync(incline.Command, connection, [0x80, 0x03, 0x01]);

		AssertWritten(connection, [0x03, 0xC9, 0xFF]);
	}

	[Fact]
	public async Task TargetResistance_EncodedByFactorOneAtSendTime()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem resistance = Item(vm, "Target Resistance Level");
		resistance.Value = "10";

		await SendAsync(resistance.Command, connection, [0x80, 0x04, 0x01]);

		AssertWritten(connection, [0x04, 0x0A]);
	}

	[Fact]
	public async Task TargetPower_EnteredInWatts_EncodedByFactorOneAtSendTime()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem power = Item(vm, "Target Power");
		power.Value = "200";

		await SendAsync(power.Command, connection, [0x80, 0x05, 0x01]);

		AssertWritten(connection, [0x05, 0xC8, 0x00]);
	}

	[Fact]
	public async Task TargetHeartRate_EnteredInBpm_EncodedByFactorOneAtSendTime()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem heartRate = Item(vm, "Target Heart Rate");
		heartRate.Value = "120";

		await SendAsync(heartRate.Command, connection, [0x80, 0x06, 0x01]);

		AssertWritten(connection, [0x06, 0x78]);
	}

	[Fact]
	public async Task TargetCadence_EnteredInRpm_EncodedByFactorTwoAtSendTime()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem cadence = Item(vm, "Target Cadence");
		cadence.Value = "90";

		await SendAsync(cadence.Command, connection, [0x80, 0x14, 0x01]);

		AssertWritten(connection, [0x14, 0xB4, 0x00]);
	}

	[Fact]
	public async Task OutOfRangeValue_ClampedToAdvertisedRangeMaximum()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem speed = Item(vm, "Target Speed");
		speed.Value = "30";

		await SendAsync(speed.Command, connection, [0x80, 0x02, 0x01]);

		AssertWritten(connection, [0x02, 0xC4, 0x09]);
	}

	[Fact]
	public async Task Value_WithToggleOff_SnappedToAdvertisedIncrement()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem speed = Item(vm, "Target Speed");
		speed.Value = "12.345";

		await SendAsync(speed.Command, connection, [0x80, 0x02, 0x01]);

		AssertWritten(connection, [0x02, 0xCE, 0x04]);
	}

	[Fact]
	public async Task Value_BelowRangeMinimum_ClampedToMinimum()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem speed = Item(vm, "Target Speed");
		speed.Value = "0.5";

		await SendAsync(speed.Command, connection, [0x80, 0x02, 0x01]);

		AssertWritten(connection, [0x02, 0x64, 0x00]);
	}

	[Fact]
	public async Task Incline_NegativeValue_ClampedToAdvertisedRange()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem incline = Item(vm, "Target Incline");
		incline.Value = "-15";

		await SendAsync(incline.Command, connection, [0x80, 0x03, 0x01]);

		AssertWritten(connection, [0x03, 0x9C, 0xFF]);
	}

	[Fact]
	public async Task AllowOutOfRange_SendsValueExactlyAsEntered_Unclamped()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		vm.IsAllowOutOfRange = true;
		TargetValueItem speed = Item(vm, "Target Speed");
		speed.Value = "30";

		await SendAsync(speed.Command, connection, [0x80, 0x02, 0x01]);

		AssertWritten(connection, [0x02, 0xB8, 0x0B]);
	}

	[Fact]
	public async Task AllowOutOfRange_SendsValueExactlyAsEntered_Unsnapped()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		vm.IsAllowOutOfRange = true;
		TargetValueItem speed = Item(vm, "Target Speed");
		speed.Value = "12.34";

		await SendAsync(speed.Command, connection, [0x80, 0x02, 0x01]);

		AssertWritten(connection, [0x02, 0xD2, 0x04]);
	}

	[Fact]
	public async Task UnsupportedTargetSetting_ShowsTag_ButStaysSendable()
	{
		var (vm, manager, connection, toasts, _) = CreateViewModel();
		connection.Feature.ReadValue =
			[0x00, 0x00, 0x00, 0x00, 0x17, 0x00, 0x01];
		manager.PushConnection(connection);

		Assert.True(Item(vm, "Target Power").IsNotSupported);
		Assert.False(Item(vm, "Target Speed").IsNotSupported);
		Assert.False(Item(vm, "Target Heart Rate").IsNotSupported);
		Assert.False(Item(vm, "Target Cadence").IsNotSupported);

		TargetValueItem power = Item(vm, "Target Power");
		power.Value = "200";
		await SendAsync(power.Command, connection, [0x80, 0x05, 0x01]);

		AssertWritten(connection, [0x05, 0xC8, 0x00]);
		Assert.Empty(toasts.Messages);
	}

	[Fact]
	public void UnsupportedTargetSetting_TagUpdatesOnReconnect()
	{
		var (vm, manager, connection, _, _) = CreateViewModel();
		connection.Feature.ReadValue =
			[0x00, 0x00, 0x00, 0x00, 0x17, 0x00, 0x01];
		manager.PushConnection(connection);
		Assert.True(Item(vm, "Target Power").IsNotSupported);

		connection.Feature.ReadValue = FakeMachineServiceConnection.DefaultFeatureData;
		manager.PushConnection(null);
		manager.PushConnection(connection);

		Assert.False(Item(vm, "Target Power").IsNotSupported);
	}

	[Fact]
	public void TargetSpeedChangedFrame_PrefillsEnteredValueInHumanUnits()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();

		connection.MachineState.Feed([0x05, 0x10, 0x27]);

		Assert.Equal("100", Item(vm, "Target Speed").Value);
	}

	[Fact]
	public void TargetInclineChangedFrame_PrefillsNegativeValue()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();

		connection.MachineState.Feed([0x06, 0x9C, 0xFF]);

		Assert.Equal("-10", Item(vm, "Target Incline").Value);
	}

	[Fact]
	public void TargetedCadenceChangedFrame_PrefillsValue()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();

		connection.MachineState.Feed([0x15, 0x64, 0x00]);

		Assert.Equal("50", Item(vm, "Target Cadence").Value);
	}

	[Fact]
	public void Prefill_DoesNotOverwriteFieldBeingEdited()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem speed = Item(vm, "Target Speed");
		connection.MachineState.Feed([0x05, 0x10, 0x27]);
		Assert.Equal("100", speed.Value);

		speed.Value = "55";
		speed.IsDirty = true;
		connection.MachineState.Feed([0x05, 0x64, 0x00]);

		Assert.Equal("55", speed.Value);
	}

	[Fact]
	public async Task SuccessfulSend_ClearsDirty_AndMachineEchoReArmsField()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem speed = Item(vm, "Target Speed");
		connection.MachineState.Feed([0x05, 0x10, 0x27]);
		speed.Value = "55";
		speed.IsDirty = true;

		await SendAsync(speed.Command, connection, [0x80, 0x02, 0x01]);

		Assert.False(speed.IsDirty);

		connection.MachineState.Feed([0x05, 0x64, 0x00]);

		Assert.Equal("1", speed.Value);
	}

	[Fact]
	public async Task RejectedTargetSetting_ShowsReasonAndToasts()
	{
		var (vm, _, connection, toasts, _) = CreateConnectedViewModel();
		TargetValueItem speed = Item(vm, "Target Speed");
		speed.Value = "12.3";

		await SendAsync(speed.Command, connection, [0x80, 0x02, 0x05]);

		Assert.Equal("Target Speed: Rejected — control not permitted", vm.Status);
		Assert.Equal(["Target Speed: Rejected — control not permitted"], toasts.Messages);
	}

	[Fact]
	public void EnteredValues_PersistAcrossDisconnectReconnect()
	{
		var (vm, manager, connection, _, _) = CreateConnectedViewModel();
		TargetValueItem speed = Item(vm, "Target Speed");
		TargetValueItem power = Item(vm, "Target Power");
		speed.Value = "15";
		power.Value = "250";

		manager.PushConnection(null);
		manager.PushConnection(connection);

		Assert.Equal("15", speed.Value);
		Assert.Equal("250", power.Value);
	}

	[Fact]
	public void TargetValueCommands_DisabledWhileDisconnected()
	{
		var (vm, _, _, _, _) = CreateViewModel();

		Assert.False(vm.IsConnected);
		Assert.False(((IAsyncRelayCommand)Item(vm, "Target Speed").Command).CanExecute(null));
	}

	private static TargetValueItem Item(ControlViewModel vm, string name)
		=> vm.ControlGroups
			.Single(g => g.Title == "Targets")
			.Items
			.OfType<TargetValueItem>()
			.Single(i => i.Name == name);
}
