using System.Net.Sockets;
using EdenNetwork.Dispatcher;
using EdenNetwork.EdenException;
using EdenNetwork.Log;
using EdenNetwork.Packet;

namespace EdenNetwork;

internal class TcpPeer
{
	private readonly TcpClient _tcpClient;
	private readonly NetworkStream _stream;
	private readonly EdenServerDispatcher _dispatcher;
	private readonly EdenPacketSerializer _serializer;
	private readonly EdenTcpServer _parent;
	private readonly PeerId _serverId;

	private readonly Logger? _logger;

	public PeerId ServerId => _serverId;
	
	public TcpPeer(TcpClient tcpClient, PeerId serverId, EdenPacketSerializer serializer, EdenServerDispatcher dispatcher, EdenTcpServer parent, Logger? logger)
	{
		_tcpClient = tcpClient;
		_stream = tcpClient.GetStream();
		_serverId = serverId;
		_dispatcher = dispatcher;
		_serializer = serializer;
		_parent = parent;
		_logger = logger;
	}
	
	public void BeginReceive()
	{
		Task.Run(() =>
		{
			while (_tcpClient.Connected && _stream.CanRead)
			{
				var packetLengthByte = new byte[2];
				if (!(_tcpClient.Connected && _stream.CanRead))
					break;
				_stream.Read(packetLengthByte);
				var packetLength = _serializer.GetPacketLength(packetLengthByte);
				//Ignore Not Formatted Packet
				if (packetLength < 0)
				{
					_logger?.LogUnformattedPacketError(_serverId);
					continue;
				}
				var packetBytes = new byte[packetLength];
				if (!(_tcpClient.Connected && _stream.CanRead))
					break;
				_stream.Read(packetBytes);
				Task.Run(() => NetworkReceive(packetBytes, packetLength));
			}
			_parent.DisconnectClient(_serverId);
		});

		void NetworkReceive(byte[] serializedPacket, int packetLength)
		{
			EdenPacket packet;
			try
			{
				packet = _serializer.Deserialize(serializedPacket, packetLength);
			}
			catch (Exception e)
			{
				_logger?.LogUnformattedPacketError(_serverId,e);
				return;
			}
			
			try
			{
				if (packet.Type == EdenPacketType.Request)
				{
					var responseData = _dispatcher.DispatchRequestPacket(_serverId, packet);
					_logger?.LogRequestFrom(_serverId, packet);
					Response(packet.Tag, responseData);
				}
				else if(packet.Type == EdenPacketType.Send)
				{
					_dispatcher.DispatchSendPacket(_serverId, packet);
					_logger?.LogRequestFrom(_serverId, packet);
				}
				else
				{
					//Ignore Not Formatted Packet
					_logger?.LogUnformattedPacketError(_serverId);
					return;
				}
			}
			catch (Exception e)
			{
				_logger?.LogUnformattedPacketError(_serverId, e);
				return;
			}
			
		
		}
	}

	public void Send(string tag, object? data)
	{
		if (!(_tcpClient.Connected && _stream.CanWrite))
			throw new EdenNetworkException("Peer is Not Connected");

		var packet = new EdenPacket {Type = EdenPacketType.Send, Tag = tag, Data = data};

		var serializedPacket = _serializer.Serialize(packet);
		_stream.Write(serializedPacket);

		_logger?.LogSend(_serverId, packet);
	}
	

	private void Response(string tag, object? data)
	{
		if (!(_tcpClient.Connected && _stream.CanWrite))
			throw new EdenNetworkException("Peer is Not Connected");

		var packet = new EdenPacket {Type = EdenPacketType.Response, Tag = tag, Data = data};

		var serializedPacket = _serializer.Serialize(packet);
		_stream.Write(serializedPacket);
		
		_logger?.LogResponseTo(_serverId, packet);
	}
	
	public void Close()
	{
		_tcpClient.GetStream().Close();
		_tcpClient.Close();
	}
};
