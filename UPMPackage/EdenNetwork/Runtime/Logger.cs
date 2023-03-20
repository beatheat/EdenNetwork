using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace EdenNetwork
{
	public class Logger
	{
		private const int DEFAULT_FLUSH_INTERVAL = 3 * 60 * 1000;
		private StreamWriter _stream;
		private readonly bool _printConsole;
		private readonly int _flushInterval;
		private readonly Queue<string> _logQueue;
		private readonly string _name;

		/// <summary>
		/// Logging class
		/// </summary>
		/// <param name="path">path for log file written</param>
		/// <param name="name">name of log subject</param>
		/// <param name="printConsole">whether log is printed on console</param>
		/// <param name="flushInterval">interval of stream flush in millisecond</param>
		/// <exception cref="Exception">Cannot create log stream Exception</exception>
		public Logger(string path, string name, bool printConsole = true, int flushInterval = DEFAULT_FLUSH_INTERVAL)
		{
			this._printConsole = printConsole;
			this._flushInterval = flushInterval;
			this._name = name;
			_logQueue = new Queue<string>();
			try
			{
				if(_printConsole)
					Debug.Log($"[{name}] Opening log stream...");
				_stream = new StreamWriter(path, append: true);
				var loggerThread = new Thread(LoggerLoop); 
				loggerThread.Start();
				var flushThread = new Thread(FlushLoop);
				flushThread.Start();
				if (_printConsole)
					Debug.Log($"[{name}] Log stream opened. Log thread is running");
			}
			catch (Exception e)
			{
				throw new Exception("Logger::Load - Cannot create log stream\n" + e.Message);
			}
		}
		
		/// <summary>
		/// Close log stream
		/// </summary>
		public void Close()
		{
			_stream?.Flush();
			_stream?.Close();
			_stream = null;
		}

		/// <summary>
		/// Thread loop for Write log to file
		/// </summary>
		private void LoggerLoop()
		{
			while (_stream != null) 
			{
				while (_logQueue.Count > 0)
				{
					string log = _logQueue.Dequeue();
					_stream?.WriteLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ff") + "]" + log);
				}
				Thread.Sleep(100);
			}
		}
		
		/// <summary>
		/// Thread loop for flush log stream
		/// </summary>
		private void FlushLoop()
		{
			while (_stream != null)
			{
				Thread.Sleep(_flushInterval);
				_stream?.Flush();
			}
		}

		/// <summary>
		/// Enqueue log
		/// </summary>
		public void Log(string log)
		{
			_logQueue.Enqueue(log);
			if (_printConsole)
			    Debug.Log($"[{_name}] " + log);
		}
		
		/// <summary>
		/// Enqueue logError
		/// </summary>
		public void LogError(string log)
		{
			_logQueue.Enqueue(log);
			if (_printConsole)
				Debug.LogError($"[{_name}] " + log);
		}
	}
}