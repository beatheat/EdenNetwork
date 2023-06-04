using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace EdenNetwork.Log
{

	public class Logger
	{
		private readonly bool _printConsole;
		private readonly Queue<string> _logQueue;
		private readonly string _loggerName;

		public Logger(string loggerName, Queue<string> logQueue, bool printConsole = true)
		{
			_loggerName = loggerName;
			_printConsole = printConsole;
			_logQueue = logQueue;
		}


		public void Log(NetworkEventType eventType, object payload, string message)
		{
			var log = new
			{
				LogLevel = "Information",
				EventType = eventType.ToString(),
				Message = message,
				Payload = payload,
				Timestamp = DateTime.Now
			};

			var serializedLog = JsonConvert.SerializeObject(log);

			_logQueue.Enqueue(serializedLog);
			if (_printConsole)
			{
				Debug.Log($"[{_loggerName} | {DateTime.Now:yy-MM-dd hh:mm:ss}] {message}, {payload}");
			}
		}

		public void LogError(NetworkEventType eventType, object payload, string message)
		{

			var log = new
			{
				LogLevel = "Error",
				EventType = eventType.ToString(),
				Message = message,
				Payload = payload,
				Timestamp = DateTime.Now,
			};

			var serializedLog = JsonConvert.SerializeObject(log);

			_logQueue.Enqueue(serializedLog);
			if (_printConsole)
			{
				Debug.LogError($"[{_loggerName} | {DateTime.Now:yy-MM-dd hh:mm:ss}] {message}, {payload}");
			}
		}

		public void LogError(NetworkEventType eventType, object payload, string message, Exception e)
		{
			var log = new
			{
				LogLevel = "Error",
				EventType = eventType.ToString(),
				Message = message,
				Payload = payload,
				Timestamp = DateTime.Now,
				Exception = e.Message
			};

			var serializedLog = JsonConvert.SerializeObject(log);

			_logQueue.Enqueue(serializedLog);
			if (_printConsole)
			{
				Debug.LogError($"[{_loggerName} | {DateTime.Now:yy-MM-dd hh:mm:ss}] {message}, {payload}");
			}
		}

	}
}