using System.Text;
using System.Text.Json;

namespace EdenNetwork.Log;

public class LoggerManager
{
	private const int FILE_ROLLING_SIZE = 1024 * 1024; // 1MB
	
	private StreamWriter? _fileWriter;

	private readonly Queue<Log> _logQueue;
	private readonly string _loggerName;
	private readonly bool _printConsole;
	private readonly string _logPath;
	
	private int _fileSize;
	private int _fileNo;
	private DateTime _today;
	
	public LoggerManager(string loggerName, string path, bool printConsole)
	{
		_loggerName = loggerName;
		_printConsole = printConsole;
		_logQueue = new Queue<Log>();

		_logPath = path;
		_fileSize = 0;
		_fileNo = 0;
		_today = DateTime.Today;

		if (Directory.Exists(path) == false)
		{
			Directory.CreateDirectory(path);
		}
		
		//select last log file, if it is today`s log
		var logPathFiles = Directory.GetFiles(_logPath);
		if (logPathFiles.Length > 0)
		{
			var fileName = logPathFiles.Last();

			var splitFileName = fileName.Split("_");
			var dateString = splitFileName[0];
			var numberString = splitFileName[1];

			if (DateTime.TryParse(dateString, out var date) && int.TryParse(numberString, out var number))
			{
				if (date == DateTime.Today)
				{
					_fileNo = number;
				}
			}
		}

		CreateRollingFile();

		new Thread(LoggerThread).Start();
	}

	public Logger CreateLogger()
	{
		return new Logger(_logQueue);
	}
	
	public void Close()
	{
		_fileWriter?.Close();
		_fileWriter = null;
	}

	private void CreateRollingFile()
	{
		string rollingFileName = $"{_logPath}/{_today:yyyy-MM-dd}_{_fileNo:000}.log";
		_fileWriter = new StreamWriter(rollingFileName, append: true);
		_fileWriter.AutoFlush = true;
		_fileSize = (int) new FileInfo(rollingFileName).Length;
	}
	
	private void LoggerThread()
	{
		while (_fileWriter != null)
		{
			while (_logQueue.Count > 0)
			{
				var log = _logQueue.Dequeue();
				
				var jsonLog = JsonSerializer.Serialize(log);
				_fileWriter?.WriteLine(jsonLog);

				if (_printConsole)
				{
					if (log.LogLevel == LogLevel.Information)
						Console.ForegroundColor = ConsoleColor.White;
					else
						Console.ForegroundColor = ConsoleColor.Red;
					
					var consoleLogString = $"[{_loggerName}|{log.Timestamp:yy-MM-dd hh:mm:ss.ffff}] {log.Message}";

					if (log.Payload != null) consoleLogString += $", {JsonSerializer.Serialize(log.Payload)}";
					if (log.Exception != null) consoleLogString += $", {log.Exception}";

					Console.WriteLine(consoleLogString);
				}

				// size
				_fileSize += Encoding.Default.GetByteCount(jsonLog);
				if (_fileSize > FILE_ROLLING_SIZE)
				{
					_fileNo++;
					CreateRollingFile();
				}
			}
			// date
			if (DateTime.Today - _today >= TimeSpan.FromDays(1))
			{
				_fileNo = 0;
				_today = DateTime.Today;
				CreateRollingFile();
			}
			
			Thread.Sleep(100);
		}
	}

}