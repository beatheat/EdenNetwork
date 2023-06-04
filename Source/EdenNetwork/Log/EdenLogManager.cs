using System.Text.Json;

namespace EdenNetwork.Log;


public static class EdenLogManager
{
	private static LoggerManager? _loggerManager = null;
	
	public static Logger? GetLogger()
	{
		return _loggerManager?.CreateLogger();
	}

	public static void SettingLogger(string loggerName, string logDirectory, bool printConsole = false)
	{
		_loggerManager = new LoggerManager(loggerName, logDirectory, printConsole);
	}

	public static void Close()
	{
		_loggerManager?.Close();
	}
}