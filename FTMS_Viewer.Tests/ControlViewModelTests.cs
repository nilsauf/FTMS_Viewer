namespace FTMS_Viewer.Tests;

using FTMS_Viewer.Pages;
using FTMS_Viewer.Tests.Harness;
using FTMS.NET;
using Microsoft.Extensions.Logging;

using static FTMS_Viewer.Tests.Harness.ControlPageTestHelpers;

/// <summary>
/// Tests for the Control page spine: control operations are sent as the correct bytes,
/// outcomes are reported on the status line, rejections and transport failures toast while
/// successes stay quiet, the permission indicator transitions correctly, and everything
/// resets on disconnect.
/// </summary>
public sealed class ControlViewModelTests : TestBase
{
	[Fact]
	public void Commands_DisabledWhileDisconnected()
	{
		var (vm, _, _, _, _) = CreateViewModel();

		Assert.False(vm.IsConnected);
		Assert.Equal("Not Connected", vm.Status);
		Assert.Equal(ControlPermission.NotRequested, vm.Permission);
		Assert.False(vm.RequestControlCommand.CanExecute(null));
		Assert.False(vm.ResetCommand.CanExecute(null));
		Assert.False(vm.StartOrResumeCommand.CanExecute(null));
		Assert.False(vm.StopCommand.CanExecute(null));
		Assert.False(vm.PauseCommand.CanExecute(null));
	}

	[Fact]
	public void Commands_EnabledWhileConnected()
	{
		var (vm, _, _, _, _) = CreateConnectedViewModel();

		Assert.True(vm.IsConnected);
		Assert.Equal(string.Empty, vm.Status);
		Assert.True(vm.RequestControlCommand.CanExecute(null));
		Assert.True(vm.ResetCommand.CanExecute(null));
		Assert.True(vm.StartOrResumeCommand.CanExecute(null));
		Assert.True(vm.StopCommand.CanExecute(null));
		Assert.True(vm.PauseCommand.CanExecute(null));
	}

	[Fact]
	public async Task RequestControl_WritesByteAndGrantsPermission()
	{
		var (vm, _, connection, toasts, _) = CreateConnectedViewModel();

		await SendAsync(vm.RequestControlCommand, connection, [0x80, 0x00, 0x01]);

		AssertWritten(connection, [0x00]);
		Assert.Equal("Request Control: Success", vm.Status);
		Assert.Equal(ControlPermission.Granted, vm.Permission);
		Assert.Empty(toasts.Messages);
	}

	[Fact]
	public async Task Reset_WritesByte()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();

		await SendAsync(vm.ResetCommand, connection, [0x80, 0x01, 0x01]);

