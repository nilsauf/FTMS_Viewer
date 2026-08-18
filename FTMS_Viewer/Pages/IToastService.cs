namespace FTMS_Viewer.Pages;

/// <summary>
/// Injected abstraction for user-facing toast notifications, so view-model behavior is
/// testable without a MAUI runtime. The app implementation shows a native toast.
/// </summary>
public interface IToastService
{
	Task ShowAsync(string message);
}
