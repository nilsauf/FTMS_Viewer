namespace FTMS_Viewer.DebugLog;

using System;
using System.Collections.Concurrent;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using Microsoft.Extensions.Logging;

internal partial class DebugPageLoggerProvider : ILoggerProvider, IDebugPageLogProvider
{
	private readonly Subject<DebugLogItem> logItems = new();
	private readonly ConcurrentDictionary<string, ILogger> loggers = new(StringComparer.OrdinalIgnoreCase);

	public ILogger CreateLogger(string categoryName)
		=> this.loggers.GetOrAdd(categoryName, category => new DebugPageLogger(this.logItems.OnNext, category));

	public void Dispose()
	{
		this.logItems.Dispose();
		this.loggers.Clear();
	}

	public IObservable<DebugLogItem> ObserveLogItems()
		=> this.logItems.AsObservable();

	private class DebugPageLogger(Action<DebugLogItem> addLogItem, string categoryName) : ILogger
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull
			=> Disposable.Empty;

		public bool IsEnabled(LogLevel logLevel)
			=> true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			string formattedMessage = formatter(state, exception);
			DebugLogItem debugLogItem = new()
			{
				Id = eventId,
				LogLevel = logLevel,
				Category = categoryName.Split('.').Last(),
				FormattedMessage = formattedMessage,
				ExceptionName = exception?.GetType().Name,
				ExceptionMessage = exception?.Message
			};
			addLogItem(debugLogItem);
		}
	}
}