		AssertWritten(connection, [0x01]);
		Assert.Equal("Reset: Success", vm.Status);
	}

	[Fact]
	public async Task StartOrResume_WritesByte()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();

		await SendAsync(vm.StartOrResumeCommand, connection, [0x80, 0x07, 0x01]);

		AssertWritten(connection, [0x07]);
		Assert.Equal("Start/Resume: Success", vm.Status);
	}

	[Fact]
	public async Task Stop_WritesByteWithStopCode()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();

		await SendAsync(vm.StopCommand, connection, [0x80, 0x08, 0x01]);

		AssertWritten(connection, [0x08, 0x01]);
		Assert.Equal("Stop: Success", vm.Status);
	}

	[Fact]
	public async Task Pause_WritesByteWithPauseCode()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();

		await SendAsync(vm.PauseCommand, connection, [0x80, 0x08, 0x01]);

		AssertWritten(connection, [0x08, 0x02]);
		Assert.Equal("Pause: Success", vm.Status);
	}

	[Fact]
	public async Task Rejected_ControlNotPermitted_ShowsReasonAndToasts()
	{
		var (vm, _, connection, toasts, _) = CreateConnectedViewModel();

		await SendAsync(vm.ResetCommand, connection, [0x80, 0x01, 0x05]);

		Assert.Equal("Reset: Rejected — control not permitted", vm.Status);
		Assert.Equal(["Reset: Rejected — control not permitted"], toasts.Messages);
		Assert.Equal(ControlPermission.NotRequested, vm.Permission);
	}

	[Fact]
	public async Task Rejected_OpCodeNotSupported_ShowsReasonAndToasts()
	{
		var (vm, _, connection, toasts, _) = CreateConnectedViewModel();

		await SendAsync(vm.StartOrResumeCommand, connection, [0x80, 0x07, 0x02]);

		Assert.Equal("Start/Resume: Rejected — op code not supported", vm.Status);
		Assert.Equal(["Start/Resume: Rejected — op code not supported"], toasts.Messages);
	}

	[Fact]
	public async Task Rejected_InvalidParameter_ShowsReasonAndToasts()
	{
		var (vm, _, connection, toasts, _) = CreateConnectedViewModel();

		await SendAsync(vm.StopCommand, connection, [0x80, 0x08, 0x03]);

		Assert.Equal("Stop: Rejected — invalid parameter", vm.Status);
		Assert.Equal(["Stop: Rejected — invalid parameter"], toasts.Messages);
	}

	[Fact]
	public async Task Rejected_OperationFailed_ShowsReasonAndToasts()
	{
		var (vm, _, connection, toasts, _) = CreateConnectedViewModel();

		await SendAsync(vm.PauseCommand, connection, [0x80, 0x08, 0x04]);

		Assert.Equal("Pause: Rejected — operation failed", vm.Status);
		Assert.Equal(["Pause: Rejected — operation failed"], toasts.Messages);
	}

	[Fact]
	public async Task RejectedRequestControl_DoesNotGrantPermission()
	{
		var (vm, _, connection, toasts, _) = CreateConnectedViewModel();

		await SendAsync(vm.RequestControlCommand, connection, [0x80, 0x00, 0x05]);

		Assert.Equal("Request Control: Rejected — control not permitted", vm.Status);
		Assert.Equal(["Request Control: Rejected — control not permitted"], toasts.Messages);
		Assert.Equal(ControlPermission.NotRequested, vm.Permission);
	}

	[Fact]
	public async Task TransportFailure_FallsBackToGenericErrorToast()
	{
		var (vm, _, connection, toasts, _) = CreateConnectedViewModel();
		connection.ControlPoint.WriteException = new InvalidOperationException("simulated ATT error");

		await vm.StopCommand.ExecuteAsync(null);

		Assert.Equal("Stop: Failed to send control request", vm.Status);
		Assert.Equal(["Stop: Failed to send control request"], toasts.Messages);
	}

	[Fact]
	public void ControlPermissionLostNotification_SetsPermissionToLost()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();

		connection.MachineState.Feed([0xFF]);

		Assert.Equal(ControlPermission.Lost, vm.Permission);
		Assert.Equal("Control permission lost", vm.Status);
	}

	[Fact]
	public void OtherMachineStateNotifications_DoNotAffectPermission()
	{
		var (vm, _, connection, _, _) = CreateConnectedViewModel();

		connection.MachineState.Feed([0x05, 0x10, 0x27]);

		Assert.Equal(ControlPermission.NotRequested, vm.Permission);
	}

	[Fact]
	public async Task Disconnect_ResetsPageState()
	{
		var (vm, manager, connection, toasts, _) = CreateConnectedViewModel();
		await SendAsync(vm.RequestControlCommand, connection, [0x80, 0x00, 0x01]);
		Assert.Equal(ControlPermission.Granted, vm.Permission);

		manager.PushConnection(null);

		Assert.False(vm.IsConnected);
		Assert.Equal("Not Connected", vm.Status);
		Assert.Equal(ControlPermission.NotRequested, vm.Permission);
		Assert.False(vm.RequestControlCommand.CanExecute(null));
		Assert.Empty(toasts.Messages);
	}

	[Fact]
	public async Task Reconnect_RecreatesControlAndResubscribesToMachineState()
	{
		var (vm, manager, connection, _, _) = CreateConnectedViewModel();
		await SendAsync(vm.RequestControlCommand, connection, [0x80, 0x00, 0x01]);
		Assert.Equal(ControlPermission.Granted, vm.Permission);
		Assert.Single(connection.ControlPoint.WrittenValues);

		manager.PushConnection(null);
		manager.PushConnection(connection);

		Assert.True(vm.IsConnected);
		Assert.Equal(ControlPermission.NotRequested, vm.Permission);

		await SendAsync(vm.RequestControlCommand, connection, [0x80, 0x00, 0x01]);
		Assert.Equal(ControlPermission.Granted, vm.Permission);
		Assert.Equal(2, connection.ControlPoint.WrittenValues.Count);

		connection.MachineState.Feed([0xFF]);
		Assert.Equal(ControlPermission.Lost, vm.Permission);
	}

	[Fact]
	public async Task EveryOutcome_IsLogged()
	{
		var (vm, _, connection, _, logger) = CreateConnectedViewModel();

		await SendAsync(vm.RequestControlCommand, connection, [0x80, 0x00, 0x01]);

		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("sending control request", StringComparison.OrdinalIgnoreCase));
		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("succeeded", StringComparison.OrdinalIgnoreCase));

		await SendAsync(vm.StopCommand, connection, [0x80, 0x08, 0x05]);

		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("rejected", StringComparison.OrdinalIgnoreCase));
	}
}
