using System.Text;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Serialization;
using static EdenNetwork.Constant;
using LiteNetLib;
using LiteNetLib.Utils;

namespace EdenNetwork.Udp
{
    public class EdenUdpServer
    {
        #region Pre-defined Class&Struct
        
        /// <summary>
        /// Receive packet
        /// </summary>
        internal struct Receiver
        {
            public bool log;
            public Action<string, EdenData> receiveEvent;
            public Func<string, EdenData, EdenData> responseEvent;
        }

        #endregion
        #region Fields
        /// <summary>
        /// Constant : constant for infinite listen number of client
        /// </summary>
        public const int INF_CLIENT_ACCEPT = int.MaxValue;

        private readonly NetManager _netManager;
        private readonly EventBasedNetListener _listener;
        private readonly Dictionary<string, NetPeer> _clients;
        private readonly Dictionary<string, Receiver> _receiveEvents;
        private readonly Dictionary<string, Receiver> _responseEvents;
        private Action<string, DisconnectReason>? _disconnectEvent;

        private readonly Logger? _logger;

        private JsonSerializerOptions _options;
        
        private bool _ignoreUnknownPacket;
        
        private readonly string _address;
        private readonly int _port;
        
        public int ClientNum => _clients.Count;

        #endregion
        #region Constructor
        /// <summary>
        /// Constructor for EdenUdpServer
        /// </summary>
        /// <param name="address">IPv4 address to open server</param>
        /// <param name="port">Port number to open server</param>
        /// <param name="logPath">Path for log data to write</param>
        /// <param name="printConsole">print log on console or not</param>
        /// <param name="flushInterval">log stream flush interval in milliseconds</param>
        /// <exception cref="Exception">
        /// Exception exist
        /// case 1. Cannot open stream for log file path
        /// </exception>
        public EdenUdpServer(string address, int port, string logPath, bool printConsole = true, int flushInterval = Logger.DEFAULT_FLUSH_INTERVAL)
        {
            this._listener = new EventBasedNetListener();
            this._netManager = new NetManager(_listener) {AutoRecycle = true, UnsyncedEvents = true};
            this._clients = new Dictionary<string, NetPeer>();
            _receiveEvents = new Dictionary<string, Receiver>();
            _responseEvents = new Dictionary<string, Receiver>();

            this._address = address;
            this._port = port;
            
            _disconnectEvent = null;
            _ignoreUnknownPacket = false;
            
            _options = new JsonSerializerOptions {IncludeFields = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull};
            
            if (logPath != "")
                _logger = new Logger(logPath, printConsole, flushInterval);
        }
        /// <summary>
        /// Constructor for EdenUdpServer
        /// </summary>
        /// <param name="address">IPv4 address to open server</param>
        /// <param name="port">Port number to open server</param>
        /// <exception cref="Exception">
        /// Exception exist
        /// case 1. Cannot open stream for log file path
        /// </exception>
        public EdenUdpServer(string address, int port) : this(address, port, "") { }
        /// <summary>
        /// Constructor for EdenUdpServer
        /// </summary>
        /// <param name="port">Port number to open server</param>
        /// <param name="logPath">Path for log data to write</param>
        /// <param name="printConsole">print log on console or not</param>
        /// <param name="flushInterval">log stream flush interval in milliseconds</param>
        /// <exception cref="Exception">
        /// Exception exist
        /// case 1. Cannot open stream for log file path
        /// </exception>
        public EdenUdpServer(int port, string logPath,  bool printConsole = true, int flushInterval = Logger.DEFAULT_FLUSH_INTERVAL) : this("0.0.0.0", port, logPath, printConsole, flushInterval) { }
        /// <summary>
        /// Constructor for EdenNetSerer
        /// </summary>
        /// <param name="port">Port number to open server</param>
        /// <exception cref="Exception">
        /// Exception exist
        /// case 1. Cannot open stream for log file path
        /// </exception>
        public EdenUdpServer(int port) : this("0.0.0.0", port, "") { }
        
        #endregion
        #region General Methods
        /// <summary>
        /// Check specific UDP port is available
        /// </summary>
        /// <param name="port"></param>
        public static bool IsPortAvailable(int port)
        {
            bool isAvailable = true;

            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            var activeUdpListeners = ipGlobalProperties.GetActiveUdpListeners();

            foreach (var udpInfo in activeUdpListeners)
            {
                if (udpInfo.Port==port)
                {
                    isAvailable = false;
                    break;
                }
            }
            
            return isAvailable;
        }
        
        
        /// <summary>
        /// Do not log unknown receive packets
        /// </summary>
        /// <param name="ignore">ignore or not</param>
        public void SetIgnoreUnknownPacketTag(bool ignore)
        {
            _ignoreUnknownPacket = ignore;
        }
        
