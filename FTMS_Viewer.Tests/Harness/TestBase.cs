namespace FTMS_Viewer.Tests.Harness;

using System.Threading;

/// <summary>
/// Base class for tests that exercise the view models: installs a
/// <see cref="TestSynchronizationContext"/> on the test thread so
/// <see cref="SynchronizationContext.Current"/> is never null while a view model subscribes.
/// </summary>
public abstract class TestBase
{
	protected TestBase()
	{
		SynchronizationContext.SetSynchronizationContext(new TestSynchronizationContext());
	}
}