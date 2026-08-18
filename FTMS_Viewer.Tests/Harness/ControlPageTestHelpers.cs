namespace FTMS_Viewer.Tests.Harness;

using CommunityToolkit.Mvvm.Input;
using FTMS_Viewer.Pages;
using System.Windows.Input;

/// <summary>
/// Shared test helpers for exercising the <see cref="ControlViewModel"/> through the machine
/// simulation harness: create the view model against a fake connection, drive a send command by
/// feeding a control response, and assert the exact bytes written to the control point.
/// </summary>
public static class ControlPageTestHelpers
{
	public static (ControlViewModel vm, FakeConnectionManager manager, FakeMachineServiceConnection connection, FakeToastService toasts, FakeLogger<ControlViewModel> logger) CreateViewModel()
	{
		var manager = new FakeConnectionManager();
		var connection = new FakeMachineServiceConnection();
		var toasts = new FakeToastService();
		var logger = new FakeLogger<ControlViewModel>();
		var vm = new ControlViewModel(manager, logger, toasts);
		return (vm, manager, connection, toasts, logger);
	}

	public static (ControlViewModel vm, FakeConnectionManager manager, FakeMachineServiceConnection connection, FakeToastService toasts, FakeLogger<ControlViewModel> logger) CreateConnectedViewModel()
	{
		var (vm, manager, connection, toasts, logger) = CreateViewModel();
		manager.PushConnection(connection);
		return (vm, manager, connection, toasts, logger);
	}

	public static async Task SendAsync(ICommand command, FakeMachineServiceConnection connection, byte[] response)
	{
		Task sendTask = ((IAsyncRelayCommand)command).ExecuteAsync(null);
		connection.ControlPoint.Feed(response);
		await sendTask;
	}

	public static void AssertWritten(FakeMachineServiceConnection connection, params byte[] expected)
		=> Assert.Equal([expected], connection.ControlPoint.WrittenValues.ToArray());
}
