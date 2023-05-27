using EdenNetwork.Packet;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace EdenNetwork.Log;

public static class LoggerExtension
{

	public static void LogConnect(this ILogger logger, PeerId peerId)
	{
		logger.ZLogInformationWithPayload(new EventId((int)LogEvent.Connect, "Connect"),
			new {RemoteAddress = peerId}, $"{peerId} Connected");
	}
	
	public static void LogDisconnect(this ILogger logger, PeerId peerId)
	{
		logger.ZLogInformationWithPayload(new EventId((int)LogEvent.Disconnect, "Disconnect"),
			new {RemoteAddress = peerId}, $"{peerId} Disconnected");
	}
	
	public static void LogSend(this ILogger logger, PeerId peerId, EdenPacket packet)
	{
		logger.ZLogInformationWithPayload(new EventId((int)LogEvent.Send, "Send"),
			new {RemoteAddress = peerId, Packet = packet}, $"Data Send To {peerId}");
	}
	
	public static void LogRequestTo(this ILogger logger, PeerId peerId, EdenPacket packet)
	{
		logger.ZLogInformationWithPayload(new EventId((int)LogEvent.RequestTo, "RequestTo"),
			new {RemoteAddress = peerId, Packet = packet}, $"Data Request To {peerId}");
	}
	
	public static void LogRequestFrom(this ILogger logger, PeerId peerId, EdenPacket packet)
	{
		logger.ZLogInformationWithPayload(new EventId((int)LogEvent.RequestFrom, "Request From"),
			new {RemoteAddress = peerId, Packet = packet}, $"Data Request From {peerId}");
	}

	public static void LogReceive(this ILogger logger, PeerId peerId, EdenPacket packet)
	{
		logger.ZLogInformationWithPayload(new EventId((int)LogEvent.Receive, "Connection"),
			new {RemoteAddress = peerId, Packet = packet}, $"Data Receive From {peerId}");
	}
	
	public static void LogResponseTo(this ILogger logger, PeerId peerId, EdenPacket packet)
	{
		logger.ZLogInformationWithPayload(new EventId((int)LogEvent.ResponseTo, "ResponseTo"),
			new {RemoteAddress = peerId, Packet = packet}, $"Data Response To {peerId}");
	}
	
	public static void LogResponseFrom(this ILogger logger, PeerId peerId, EdenPacket packet)
	{
		logger.ZLogInformationWithPayload(new EventId((int)LogEvent.ResponseFrom, "ResponseFrom"),
			new {RemoteAddress = peerId, Packet = packet}, $"Data Response From {peerId}");
	}

	public static void LogUnformattedPacketError(this ILogger logger, PeerId peerId)
	{
		logger.ZLogErrorWithPayload(new EventId((int)LogEvent.NotFormattedPacket, "NotFormattedPacket"),
			new {RemoteAddress = peerId}, $"Not Formatted Packet From {peerId}");
	}
	
	public static void LogUnformattedPacketError(this ILogger logger, PeerId peerId, Exception e)
	{
		logger.ZLogErrorWithPayload(new EventId((int) LogEvent.NotFormattedPacket), e, 
			new {RemoteAddress = peerId}, $"Not Formatted Packet From {peerId}");
	}
}