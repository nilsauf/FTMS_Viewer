namespace FTMS_Viewer.DebugLog;

using Microsoft.Extensions.Logging;
public class DebugLogItem
{
	public EventId Id { get; init; }
	public LogLevel LogLevel { get; init; }
	public required string Category { get; init; }
	public required string FormattedMessage { get; init; }
	public string? ExceptionName { get; init; }
	public string? ExceptionMessage { get; init; }
}
