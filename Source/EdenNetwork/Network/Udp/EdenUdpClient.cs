using System.Net;
using EdenNetwork.Dispatcher;
using EdenNetwork.EdenException;
using EdenNetwork.Log;
using EdenNetwork.Packet;
using LiteNetLib;
using LiteNetLib.Utils;
using static EdenNetwork.Constant;

namespace EdenNetwork;

public class EdenUdpClient : IEdenNetClient
{
	private readonly NetManager _netManager;
	private readonly EventBasedNetListener _listener;
	private readonly EventBasedNatPunchListener _punchListener;
	private NetPeer? _peer;

	private readonly EdenPacketSerializer _serializer;
	private readonly EdenClientDispatcher _dispatcher;
	private DeliveryMethod _deliveryMethod;

	private TimeSpan _defaultTimeout;

	private PeerId _serverId;
	
	private readonly Logger? _logger;
	
	public EdenUdpClient()
	{
		_peer = null;
		_listener = new EventBasedNetListener();
		_punchListener = new EventBasedNatPunchListener();
		_netManager = new NetManager(_listener) {AutoRecycle = true, UnsyncedEvents = true};
		
		_defaultTimeout = DEFAULT_TIMEOUT;
		_deliveryMethod = DeliveryMethod.ReliableOrdered;

		_serializer = new EdenPacketSerializer(new EdenDataSerializer());
		_dispatcher = new EdenClientDispatcher(_serializer);
		
		_netManager.NatPunchModule.Init(_punchListener);

		_logger = EdenLogManager.GetLogger();
		
		_netManager.Start();
	}
	
	public EdenUdpClient(string address, int port) : this()
	{
		_serverId = new PeerId(address, port);
	}
	
	public void SetDefaultTimeout(TimeSpan timeout)
	{
		_defaultTimeout = timeout;
	}

	public void SetDeliveryMethod(DeliveryMethod deliveryMethod)
	{
		_deliveryMethod = deliveryMethod;
	}

	public void Close()
	{
		_peer = null;
		_netManager.Stop();
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
		_dispatcher.RemoveEndpoints(endpoints);
	}

	public ConnectionState Connect(TimeSpan? timeout = null)
	{
		timeout ??= _defaultTimeout;

		_listener.PeerDisconnectedEvent += PeerDisconnect;
		_listener.PeerConnectedEvent += PeerConnect;
		_listener.NetworkReceiveEvent += NetworkReceive;
		_netManager.MaxConnectAttempts = (int)(timeout.Value.TotalSeconds) / _netManager.ReconnectDelay;
		
		ConnectionState connectionState = ConnectionState.Timeout;
		bool connectResponded = false;
		
		_netManager.Connect(_serverId.Ip, _serverId.Port, "");
		SpinWait.SpinUntil(() => connectResponded, timeout.Value);
		return connectionState;

		void PeerConnect(NetPeer peer)
		{
			connectResponded = true;
			connectionState = ConnectionState.Ok;
			_peer = peer;
			_logger?.LogConnect(new PeerId(peer.EndPoint));
		}

		void PeerDisconnect(NetPeer peer, DisconnectInfo info)
		{
			if (info.Reason is LiteNetLib.DisconnectReason.ConnectionFailed)
			{
				connectionState = ConnectionState.Fail;
			}
			else
			{
				_dispatcher.DispatchDisconnectMessage((DisconnectReason) info.Reason);
			}
			_logger?.LogDisconnect(new PeerId(peer.EndPoint));
		}
	}

	public async Task<ConnectionState> ConnectAsync(TimeSpan? timeout = null)
	{
		return await Task.Run(() => Connect(timeout));
	}

	public void Send(string tag, object? data = null)
	{
		if (_peer == null)
			throw new EdenNetworkException("Peer is Not Connected");
		EdenPacket packet = new EdenPacket {Type = EdenPacketType.Send,Tag = tag, Data = data};
		var serializedPacket = _serializer.Serialize(packet);
		NetDataWriter writer = new NetDataWriter();
		writer.PutBytesWithLength(serializedPacket);
		_peer.Send(writer, _deliveryMethod);
		_logger?.LogSend(new PeerId(_peer.EndPoint), packet);
	}

