using EdenNetwork.Packet;

namespace EdenNetwork.Log;

public static class LoggerExtension
{
	public static void LogConnect(this Logger logger, PeerId peerId)
	{
		logger.Log(NetworkEventType.Connect, new {RemoteAddress = peerId}, $"{peerId} Connected");
	}
	
	public static void LogDisconnect(this Logger logger, PeerId peerId)
	{
		logger.Log(NetworkEventType.Disconnect, new {RemoteAddress = peerId}, $"{peerId} Disconnected");
	}
	
	public static void LogSend(this Logger logger, PeerId peerId, EdenPacket packet)
	{
		logger.Log(NetworkEventType.Send, new {RemoteAddress = peerId, Packet = packet}, $"Data Send To {peerId}");
	}
	
	public static void LogRequestTo(this Logger logger, PeerId peerId, EdenPacket packet)
	{
		logger.Log(NetworkEventType.RequestTo, new {RemoteAddress = peerId, Packet = packet}, $"Data Request To {peerId}");
	}
	
	public static void LogRequestFrom(this Logger logger, PeerId peerId, EdenPacket packet)
	{
		logger.Log(NetworkEventType.RequestFrom, new {RemoteAddress = peerId, Packet = packet}, $"Data Request From {peerId}");
	}

	public static void LogReceive(this Logger logger, PeerId peerId, EdenPacket packet)
	{
		logger.Log(NetworkEventType.Receive, new {RemoteAddress = peerId, Packet = packet}, $"Data Receive From {peerId}");
	}
	
	public static void LogResponseTo(this Logger logger, PeerId peerId, EdenPacket packet)
	{
		logger.Log(NetworkEventType.ResponseTo, new {RemoteAddress = peerId, Packet = packet}, $"Data Response To {peerId}");
	}
	
	public static void LogResponseFrom(this Logger logger, PeerId peerId, EdenPacket packet)
	{
		logger.Log(NetworkEventType.ResponseFrom, new {RemoteAddress = peerId, Packet = packet}, $"Data Response From {peerId}");
	}

	public static void LogUnformattedPacketError(this Logger logger, PeerId peerId)
	{
		logger.LogError(NetworkEventType.NotFormattedPacket, new {RemoteAddress = peerId}, $"Not Formatted Packet From {peerId}");
	}
	
	public static void LogUnformattedPacketError(this Logger logger, PeerId peerId, Exception e)
	{
		logger.LogError(NetworkEventType.NotFormattedPacket, new {RemoteAddress = peerId}, $"Not Formatted Packet From {peerId}", e);
	}
}