        /// <summary>
        /// Set JsonSerialize Option
        /// Default Option is {IncludeFields = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNul}
        /// </summary>
        /// <param name="options">JsonSerialize Option</param>
        public void SetSerializeOption(JsonSerializerOptions options)
        {
            _options = options;
        }

        
        /// <summary>
        /// Append receive event which response for packet named with specific tag 
        /// </summary>
        /// <param name="tag">responsible tag name for packet received</param>
        /// <param name="receiveEvent">
        ///     event that processed by packet received <br/>
        ///     arg1 = string client_id <br/>
        ///     arg2 = EdenData data
        /// </param>
        public void AddReceiveEvent(string tag, Action<string, EdenData> receiveEvent, bool log = true)
        {
            if(_receiveEvents.ContainsKey(tag))
            {
                _logger?.Log($"Error! AddReceiveEvent - receive event tag({tag}) already exists");
                return;
            }
            _receiveEvents.Add(tag, new Receiver {receiveEvent = receiveEvent, log = log});
        }
        
        /// <summary>
        /// Remove receive event which response for packet name with specific tag
        /// </summary>
        /// <param name="tag">responsible tag name for packet received</param>
        public void RemoveReceiveEvent(string tag)
        {
            if (!_receiveEvents.ContainsKey(tag))
            {
                _logger?.Log($"Error! RemoveReceiveEvent - receive event tag({tag}) does not exist");
                return;
            }
            _receiveEvents.Remove(tag);
        }
        
        
        /// <summary>
        /// Append response event which response for request message named with specific tag 
        /// </summary>
        /// <param name="tag">responsible tag name for packet received</param>
        /// <param name="response">
        ///     event that processed by packet received <br/>
        ///     arg1 = string clientId <br/>
        ///     arg2 = EdenData data
        ///     return response data to client
        /// </param>
        public void AddResponse(string tag, Func<string, EdenData, EdenData> response, bool log = true)
        {
            if (_responseEvents.ContainsKey(REQUEST_PREFIX + tag))
            {
                _logger?.Log($"Error! AddResponse - receive event tag({tag}) already exists");
                return;
            }
            _responseEvents.Add(REQUEST_PREFIX + tag, new Receiver{responseEvent = response, log = log});
        }

        /// <summary>
        /// Remove response event which response for packet name with specific tag
        /// </summary>
        /// <param name="tag">responsible tag name for packet received</param>
        public void RemoveResponse(string tag)
        {
            if (!_responseEvents.ContainsKey(REQUEST_PREFIX + tag))
            {
                _logger?.Log($"Error! RemoveResponse - response tag({tag}) does not exist");
                return;
            }
            _responseEvents.Remove(REQUEST_PREFIX + tag);
        }
        
        /// <summary>
        /// Append event activates when client connection close
        /// </summary>
        /// <param name="callback">
        ///     callback for run after client disconnected <br/>
        ///     arg1 = string clientId
        /// </param>
        public void SetClientDisconnectEvent(Action<string, DisconnectReason> callback)
        {
            _disconnectEvent = callback;
        }

        /// <summary>
        /// set null to the event activates when client connection close
        /// </summary>
        public void ResetClientDisconnectEvent()
        {
            _disconnectEvent = null;
        }
        
        /// <summary>
        /// Force disconnect client connection from server
        /// </summary>
        /// <param name="clientId">client id</param>
        public void DisconnectClient(string clientId)
        {
            if (_clients.ContainsKey(clientId))
            {
                _clients[clientId].Disconnect();
            }
        }
        
        
        /// <summary>
        /// Listen and wait for client connection
        /// </summary>
        /// <param name="maxAcceptNum">max number of clients to accept</param>
        /// <param name="key">accept key which reject requests without correct key</param>
        /// <param name="callback">  
        ///     callback for run after client accepted <br/>
        ///     arg1 = string clientId
        /// </param>
        public void Listen(int maxAcceptNum, string key, Action<string>? callback = null)
        {
            callback ??= _ => { };
            _netManager.Start(_address,"::",_port);
            _listener.ConnectionRequestEvent += request =>
            {
                request.AcceptIfKey(key);
            };
            
            _listener.PeerConnectedEvent += peer =>
            {
                string clientId = peer.EndPoint.ToString();
                NetDataWriter writer = new NetDataWriter();
                if (_netManager.ConnectedPeersCount > maxAcceptNum)
                {
                    writer.Put((byte)ConnectionState.FULL);
                    peer.Disconnect(writer);
                }
                _logger?.Log($"Remote client connected: {clientId}");
                _clients.Add(clientId, peer);
                callback(clientId);
            };

            _listener.PeerDisconnectedEvent += (peer, info) =>
            {
                _disconnectEvent?.Invoke(peer.EndPoint.ToString(), (DisconnectReason) info.Reason);
            };
            
            _listener.NetworkReceiveEvent += NetworkReceive;

            _logger?.Log("Eden Server is listening on  " + _address + ":" + _port);
        }

