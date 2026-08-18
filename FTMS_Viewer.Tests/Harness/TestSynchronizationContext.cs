namespace FTMS_Viewer.Tests.Harness;

using System.Threading;

/// <summary>
/// A <see cref="SynchronizationContext"/> that dispatches posted work inline and keeps itself
/// current while the callback runs. The view models call
/// <c>ObserveOn(SynchronizationContext.Current!)</c> during construction, so tests must install
/// a context before a view model subscribes; executing callbacks inline (while setting
/// <see cref="SynchronizationContext.Current"/>) makes nested context reads non-null and keeps
/// the whole subscription pipeline running synchronously on the test thread.
/// </summary>
public sealed class TestSynchronizationContext : SynchronizationContext
{
	public override void Post(SendOrPostCallback d, object? state)
		=> this.RunInline(d, state);

	public override void Send(SendOrPostCallback d, object? state)
		=> this.RunInline(d, state);

	private void RunInline(SendOrPostCallback d, object? state)
	{
		var previous = SynchronizationContext.Current;
		SynchronizationContext.SetSynchronizationContext(this);
		try
		{
			d(state);
		}
		finally
		{
			SynchronizationContext.SetSynchronizationContext(previous);
		}
	}
}
