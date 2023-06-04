using System.Net;
using EdenNetwork.Dispatcher;
using EdenNetwork.EdenException;
using EdenNetwork.Log;
using EdenNetwork.Packet;
using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.Extensions.Logging;
using ZLogger;
using static EdenNetwork.Constant;

namespace EdenNetwork;

public class EdenUdpServer : IEdenNetServer
{
	private readonly NetManager _netManager;
	private readonly EventBasedNetListener _listener;
	private readonly EventBasedNatPunchListener _punchListener;
	private readonly Dictionary<PeerId, NetPeer> _clients;
	private readonly EdenServerDispatcher _dispatcher;
	private readonly EdenPacketSerializer _serializer;
	
	private readonly string _address;
	private readonly int _port;
	
	private DeliveryMethod _deliveryMethod;

	private readonly ILogger? _logger;
	
	public EdenUdpServer(string address, int port)
	{
		_listener = new EventBasedNetListener();
		_punchListener = new EventBasedNatPunchListener();
		_netManager = new NetManager(_listener) {AutoRecycle = true, UnsyncedEvents = true};
		_clients = new Dictionary<PeerId, NetPeer>();
		_serializer = new EdenPacketSerializer(new EdenDataSerializer());
		_dispatcher = new EdenServerDispatcher(_serializer);
		_deliveryMethod = DeliveryMethod.ReliableOrdered;

		_address = address;
		_port = port;
		_logger = EdenLogManager.GetLogger<EdenUdpServer>();

		_netManager.NatPunchModule.Init(_punchListener);
	}

	public EdenUdpServer(int port) : this("0.0.0.0", port) {}

	public void SetSerializer(IEdenDataSerializer serializer)
	{
		_serializer.SetSerializer(serializer);
	}

	public void SetDeliveryMethod(DeliveryMethod deliveryMethod)
	{
		_deliveryMethod = deliveryMethod;
	}
	
	public void Close()
	{
		_netManager.Stop(true);
	}

	public void Listen(int maxAcceptNum)
	{
		_netManager.Start(_address, "::", _port);
		_listener.ConnectionRequestEvent += request => { request.Accept(); };
		_listener.PeerConnectedEvent += PeerConnect;
		_listener.PeerDisconnectedEvent += PeerDisconnect;
		_listener.NetworkReceiveEvent += NetworkReceive;

		void PeerConnect(NetPeer peer)
		{
			if (_netManager.ConnectedPeersCount > maxAcceptNum)
			{
				var writer = new NetDataWriter();
				writer.Put((byte) ConnectionState.FULL);
				peer.Disconnect(writer);
			}

			var clientId = new PeerId(peer.EndPoint);
			lock (_clients)
			{
				_clients.Add(clientId, peer);
			}
			_dispatcher.DispatchConnectMessage(clientId);
			_logger?.LogConnect(clientId);
		}

		void PeerDisconnect(NetPeer peer, DisconnectInfo info)
		{
			var clientId = new PeerId(peer.EndPoint);
			lock (_clients)
			{
				_clients.Remove(clientId);
			}
			_dispatcher.DispatchDisconnectMessage(clientId, (DisconnectReason) info.Reason);
			_logger?.LogDisconnect(clientId);
		}
		
		void NetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
		{
			var clientId = new PeerId(peer.EndPoint);
			//Ignore Not Formatted Packet
			if (reader.TryGetBytesWithLength(out var packetBytes) == false)
			{
				_logger?.LogUnformattedPacketError(clientId);
				return;
			}

			try
			{
				var packet = _serializer.Deserialize(packetBytes);
				if (packet.Type == EdenPacketType.Request)
				{
					var responseData = _dispatcher.DispatchRequestPacket(clientId, packet);
					_logger?.LogRequestFrom(clientId, packet);
					Response(packet.Tag, clientId, responseData);
				}
				else if (packet.Type == EdenPacketType.Send)
				{

					_dispatcher.DispatchSendPacket(clientId, packet);
					_logger?.LogReceive(clientId, packet);
				}
				else
				{
					_logger?.LogUnformattedPacketError(clientId);
					return;
				}
			}
			catch (Exception e)
			{
				_logger?.LogUnformattedPacketError(clientId, e);
				return;
			}
		}
	}

	public void AddEndpoints(params object[] endpoints) 
	{
		_dispatcher.AddEndpoints(endpoints);
	}
	
	public void RemoveEndpoints(params object[] endpoints)
	{
		_dispatcher.RemoveEndpoints(endpoints);
	}

	public void DisconnectClient(PeerId clientId)
	{
		if (_clients.ContainsKey(clientId))
		{
			_clients[clientId].Disconnect();
		}
	}

