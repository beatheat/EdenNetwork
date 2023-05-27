using System.Net.Sockets;
using EdenNetwork.Dispatcher;
using EdenNetwork.EdenException;
using EdenNetwork.Log;
using EdenNetwork.Packet;
using Microsoft.Extensions.Logging;

namespace EdenNetwork;

internal class TcpPeer
{
	private readonly TcpClient _tcpClient;
	private readonly NetworkStream _stream;
	private readonly EdenServerDispatcher _dispatcher;
	private readonly EdenPacketSerializer _serializer;
	private readonly EdenTcpServer _parent;
	private readonly PeerId _peerId;

	private ILogger? _logger;

	public PeerId PeerId => _peerId;
	
	public TcpPeer(TcpClient tcpClient, PeerId peerId, EdenPacketSerializer serializer, EdenServerDispatcher dispatcher, EdenTcpServer parent, ILogger? logger)
	{
		_tcpClient = tcpClient;
		_stream = tcpClient.GetStream();
		_peerId = peerId;
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
					_logger?.LogUnformattedPacketError(_peerId);
					continue;
				}
				var packetBytes = new byte[packetLength];
				if (!(_tcpClient.Connected && _stream.CanRead))
					break;
				_stream.Read(packetBytes);
				Task.Run(() => NetworkReceive(packetBytes, packetLength));
			}
			_parent.DisconnectClient(_peerId);
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
				_logger?.LogUnformattedPacketError(_peerId,e);
				return;
			}
			if (packet.Type == EdenPacketType.Request)
			{
				var responseData = _dispatcher.DispatchRequestPacket(_peerId, packet);
				_logger?.LogRequestFrom(_peerId, packet);
				Response(packet.Tag, responseData);
			}
			else if(packet.Type == EdenPacketType.Send)
			{
				_dispatcher.DispatchSendPacket(_peerId, packet);
				_logger?.LogRequestFrom(_peerId, packet);
			}
			else
			{
				//Ignore Not Formatted Packet
				_logger?.LogUnformattedPacketError(_peerId);
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

		_logger?.LogSend(_peerId, packet);
	}
	

	private void Response(string tag, object? data)
	{
		if (!(_tcpClient.Connected && _stream.CanWrite))
			throw new EdenNetworkException("Peer is Not Connected");

		var packet = new EdenPacket {Type = EdenPacketType.Response, Tag = tag, Data = data};

		var serializedPacket = _serializer.Serialize(packet);
		_stream.Write(serializedPacket);
		
		_logger?.LogResponseTo(_peerId, packet);
	}
	
	public void Close()
	{
		_tcpClient.GetStream().Close();
		_tcpClient.Close();
	}
};
