namespace FTMS_Viewer.Tests.Harness;

using FTMS_Viewer.Pages;

/// <summary>
/// A fake <see cref="IToastService"/> that records every toast message for assertion.
/// </summary>
public sealed class FakeToastService : IToastService
{
	public List<string> Messages { get; } = [];

	public Task ShowAsync(string message)
	{
		this.Messages.Add(message);
		return Task.CompletedTask;
	}
}
