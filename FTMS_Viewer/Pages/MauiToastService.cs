namespace FTMS_Viewer.Pages;

using CommunityToolkit.Maui.Alerts;

/// <summary>
/// Shows toasts through the CommunityToolkit MAUI <see cref="Toast"/> API. Windows has no
/// toast support, so it no-ops there (matching the flyout debug-log page).
/// </summary>
public sealed class MauiToastService : IToastService
{
	public Task ShowAsync(string message)
	{
#if !WINDOWS
		return Toast.Make(message).Show();
#else
		return Task.CompletedTask;
#endif
	}
}
