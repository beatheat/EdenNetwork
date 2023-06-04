using System.Net;
using System.Net.Sockets;
using EdenNetwork.Dispatcher;
using EdenNetwork.EdenException;
using EdenNetwork.Log;
using EdenNetwork.Packet;

namespace EdenNetwork;

public class EdenTcpServer : IEdenNetServer
{
	
	private readonly TcpListener _server;
	private readonly Dictionary<PeerId, TcpPeer> _clients;
	private readonly EdenServerDispatcher _dispatcher;
	private readonly EdenPacketSerializer _serializer;
	
	private bool _isListening;

	private readonly Logger? _logger;
	
	public EdenTcpServer(string address, int port)
	{
		_server = new TcpListener(IPEndPoint.Parse($"{address}:{port}"));
		_clients = new Dictionary<PeerId, TcpPeer>();
		_serializer = new EdenPacketSerializer(new EdenDataSerializer());
		_dispatcher = new EdenServerDispatcher(_serializer);
		_logger = EdenLogManager.GetLogger();
	}
	
	public EdenTcpServer(int port) : this("0.0.0.0", port) {}


	public void Close()
	{
		_isListening = false;
		foreach (var client in _clients.Values)
		{
			client.Close();
		}
		_server.Stop();
	}

	public void SetSerializer(IEdenDataSerializer serializer)
	{
		_serializer.SetSerializer(serializer);
	}
	
	public void Listen(int maxAcceptNum)
	{
		_isListening = true;
		_server.Start();
		Task.Run(() =>
		{
			while (_isListening)
			{
				var tcpClient = _server.AcceptTcpClient();
				if (_clients.Count > maxAcceptNum)
				{
					tcpClient.GetStream().Write(BitConverter.GetBytes((int) ConnectionState.Full));
					tcpClient.GetStream().Close();
					tcpClient.Close();
					return;
				}

				var remoteEndpoint = (IPEndPoint) tcpClient.Client.RemoteEndPoint!;
				var clientId = new PeerId(remoteEndpoint);
				var tcpPeer = new TcpPeer(tcpClient, clientId, _serializer, _dispatcher, this, _logger);
				_clients.Add(clientId, tcpPeer);
				_dispatcher.DispatchConnectMessage(clientId);
				tcpPeer.BeginReceive();
				tcpClient.GetStream().Write(BitConverter.GetBytes((int) ConnectionState.Ok));
				
				_logger?.LogConnect(clientId);
			}
		});
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
			_clients[clientId].Close();
			_clients.Remove(clientId);
			_dispatcher.DispatchDisconnectMessage(clientId, DisconnectReason.RemoteConnectionClose);
			_logger?.LogDisconnect(clientId);
		}
	}

	public void Send(string tag, PeerId clientId, object? data = null)
	{
		if (_clients.TryGetValue(clientId, out var client) == false)
			throw new EdenNetworkException("Peer is Not Connected");
		client.Send(tag, data);
	}

	public void Broadcast(string tag, object? data = null)
	{
		foreach(var client in _clients.Values)
			client.Send(tag, data);	
	}

	public void BroadcastExcept(string tag, PeerId clientId, object? data = null)
	{
		foreach (var client in _clients.Values)
		{
			if(client.ServerId != clientId)
				client.Send(tag, data);
		}
	}

	public async Task SendAsync(string tag, PeerId clientId, object? data = null)
	{
		await Task.Run(() => Send(tag, clientId, data));
	}

	public async Task BroadcastAsync(string tag, object? data = null)
	{
		await Task.Run(() => Broadcast(tag, data));
	}

	public async Task BroadcastExceptAsync(string tag, PeerId clientId, object? data = null)
	{
		await Task.Run(() => BroadcastExcept(tag, clientId, data));
	}
}