	public void Send(string tag, PeerId clientId, object? data = null)
	{
		if (_clients.TryGetValue(clientId, out var peer) == false)
		{
			throw new EdenNetworkException("Peer is Not Connected");
		}
            
		var packet = new EdenPacket {Type = EdenPacketType.Send, Tag = tag, Data = data};
		var packetBytes = _serializer.Serialize(packet);
		
		NetDataWriter writer = new NetDataWriter();
		writer.PutBytesWithLength(packetBytes);
		peer.Send(writer, _deliveryMethod);
		_logger?.LogSend(clientId, packet);
	}

	private void Response(string tag, PeerId clientId, object? data = null)
	{
		if (_clients.TryGetValue(clientId, out var peer) == false)
		{
			throw new EdenNetworkException("Peer is Not Connected");
		}
            
		var packet = new EdenPacket {Type = EdenPacketType.Response, Tag = tag, Data = data};
		var packetBytes = _serializer.Serialize(packet);
		
		NetDataWriter writer = new NetDataWriter();
		writer.PutBytesWithLength(packetBytes);
		peer.Send(writer, _deliveryMethod);
		_logger?.LogResponseTo(clientId, packet);
	}

	public void Broadcast(string tag, object? data = null)
	{
		var packet = new EdenPacket {Tag = tag, Data = data};
		var packetBytes = _serializer.Serialize(packet);
		
		NetDataWriter writer = new NetDataWriter();
		writer.PutBytesWithLength(packetBytes);
		_netManager.SendToAll(writer, _deliveryMethod);
	}

	public void BroadcastExcept(string tag, PeerId clientId, object? data = null)
	{
		if (_clients.TryGetValue(clientId, out var exceptPeer))
		{
			throw new EdenNetworkException("Peer is Not Connected");
		}
		var packet = new EdenPacket {Tag = tag, Data = data};
		var packetBytes = _serializer.Serialize(packet);
		
		NetDataWriter writer = new NetDataWriter();
		writer.PutBytesWithLength(packetBytes);
		_netManager.SendToAll(writer, _deliveryMethod, exceptPeer);	
	}

	public async Task SendAsync(string tag, PeerId clientId, object? data = null) 
	{
		await Task.Run(() => Send(tag, clientId, data));
	}

	public async Task BroadcastAsync(string tag, object? data = null)
	{
		await Task.Run(() => BroadcastAsync(tag, data));
	}

	public async Task BroadcastExceptAsync(string tag, PeerId clientId, object? data = null)
	{
		await Task.Run(() => BroadcastExceptAsync(tag, clientId, data));
	}

	public void RegisterNatHolePunching(string address, int port, string additionalData = "", double timeout = -1)
	{
		_netManager.NatPunchEnabled = true;
		_netManager.NatPunchModule.UnsyncedEvents = true;
		
		_punchListener.NatIntroductionSuccess += (IPEndPoint targetEndPoint, NatAddressType type, string key) =>
		{
			var peer = _netManager.Connect(targetEndPoint, "");
		};
		_netManager.NatPunchModule.SendNatIntroduceRequest(address, port, additionalData);
	}
	
	public bool RequestNatHolePunching(string address, int port, string additionalData = "", double timeout = -1)
	{
		_netManager.NatPunchEnabled = true;
		_netManager.NatPunchModule.UnsyncedEvents = true;
		
		bool success = false;
		_punchListener.NatIntroductionSuccess += (IPEndPoint targetEndPoint, NatAddressType type, string key) =>
		{
			var peer = _netManager.Connect(targetEndPoint, "");
			success = peer != null;
		};
		_netManager.NatPunchModule.SendNatIntroduceRequest(address, port, additionalData);
		if (timeout < 0) timeout = DEFAULT_TIMEOUT;
		EdenUtil.WaitUntilFlagOn(ref success, timeout);
		return success;
	}
	
	public async Task<bool> RequestNatHolePunchingAsync(string address, int port, string additionalData = "", double timeout = -1)
	{
		return await Task.Run(() => RequestNatHolePunching(address,port,additionalData,timeout));
	}
	
	public void SetNatRequestListener()
	{
		_netManager.NatPunchEnabled = true;
		_netManager.NatPunchModule.UnsyncedEvents = true;
		_punchListener.NatIntroductionRequest += (localEndPoint, remoteEndPoint, additionalData) =>
		{
			var hostEndPoint = _dispatcher.DispatchNatRelayMessage(new NatPeer {LocalEndPoint = new PeerId(localEndPoint), RemoteEndPoint = new PeerId(remoteEndPoint)});
			if (hostEndPoint != null)
			{
				_netManager.NatPunchModule.NatIntroduce(
					hostEndPoint.LocalEndPoint.ToIPEndPoint(), hostEndPoint.RemoteEndPoint.ToIPEndPoint(), localEndPoint, remoteEndPoint, additionalData);
			}
		};
	}

}