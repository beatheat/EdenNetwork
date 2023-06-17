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
	private readonly PeerId _clientId;

	private readonly Logger? _logger;

	public PeerId ClientId => _clientId;
	
	public TcpPeer(TcpClient tcpClient, PeerId clientId, EdenPacketSerializer serializer, EdenServerDispatcher dispatcher, EdenTcpServer parent, Logger? logger)
	{
		_tcpClient = tcpClient;
		_stream = tcpClient.GetStream();
		_clientId = clientId;
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
					_logger?.LogUnformattedPacketError(_clientId);
					continue;
				}
				var packetBytes = new byte[packetLength];
				if (!(_tcpClient.Connected && _stream.CanRead))
					break;
				_stream.Read(packetBytes);
				Task.Run(() => NetworkReceive(packetBytes, packetLength));
			}
			_parent.DisconnectClient(_clientId);
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
				_logger?.LogUnformattedPacketError(_clientId,e);
				return;
			}
			
			try
			{
				if (packet.Type == EdenPacketType.Request)
				{
					var responseData = _dispatcher.DispatchRequestPacket(_clientId, packet);
					_logger?.LogRequestFrom(_clientId, packet);
					Response(packet.Tag, responseData);
				}
				else if(packet.Type == EdenPacketType.Send)
				{
					_dispatcher.DispatchSendPacket(_clientId, packet);
					_logger?.LogRequestFrom(_clientId, packet);
				}
				else
				{
					//Ignore Not Formatted Packet
					_logger?.LogUnformattedPacketError(_clientId);
					return;
				}
			}
			catch (Exception e)
			{
				_logger?.LogUnformattedPacketError(_clientId, e);
				return;
			}
			
		
		}
	}

	public EdenPacket Send(string tag, object? data)
	{
		if (!(_tcpClient.Connected && _stream.CanWrite))
			throw new EdenNetworkException("Peer is Not Connected");

		var packet = new EdenPacket {Type = EdenPacketType.Send, Tag = tag, Data = data};

		var serializedPacket = _serializer.Serialize(packet);
		_stream.Write(serializedPacket);
		return packet;
	}
	

	private void Response(string tag, object? data)
	{
		if (!(_tcpClient.Connected && _stream.CanWrite))
			throw new EdenNetworkException("Peer is Not Connected");

		var packet = new EdenPacket {Type = EdenPacketType.Response, Tag = tag, Data = data};

		var serializedPacket = _serializer.Serialize(packet);
		_stream.Write(serializedPacket);
		
		_logger?.LogResponseTo(_clientId, packet);
	}
	
	public void Close()
	{
		_tcpClient.GetStream().Close();
		_tcpClient.Close();
	}
}