using System.Text.Json;

namespace EdenNetwork.Log;

public class Logger
{
	private readonly Queue<Log> _logQueue;
	
	public Logger(Queue<Log> logQueue)
	{
		_logQueue = logQueue;
	}


	public void Log(NetworkEventType eventType, object payload, string message)
	{
		var log = new Log
		{
			LogLevel = LogLevel.Information,
			EventType = eventType,
			Message = message,
			Payload = payload,
			Timestamp = DateTime.Now
		};

		_logQueue.Enqueue(log);

	}

	public void LogError(NetworkEventType eventType, object payload, string message)
	{

		var log = new Log
		{
			LogLevel = LogLevel.Error,
			EventType = eventType,
			Message = message,
			Payload = payload,
			Timestamp = DateTime.Now,
		};
		
		_logQueue.Enqueue(log);
	}
	
	public void LogError(NetworkEventType eventType, object payload, string message, Exception e)
	{
		var log = new Log
		{
			LogLevel = LogLevel.Error,
			EventType = eventType,
			Message = message,
			Payload = payload,
			Timestamp = DateTime.Now,
			Exception = e.Message
		};
		
		_logQueue.Enqueue(log);
	}

}