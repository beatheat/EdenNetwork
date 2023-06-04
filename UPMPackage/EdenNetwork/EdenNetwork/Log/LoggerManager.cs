using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace EdenNetwork.Log
{
	public class LoggerManager
	{
		private StreamWriter _fileWriter;

		private readonly Queue<string> _logQueue;
		private readonly string _loggerName;
		private readonly bool _printConsole;

		public LoggerManager(string loggerName, string path, bool printConsole)
		{
			_loggerName = loggerName;
			_printConsole = printConsole;
			_logQueue = new Queue<string>();
			_fileWriter = new StreamWriter(path, append: true);
			_fileWriter.AutoFlush = true;

			new Thread(LoggerThread).Start();
		}

		public Logger CreateLogger()
		{
			return new Logger(_loggerName, _logQueue, _printConsole);
		}

		public void LoggerThread()
		{
			while (_fileWriter != null)
			{
				while (_logQueue.Count > 0)
				{
					var log = _logQueue.Dequeue();
					_fileWriter?.WriteLine(log);
				}

				Thread.Sleep(100);
			}
		}

		public void Close()
		{
			_fileWriter?.Close();
			_fileWriter = null;
		}
	}
}