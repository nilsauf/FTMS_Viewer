namespace FTMS_Viewer;
using CommunityToolkit.Maui;

using FTMS_Viewer.DebugLog;
using FTMS_Viewer.Pages;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Plugin.BLE;
using Plugin.BLE.FTMS;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		builder.AddApplicationLogging();

		builder.Services.AddSingleton<Window>();
		builder.Services.AddSingleton<AppShell>();

		builder.Services.AddViewModels();
		builder.Services.AddReactiveBleServices();

		return builder.Build();
	}

	public static MauiAppBuilder AddApplicationLogging(this MauiAppBuilder builder)
	{
		var debugPageLoggerProvider = new DebugPageLoggerProvider();
		builder.Logging.AddProvider(debugPageLoggerProvider);
		builder.Services.AddSingleton<IDebugPageLogProvider>(debugPageLoggerProvider);

		return builder;
	}

	public static IServiceCollection AddViewModels(this IServiceCollection services)
	{
		return services
			.AddSingleton<FlyoutDebugLogViewModel>()
			.AddSingleton<ConnectionViewModel>()
			.AddSingleton<FeaturesViewModel>()
			.AddSingleton<DataViewModel>()
			.AddSingleton<ControlViewModel>();
	}

	public static IServiceCollection AddReactiveBleServices(this IServiceCollection services)
	{
		return services
			.AddSingleton(CrossBluetoothLE.Current)
			.AddSingleton(CrossBluetoothLE.Current.Adapter)
			.AddSingleton<IConnectionManager, ConnectionManager>();
	}
}
