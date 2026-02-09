namespace FTMS_Viewer.Pages;

using System.Reactive.Disposables;
using System.Reactive.Linq;

using DynamicData;
using DynamicData.Binding;

using FTMS.NET;
using FTMS.NET.Data;

using Microsoft.Extensions.Logging;

using Plugin.BLE.FTMS;

public sealed partial class DataViewModel : IDisposable
{
	private readonly ObservableCollectionExtended<IFitnessMachineValue> values = [];
	private readonly IDisposable cleanUp;

	public IObservableCollection<IFitnessMachineValue> Values
		=> this.values;

	public DataViewModel(IConnectionManager connectionManager, ILogger<DataViewModel> logger)
	{
		var dataObservable = connectionManager
			.ObserveCurrentServiceConnection(
				s => s.SelectMany(async c => c is not null ? await c.CreateFitnessMachineDataAsync() : null),
				10)
			.Replay(1)
			.AutoConnect();

		this.cleanUp = ObservableChangeSet
			.Create<IFitnessMachineValue, Guid>(cache => new CompositeDisposable
				{
					dataObservable.Where(data => data is null)
						.Subscribe(_ => cache.Clear()),
					dataObservable.Select(data => data?.Connect() ?? Observable.Never<IChangeSet<IFitnessMachineValue, Guid>>())
						.Switch()
						.PopulateInto(cache)
				},
				v => v.Uuid)
			.IgnoreUpdateWhen((current, previous) => current.Value == previous.Value)
			.ObserveOn(SynchronizationContext.Current!)
			.Bind(this.values)
			.Subscribe(_ => { },
				ex => logger.LogError(ex, "Error during observing current service data! Stopping..."));
	}

	public void Dispose()
		=> this.cleanUp.Dispose();

	public class ValueViewModel(string name, double value)
	{
		public string Name { get; } = name.AddSpacesBetweenWords();
		public double Value { get; } = value;
	}
}
