using FTMS.NET.Features;

using SourceGeneration.Reflection;

[assembly: SourceReflectionType<IFitnessMachineFeatures>]

namespace FTMS_Viewer.Pages;

using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;

using CommunityToolkit.Mvvm.ComponentModel;

using DynamicData;

using FTMS.NET;
using FTMS.NET.Features;

using Microsoft.Extensions.Logging;

using Plugin.BLE.FTMS;

public sealed partial class FeaturesViewModel : ObservableObject, IDisposable
{
	private const string NotConnected = "Not Connected";
	private const string Reading = "Reading Features...";

	private readonly IConnectionManager connectionManager;
	private readonly ILogger<FeaturesViewModel> logger;
	private readonly IDisposable cleanUp;
	private readonly List<SourcePropertyInfo> featureBoolProperties = SourceReflector
		.GetType<IFitnessMachineFeatures>()?
		.GetProperties()
		.Where(p => p.PropertyType == typeof(bool))
		.ToList() ?? [];
	private readonly List<SourcePropertyInfo> featureRangeProperties = SourceReflector
		.GetType<IFitnessMachineFeatures>()?
		.GetProperties()
		.Where(p => p.PropertyType == typeof(ISupportedRange))
		.ToList() ?? [];

	public FeaturesViewModel(IConnectionManager connectionManager, ILogger<FeaturesViewModel> logger)
	{
		this.connectionManager = connectionManager;
		this.logger = logger;
		this.cleanUp = connectionManager.ObserveCurrentServiceConnection()
			.Do(connection => this.Type = connection is null ? NotConnected : Reading)
			.SelectMany(this.ReadFeaturesAsync)
			.RetryAndDisconnect(connectionManager)
			.ObserveOn(SynchronizationContext.Current!)
			.Subscribe(
				this.SetData,
				ex => this.logger.LogError(ex, "Error during observing current service features! Stopping..."));
	}

	[ObservableProperty]
	public partial string Type { get; private set; } = NotConnected;
	public ObservableCollection<NamedFlagValue> Flags { get; set; } = [];
	public ObservableCollection<RangeValue> Ranges { get; set; } = [];

	private async Task<(EFitnessMachineType type, IFitnessMachineFeatures features)?>
		ReadFeaturesAsync(IFitnessMachineServiceConnection? connection)
	{
		if (connection is null)
			return null;

		this.logger.LogDebug("Reading features from connection");
		var type = connection.ReadType();
		var features = await connection.ReadFitnessMachineFeaturesAsync();
		return (type, features);
	}

	private void SetData((EFitnessMachineType type, IFitnessMachineFeatures features)? data)
	{
		if (data is not null)
		{
			this.logger.LogDebug("Setting data from features");
			this.Type = $"{this.connectionManager.ConnectedDevice!.Name} is a {data.Value.type}";

			var features = data.Value.features;

			this.Flags.Clear();
			this.Flags.AddRange(this.featureBoolProperties
				.Select(p => new NamedFlagValue(p.Name, (bool)p.GetValue(features)!)));

			this.Ranges.Clear();
			this.Ranges.AddRange(this.featureRangeProperties
				.Select(p => new RangeValue(p.Name, (ISupportedRange)p.GetValue(features)!)));
		}
		else
		{
			this.Type = NotConnected;
			this.Flags.Clear();
			this.Ranges.Clear();
		}
	}

	public void Dispose()
	{
		this.cleanUp.Dispose();
	}
}

public sealed class NamedFlagValue(string name, bool value)
{
	public string Name { get; } = name.AddSpacesBetweenWords();
	public Color ValueColor { get; } = value ? Colors.Green : Colors.Red;
	public string ValueString { get; } = value ? "YES" : "NO";
}

public sealed class RangeValue(string name, ISupportedRange? range)
{
	public string Name { get; } = name.AddSpacesBetweenWords();
	public ISupportedRange? Range { get; } = range;
	public bool Supported { get; } = range is not null;
	public bool NotSupported { get; } = range is null;
}