        /// <summary>
        /// Listen and wait for client connection
        /// </summary>
        /// <param name="maxAcceptNum">max number of clients to accept</param>
        /// <param name="callback">  
        ///     callback for run after client accepted <br/>
        ///     arg1 = string clientId
        /// </param>
        public void Listen(int maxAcceptNum, Action<string>? callback = null)
        {
            callback ??= _ => { };
            Listen(maxAcceptNum, DEFAULT_KEY, callback);
        }
        
        /// <summary>
        /// Listen and wait for client connection
        /// </summary>
        /// <param name="callback">  
        ///     callback for run after client accepted <br/>
        ///     arg1 = string clientId
        /// </param>
        public void Listen(Action<string>? callback = null)
        {
            callback ??= _ => { };
            Listen(INF_CLIENT_ACCEPT, DEFAULT_KEY, callback);
        }
        
        /// <summary>
        /// Close EdenUdpServer
        /// </summary>
        public void Close()
        {
            _netManager.Stop(true);
            _logger?.Log("EdenUdpServer is closed");
            _logger?.Close();
        }

        #endregion
        #region Network Transmission Methods
        #region Send Methods
        /// <summary>
        /// Send data json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for send</param>
        /// <param name="data">EdenData structured sending data </param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public bool Send(string tag, string clientId, EdenData data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            if (!_clients.TryGetValue(clientId, out var peer))
            {
                _logger?.Log($"Error! Send - client tag is wrong");
                return false;
            }
            
            EdenPacket packet = new EdenPacket {tag = tag, data = data};

            string jsonPacket = JsonSerializer.Serialize(packet, _options);
            NetDataWriter writer = new NetDataWriter();
            writer.Put(jsonPacket);

            peer.Send(writer, deliveryMethod);

            if (log)
                _logger?.Log($"Send({clientId}/{writer.Data.Length,5}B) : [TAG] {tag} " + $"[DATA] {JsonSerializer.Serialize(data.data, _options)}");
            return true;
        }