	public T? Request<T>(string tag, object? data = null, TimeSpan? timeout = null)
	{
		if (_peer == null)
			throw new EdenNetworkException("Peer is Not Connected");
		
		EdenPacket packet = new EdenPacket {Type = EdenPacketType.Request,Tag = tag, Data = data};
		var serializedPacket = _serializer.Serialize(packet);
		NetDataWriter writer = new NetDataWriter();
		writer.PutBytesWithLength(serializedPacket);
		_peer.Send(writer, _deliveryMethod);
		_logger?.LogRequestTo(new PeerId(_peer.EndPoint), packet);
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

	public ConnectionState RequestNatHolePunching(string address, int port, string additionalInfo = "", TimeSpan? timeout = null)
	{
		timeout ??= _defaultTimeout;
		
		_netManager.NatPunchEnabled = true;
		_netManager.NatPunchModule.UnsyncedEvents = true;
		
		_listener.PeerDisconnectedEvent += PeerDisconnect;
		_listener.PeerConnectedEvent += PeerConnect;
		_listener.NetworkReceiveEvent += NetworkReceive;
		_netManager.MaxConnectAttempts = (int)(timeout.Value.TotalSeconds) / _netManager.ReconnectDelay;
		_listener.ConnectionRequestEvent += request => request.Accept();
		_punchListener.NatIntroductionSuccess += NatIntroductionSuccess;
		
		_netManager.NatPunchModule.SendNatIntroduceRequest(address, port, additionalInfo);
		
		var connectionState = ConnectionState.Timeout;
		var connectionResponded = false;
		SpinWait.SpinUntil(() => connectionResponded, timeout.Value);
		return connectionState;

		void NatIntroductionSuccess(IPEndPoint targetEndPoint, NatAddressType type, string key)
		{
			_serverId = new PeerId(targetEndPoint);
			_netManager.Connect(targetEndPoint, "");
		}

		void PeerConnect(NetPeer peer)
		{
			connectionState = ConnectionState.Ok;
			_peer = peer;
			_logger?.LogConnect(new PeerId(peer.EndPoint));
			connectionResponded = true;
		}

		void PeerDisconnect(NetPeer peer, DisconnectInfo info)
		{
			if (info.Reason is LiteNetLib.DisconnectReason.ConnectionFailed)
			{
				connectionState = ConnectionState.Fail;
			}
			else if(info.Reason is not LiteNetLib.DisconnectReason.PeerToPeerConnection)
			{
				_dispatcher.DispatchDisconnectMessage((DisconnectReason) info.Reason);
			}
			_logger?.LogDisconnect(new PeerId(peer.EndPoint));
		}
	}

	public async Task<ConnectionState> RequestNatHolePunchingAsync(string address, int port, string additionalInfo = "", TimeSpan? timeout = null)
	{
		return await Task.Run(() => RequestNatHolePunching(address, port, additionalInfo, timeout));
	}
	
	
	private void NetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
	{
		var serverId = new PeerId(peer.EndPoint);
		if (reader.TryGetBytesWithLength(out var packetBytes) == false)
		{
			_logger?.LogUnformattedPacketError(serverId);
			return;
		}

		try
		{
			var packet = _serializer.Deserialize(packetBytes);
			if (packet.Type == EdenPacketType.Response)
			{
				_dispatcher.DispatchResponsePacket(packet);
				_logger?.LogResponseFrom(serverId, packet);
			}
			else if(packet.Type == EdenPacketType.Send)
			{
				_dispatcher.DispatchSendPacket(packet);
				_logger?.LogReceive(serverId, packet);
			}
			else
			{
				_logger?.LogUnformattedPacketError(serverId);
				return;
			}
		}
		catch (Exception e)
		{
			_logger?.LogUnformattedPacketError(serverId, e);
			return;
		}

	}
}