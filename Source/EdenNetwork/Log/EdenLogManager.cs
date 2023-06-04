using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace EdenNetwork.Log;


public static class EdenLogManager
{
	private static ILoggerFactory? s_loggerFactory = null;

	public static ILogger<T>? GetLogger<T>() where T : class
	{
		return s_loggerFactory?.CreateLogger<T>();
	}

	public static void SettingLogger(string logDirectory, bool printConsole = false)
	{
		s_loggerFactory = LoggerFactory.Create(builder =>
		{
			builder.ClearProviders();
		
			var exists = Directory.Exists(logDirectory);

			if (!exists)
			{
				Directory.CreateDirectory(logDirectory);
			}
		
			builder.AddZLoggerRollingFile(
				fileNameSelector: (dt, x) => $"{logDirectory}{dt.ToLocalTime():yyyy-MM-dd}_{x:000}.log",
				timestampPattern: x => x.ToLocalTime().Date, 
				rollSizeKB: 1024,
				options =>
				{
					options.EnableStructuredLogging = true;
					var time = JsonEncodedText.Encode("Timestamp");
					//DateTime.Now는 UTC+0 이고 한국은 UTC+9이므로 9시간을 더한 값을 출력한다.
					var timeValue = JsonEncodedText.Encode(DateTime.Now.AddHours(9).ToString("yyyy/MM/dd HH:mm:ss"));

					options.StructuredLoggingFormatter = (writer, info) =>
					{
						writer.WriteString(time, timeValue);
						info.WriteToJsonWriter(writer);
					};
				}); 

			if (printConsole)
			{
				builder.AddZLoggerConsole(options =>
				{
					options.EnableStructuredLogging = true;
					var time = JsonEncodedText.Encode("EventTime");
					var timeValue = JsonEncodedText.Encode(DateTime.Now.AddHours(9).ToString("yyyy/MM/dd HH:mm:ss"));

					options.StructuredLoggingFormatter = (writer, info) =>
					{
						writer.WriteString(time, timeValue);
						info.WriteToJsonWriter(writer);
					};
				});
			}
			
		});
		
	}
}