        /// <summary>
        /// Send data json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for send</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public bool Send(string tag, string clientId, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return Send(tag, clientId, new EdenData(), deliveryMethod, log);
        }
        /// <summary>
        /// Send data json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for send</param>
        /// <param name="data">sending data</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public bool Send(string tag, string clientId, object? data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return Send(tag, clientId, new EdenData(), deliveryMethod, log);
        }
        
        /// <summary>
        /// Send data json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for send</param>
        /// <param name="data">sending data </param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public bool Send(string tag, string clientId, object[] data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return Send(tag, clientId, new EdenData(data), deliveryMethod, log);
        }
        
        /// <summary>
        /// Send data json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for send</param>
        /// <param name="data">sending data </param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public bool Send(string tag, string clientId, Dictionary<string,object> data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return Send(tag, clientId, new EdenData(data), deliveryMethod, log);
        }
        
        /// <summary>
        /// Send data asynchronously json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for send</param>
        /// <param name="data">sending data</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> SendAsync(string tag, string clientId, EdenData data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return await Task.Run(() => Send(tag, clientId, data, deliveryMethod, log));
        }
        
        /// <summary>
        /// Send data asynchronously json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for send</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> SendAsync(string tag, string clientId, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return await SendAsync(tag, clientId, new EdenData(), deliveryMethod, log);
        }
        /// <summary>
        /// Send data asynchronously json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for send</param>
        /// <param name="data">sending data</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> SendAsync(string tag, string clientId, object? data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return await SendAsync(tag, clientId, new EdenData(data), deliveryMethod, log);
        }
        /// <summary>
        /// Send data asynchronously json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for send</param>
        /// <param name="data">object array sending data</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> SendAsync(string tag, string clientId, object[] data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return await SendAsync(tag, clientId, new EdenData(data), deliveryMethod, log);
        }
        /// <summary>
        /// Send data asynchronously json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for send</param>
        /// <param name="data">dictionary sending data</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> SendAsync(string tag, string clientId, Dictionary<string, object> data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return await SendAsync(tag, clientId, new EdenData(data), deliveryMethod, log);
        }
        
        #endregion
        #region Broadcast Methods
        
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">sending data </param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
         public bool Broadcast(string tag, EdenData data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            EdenPacket packet = new EdenPacket {tag = tag, data = data};

            string jsonPacket = JsonSerializer.Serialize(packet, _options);
            NetDataWriter writer = new NetDataWriter();
            writer.Put(jsonPacket);

            // Begin sending packet
            _netManager.SendToAll(writer, deliveryMethod);

            if (log)
                _logger?.Log($"Broadcast({writer.Data.Length,5}B) : [TAG] {tag} " + $"[DATA] {JsonSerializer.Serialize(data.data)}");
            return true;
        }
        
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public bool Broadcast(string tag, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return Broadcast(tag, new EdenData(), deliveryMethod, log);
        }
        
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object sending data</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public bool Broadcast(string tag, object? data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return Broadcast(tag, new EdenData(data), deliveryMethod, log);
        }

        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data</param>
        /// <param name="deliveryMethod">Packet delivery method</param>
        /// <param name="log">Log send packet</param>
        public bool Broadcast(string tag, object[] data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return Broadcast(tag, new EdenData(data), deliveryMethod, log);
        }

        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">dictionary sending data</param>
        /// <param name="deliveryMethod">Packet delivery method</param>
        /// <param name="log">Log send packet</param>
        public bool Broadcast(string tag, Dictionary<string, object> data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return Broadcast(tag, new EdenData(data), deliveryMethod, log);
        }

        /// <summary>
        /// Broadcast data asynchronously to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">sending data</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> BroadcastAsync(string tag, EdenData data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return await Task.Run(() => Broadcast(tag, data, deliveryMethod, log));
        }
        /// <summary>
        /// Broadcast data asynchronously to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> BroadcastAsync(string tag, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return await BroadcastAsync(tag, new EdenData(), deliveryMethod, log);
        }
        /// <summary>
        /// Broadcast data asynchronously to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object sending data</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> BroadcastAsync(string tag, object? data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return await BroadcastAsync(tag, new EdenData(data), deliveryMethod, log);
        }
        /// <summary>
        /// Broadcast data asynchronously to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> BroadcastAsync(string tag, object[] data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return await BroadcastAsync(tag, new EdenData(data), deliveryMethod, log);
        }
        /// <summary>
        /// Broadcast data asynchronously to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">dictionary sending data</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> BroadcastAsync(string tag, Dictionary<string, object> data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return await BroadcastAsync(tag, new EdenData(data), deliveryMethod, log);
        }

        #endregion
        #region BroadcastExcept Methods

        /// <summary>
        /// Broadcast data to all connected client except specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client exclude</param>
        /// <param name="data">sending data </param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public bool BroadcastExcept(string tag, string clientId, EdenData data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            if (!_clients.TryGetValue(clientId, out var client))
                return false;
            EdenPacket packet = new EdenPacket {tag = tag, data = data};

            string jsonPacket = JsonSerializer.Serialize(packet, _options);
            NetDataWriter writer = new NetDataWriter();
            writer.Put(jsonPacket);

            // Begin sending packet
            _netManager.SendToAll(writer, deliveryMethod, client);

            if (log)
                _logger?.Log($"BroadcastExcept({clientId}/{writer.Data.Length,5}B) : [TAG] {tag} " + $"[DATA] {JsonSerializer.Serialize(data.data)}");
            return true;
        }
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client exclude</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public bool BroadcastExcept(string tag, string clientId, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return BroadcastExcept(tag, clientId, new EdenData(), deliveryMethod, log);
        }
        
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client exclude</param>
        /// <param name="data">sending data </param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public bool BroadcastExcept(string tag, string clientId, object? data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return BroadcastExcept(tag, clientId, new EdenData(data), deliveryMethod, log);
        }
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client exclude</param>
        /// <param name="data">object array sending data</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public bool BroadcastExcept(string tag, string clientId, object[] data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered,  bool log = true)
        {
            return BroadcastExcept(tag, clientId, new EdenData(data), deliveryMethod, log);
        }
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client exclude</param>
        /// <param name="data">dictionary sending data</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public bool BroadcastExcept(string tag, string clientId, Dictionary<string, object> data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return BroadcastExcept(tag, clientId, new EdenData(data), deliveryMethod, log);
        }
        
        
        /// <summary>
        /// Broadcast data to all connected client except specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for except  when broadcast</param>
        /// <param name="data">EdenData structured sending data</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> BroadcastExceptAsync(string tag, string clientId, EdenData data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return await Task.Run(() => BroadcastExcept(tag, clientId, data, deliveryMethod, log));
        }
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for except  when broadcast</param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> BroadcastExceptAsync(string tag, string clientId,DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return await BroadcastExceptAsync(tag, clientId, new EdenData(),deliveryMethod, log);
        }
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for except  when broadcast</param>
        /// <param name="data">object sending data</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> BroadcastExceptAsync(string tag, string clientId, object? data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return await BroadcastExceptAsync(tag, clientId, new EdenData(data), deliveryMethod, log);
        }
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for except  when broadcast</param>
        /// <param name="data">object array sending data</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> BroadcastExceptAsync(string tag, string clientId, object[] data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return await BroadcastExceptAsync(tag, clientId, new EdenData(data), deliveryMethod, log);
        }
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for except  when broadcast</param>
        /// <param name="data">dictionary sending data</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> BroadcastExceptAsync(string tag, string clientId, Dictionary<string, object> data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered,  bool log = true)
        {
            return await BroadcastExceptAsync(tag, clientId, new EdenData(data), deliveryMethod, log);
        }
        
        #endregion
        #endregion
        #region Network Logic Methods
        /// <summary>
        /// Async method for receive udp packet
        /// </summary>
        private void NetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            string jsonPacket = reader.GetString();
            int packetLength = Encoding.UTF8.GetByteCount(jsonPacket);
            string clientId = peer.EndPoint.ToString();

            //Read Json Packet
            try
            {
                var packet = JsonSerializer.Deserialize<EdenPacket>(jsonPacket, _options);
                packet.data.CastJsonToType();

                //Process request
                if (packet.tag.StartsWith(REQUEST_PREFIX))
                {
                    if (_responseEvents.TryGetValue(packet.tag, out var responser))
                    {
                        if (responser.log)
                            _logger?.Log($"Rqst({peer.EndPoint}/{packetLength,5}B) : [TAG] {packet.tag.Replace(REQUEST_PREFIX, "")} [DATA] {packet.data.data}");
                        try
                        {
                            var responseData = responser.responseEvent(clientId, packet.data);
                            if (!Response(packet.tag, clientId, responseData, deliveryMethod, responser.log))
                            {
                                _logger?.Log($"Error! Response Fail in ResponseEvent : {packet.tag} | {clientId}");
                            }
                        }
                        catch (Exception e) // Exception for every problem in PacketListenEvent
                        {
                            _logger?.Log($"Error! Error caught in ResponseEvent : {packet.tag} | {clientId} \n {e.Message}");
                        }
                    }
                    else // Exception for packet tag not registered
                    {
                        if (!_ignoreUnknownPacket)
                            _logger?.Log($"Error! There is no packet tag {packet.tag} from {clientId}");
                    }
                }
                //Process receive event
                else
                {
                    if (_receiveEvents.TryGetValue(packet.tag, out var receiver))
                    {
                        try
                        {
                            if (receiver.log)
                                _logger?.Log($"Recv({clientId}/{packetLength,5}B) : [TAG] {packet.tag} [DATA] {packet.data.data}");
                            receiver.receiveEvent(clientId, packet.data);
                        }
                        catch (Exception e) // Exception for every problem in PacketListenEvent
                        {
                            _logger?.Log($"Error! Error caught in ReceiveEvent : {packet.tag} | {clientId} \n {e.Message}");
                        }
                    }
                    else // Exception for packet tag not registered
                    {
                        if (!_ignoreUnknownPacket)
                            _logger?.Log($"Error! There is no packet tag {packet.tag} from {clientId}");
                    }
                }
            }
            catch (Exception e) // Exception for not formed packet data
            {
                _logger?.Log($"Error! Packet data is not JSON-formed on {clientId}\n{e.Message}");
                _logger?.Log($"Not formatted message \n {jsonPacket}");
            }
        }

        /// <summary>
        /// Response data from request
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for send</param>
        /// <param name="data">EdenData structured sending data </param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        private bool Response(string tag, string clientId, EdenData data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            if (!_clients.TryGetValue(clientId, out var peer))
            {
                _logger?.Log($"Error! Send - client tag is wrong");
                return false;
            }
            
            EdenPacket packet = new EdenPacket {tag = tag, data = data};

            string jsonPacket = JsonSerializer.Serialize(packet, _options);
            NetDataWriter writer = new NetDataWriter();
            writer.Put(jsonPacket);

            peer.Send(writer, deliveryMethod);

            if (log)
                _logger?.Log($"Resp({clientId}/{Encoding.UTF8.GetByteCount(jsonPacket),5}B) : [TAG] {tag} " + $"[DATA] {JsonSerializer.Serialize(data.data, _options)}");
            return true;
        }  
        #endregion
    }


}
