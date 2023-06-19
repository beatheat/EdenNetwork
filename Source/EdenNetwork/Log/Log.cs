namespace EdenNetwork.Log;

#pragma warning disable CS8618

public enum LogLevel
{
	Information, Error
}

public class Log
{
	public LogLevel LogLevel { get; set; }
	public NetworkEventType EventType { get; set; }
	public string Message { get; set; }
	public object? Payload { get; set; }
	public DateTime Timestamp { get; set; }
	public string? Exception { get; set; } = null;
}