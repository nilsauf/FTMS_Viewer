namespace FTMS_Viewer.Tests.Harness;

using Microsoft.Extensions.Logging;

/// <summary>
/// A fake <see cref="ILogger{T}"/> that records the formatted log entries, so tests can assert
/// that attempts and outcomes are actually logged.
/// </summary>
public sealed class FakeLogger<T> : ILogger<T>
{
	public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

	public IDisposable? BeginScope<TState>(TState state)
		where TState : notnull
		=> null;

	public bool IsEnabled(LogLevel logLevel) => true;

	public void Log<TState>(
		LogLevel logLevel,
		EventId eventId,
		TState state,
		Exception? exception,
		Func<TState, Exception?, string> formatter)
		=> this.Entries.Add((logLevel, formatter(state, exception), exception));
}
