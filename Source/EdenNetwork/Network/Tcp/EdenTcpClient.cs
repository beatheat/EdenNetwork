using System.Net.Sockets;
using EdenNetwork.Dispatcher;
using EdenNetwork.EdenException;
using EdenNetwork.Log;
using EdenNetwork.Packet;
using static EdenNetwork.Constant;

namespace EdenNetwork;

public class EdenTcpClient : IEdenNetClient
{
	private readonly TcpClient _peer;
	private readonly EdenClientDispatcher _dispatcher;
	private readonly EdenPacketSerializer _serializer;
	private TimeSpan _defaultTimeout;

	private readonly Logger? _logger;

	private readonly PeerId _serverId;

	public EdenTcpClient(string address, int port)
	{
		_serializer = new EdenPacketSerializer(new EdenDataSerializer());
		_dispatcher = new EdenClientDispatcher(_serializer);
		_peer = new TcpClient(AddressFamily.InterNetwork);
		_defaultTimeout = DEFAULT_TIMEOUT;

		_serverId = new PeerId(address, port);
		
		_logger = EdenLogManager.GetLogger();
	}
	
	public void Close()
	{
		_peer.GetStream()?.Close();
		_peer.Close();
	}

	public void SetDefaultTimeout(TimeSpan timeout)
	{
		_defaultTimeout = timeout;
	}

	public void SetSerializer(IEdenDataSerializer serializer)
	{
		_serializer.SetSerializer(serializer);
	}

	public void AddEndpoints(params object[] endpoints)
	{
		_dispatcher.AddEndpoints(endpoints);
	}

	public void RemoveEndpoints(params object[] endpoints)
	{
		_dispatcher.AddEndpoints(endpoints);
	}

	public ConnectionState Connect(TimeSpan? timeout = null)
	{
		timeout ??= _defaultTimeout;
		try
		{
			if (!_peer.ConnectAsync(_serverId.Ip,_serverId.Port).Wait(timeout.Value))
			{
				return ConnectionState.Timeout;
			}
		}
		catch 
		{
			return ConnectionState.Fail;
		}

		var buffer = new byte[4];
		_peer.GetStream().Read(buffer);
		var serverState = (ConnectionState) BitConverter.ToInt32(buffer);
		if (serverState == ConnectionState.Ok)
		{
			BeginReceive();
		}
		else
		{
			Close();
		}
		
		return serverState;
	}

	public async Task<ConnectionState> ConnectAsync(TimeSpan? timeout = null)
	{
		return await Task.Run(() => Connect(timeout));
	}

	public void Send(string tag, object? data = null)
	{
		if (!(_peer.Connected && _peer.GetStream().CanWrite))
			throw new EdenNetworkException("Peer is Not Connected");

		var packet = new EdenPacket {Type = EdenPacketType.Send, Tag = tag, Data = data};

		var serializedPacket = _serializer.Serialize(packet);
		_peer.GetStream().Write(serializedPacket);
		_logger?.LogSend(_serverId, packet);
	}

	public T? Request<T>(string tag, object? data = null, TimeSpan? timeout = null)
	{
		if (!(_peer.Connected && _peer.GetStream().CanWrite))
			throw new EdenNetworkException("Peer is Not Connected");

		var packet = new EdenPacket {Type = EdenPacketType.Request, Tag = tag, Data = data};

		var serializedPacket = _serializer.Serialize(packet);
        _peer.GetStream().Write(serializedPacket);
        
        _logger?.LogRequestTo(_serverId, packet);

        timeout ??= _defaultTimeout;
		var responseData = _dispatcher.WaitResponse<T>(tag, timeout.Value);
		return responseData;
	}

	public async Task SendAsync(string tag, object? data = null)
	{
		await Task.Run(() => Send(tag, data));
	}

	public async Task<T?> RequestAsync<T>(string tag, object? data = null, TimeSpan? timeout = null)
	{
		return await Task.Run(() => Request<T>(tag, data, timeout));
	}
	
	private void BeginReceive()
	{
		Task.Run(() =>
		{
			var stream = _peer.GetStream();
			while (stream.CanRead)
			{
				var packetLengthByte = new byte[2];
				stream.Read(packetLengthByte);
				var packetLength = _serializer.GetPacketLength(packetLengthByte);
				if (packetLength < 0)
				{
					//Unknown Packet Structure
					continue;
				}
				var packetBytes = new byte[packetLength];
				stream.Read(packetBytes);
				Task.Run(() => NetworkReceive(packetBytes, packetLength));
			}
			_dispatcher.DispatchDisconnectMessage(DisconnectReason.RemoteConnectionClose);
			Close();
		});

		void NetworkReceive(byte[] serializedPacket, int packetLength)
		{
			try
			{
				var packet = _serializer.Deserialize(serializedPacket, packetLength);
				if (packet.Type == EdenPacketType.Response)
				{
					_dispatcher.DispatchResponsePacket(packet);
					_logger?.LogResponseFrom(_serverId, packet);
				}
				else if(packet.Type == EdenPacketType.Send)
				{
					_dispatcher.DispatchSendPacket(packet);
					_logger?.LogReceive(_serverId, packet);
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
}