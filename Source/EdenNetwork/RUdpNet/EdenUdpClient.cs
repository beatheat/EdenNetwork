using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using static EdenNetwork.Constant;
using LiteNetLib;
using LiteNetLib.Utils;

namespace EdenNetwork.Udp
{
    public class EdenUdpClient
    {
        #region Pre-defined Class&Struct

        /// <summary>
        /// Receive packet
        /// </summary>
        private struct Receiver
        {
            public bool log;
            public Action<EdenData> receiveEvent;
        }

        /// <summary>
        /// Response packet
        /// </summary>
        private class Responser
        {
            public bool log;
            public EdenData? data;
        }

        #endregion
        #region Fields

        private const int DEFAULT_TIMEOUT = 10;
        private readonly NetManager _netManager;
        private readonly EventBasedNetListener _listener;
        private NetPeer? _peer;
        
        private Action? _disconnectEvent;

        private readonly string _serverId;
        
        private readonly Dictionary<string, Receiver> _receiveEvents;
        private readonly Dictionary<string, Responser> _responseEvents;

        private readonly Logger? _logger;

        private readonly string _address;
        private readonly int _port;
        private JsonSerializerOptions _options;

        private bool _ignoreUnknownPacket;

        #endregion
        #region Constructor
        /// <summary>
        /// Constructor for EdenNetClient
        /// </summary>
        public EdenUdpClient(string address, int port, string logPath = "", bool printConsole = true, int flushInterval = 3 * 60 * 1000)
        {
            _receiveEvents = new Dictionary<string, Receiver>();
            _responseEvents = new Dictionary<string, Responser>();
            _disconnectEvent = null;
            _serverId = address + ":" + port;

            _peer = null;
            _listener = new EventBasedNetListener();
            _netManager = new NetManager(_listener) {AutoRecycle = true, UnsyncedEvents = true};

            _ignoreUnknownPacket = false;

            this._address = address;
            this._port = port;

            _options = new JsonSerializerOptions {IncludeFields = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull};

            _logger = null;
            if (logPath != "")
                _logger = new Logger(logPath, printConsole, flushInterval);
        }
        #endregion
        
        #region General Methods
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
        /// Do not log unknown receive packets
        /// </summary>
        /// <param name="ignore">ignore or not</param>
        public void SetIgnoreUnknownPacketTag(bool ignore)
        {
            _ignoreUnknownPacket = ignore;
        }

        /// <summary>
        /// Get LocalEndPoint port number
        /// </summary>
        /// <returns>Get port number of local end point, return -1 if client is not connected</returns>
        public int GetLocalEndPort()
        {
            if (_peer == null) return -1;
            return ((IPEndPoint) _peer.EndPoint).Port;
        }

        /// <summary>
        /// Append event activates when connection close
        /// </summary>
        /// <param name="callback">
        ///     callback run after client disconnected <br/>
        /// </param>
        public void SetDisconnectEvent(Action callback)
        {
            _disconnectEvent = callback;
        }

        /// <summary>
        /// set null to the event activates when client connection close
        /// </summary>
        public void ReSetDisconnectEvent()
        {
            _disconnectEvent = null;
        }
        
        /// <summary>
        /// Append receive event which response for packet named with specific tag 
        /// </summary>
        /// <param name="tag">responsible tag name for packet received</param>
        /// <param name="receiveEvent">
        ///     event that processed by packet received <br/>
        ///     arg1 = EdenData data
        /// </param>
        /// <param name="log">Log send packet</param>
        public void AddReceiveEvent(string tag, Action<EdenData> receiveEvent, bool log = true)
        {
            if (_receiveEvents.ContainsKey(tag))
            {
                _logger?.Log($"Error! AddReceiveEvent - receive event tag({tag}) already exists");
                return;
            }
            _receiveEvents.Add(tag, new Receiver{receiveEvent = receiveEvent, log = log});
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
        /// Close server and release 
        /// </summary>
        public void Close()
        {
            _peer = null;
            _netManager.Stop();
            _logger?.Log($"Stream {_serverId} is closed");
            _logger?.Close();
        }
        #endregion
        #region Network Transmission Methods
        #region Connect Methods
        /// <summary>
        /// Connect to server
        /// </summary>
        /// <param name="key"></param>
        /// <param name="timeout"></param>
        /// <returns>returns ConnectionState of server[OK, FULL, FAIL, ERROR]</returns>
        public ConnectionState Connect(int timeout = DEFAULT_TIMEOUT, string key = DEFAULT_KEY)
        {
            bool connectResponded = false;
            ConnectionState connectionState = ConnectionState.ERROR;
            _listener.PeerDisconnectedEvent += (netPeer, info) =>
            {
                connectResponded = true;
                if (info.Reason == LiteNetLib.DisconnectReason.ConnectionFailed)
                {
                    connectionState = ConnectionState.FAIL;
                    _logger?.Log($"Cannot connect to server : Connection failed");
                }
                if (info.Reason == LiteNetLib.DisconnectReason.RemoteConnectionClose)
                {
                    connectionState = (ConnectionState) info.AdditionalData.GetByte();
                    var stateName = Enum.GetName(typeof(ConnectionState), connectionState);
                    _logger?.Log($"Cannot connect to server : SERVER IS {stateName}");
                }
                else
                {
                    _logger?.Log(info.Reason.ToString());   
                }
            };
            _listener.PeerConnectedEvent += peer =>
            {
                connectResponded = true;
                connectionState = ConnectionState.OK;
                _peer = peer;
                _logger?.Log($"Connection success to {_address}:{_port}");
            };
            _listener.NetworkReceiveEvent += NetworkReceive;

            _netManager.MaxConnectAttempts = timeout*1000 / _netManager.ReconnectDelay;
            _netManager.Start();
            _netManager.Connect(_address, _port, key);
            while (!connectResponded)
            {
                Thread.Sleep(15);
            }

            return connectionState;
        }
        
        /// <summary>
        /// Connect to server asynchronously
        /// </summary>
        /// <param name="key"></param>
        /// <param name="timeout"></param>
        /// <returns>returns ConnectionState of server[OK, FULL, FAIL, ERROR]</returns>
        public async Task<ConnectionState> ConnectAsync(int timeout = DEFAULT_TIMEOUT, string key = DEFAULT_KEY)
        {
            return await Task.Run(async () =>
            {
                bool connectResponded = false;
                ConnectionState connectionState = ConnectionState.ERROR;
                _listener.PeerDisconnectedEvent += (netPeer, info) =>
                {
                    connectResponded = true;
                    if (info.Reason == LiteNetLib.DisconnectReason.ConnectionFailed)
                    {
                        connectionState = ConnectionState.FAIL;
                        _logger?.Log($"Cannot connect to server : Connection failed");
                    }

                    if (info.Reason == LiteNetLib.DisconnectReason.RemoteConnectionClose)
                    {
                        connectionState = (ConnectionState) info.AdditionalData.GetByte();
                        var stateName = Enum.GetName(typeof(ConnectionState), connectionState);
                        _logger?.Log($"Cannot connect to server : SERVER IS {stateName}");
                    }
                    else
                    {
                        _logger?.Log(info.Reason.ToString());
                    }
                };
                _listener.PeerConnectedEvent += peer =>
                {
                    connectResponded = true;
                    connectionState = ConnectionState.OK;
                    _peer = peer;
                    _logger?.Log($"Connection success to {_address}:{_port}");
                };
                _listener.NetworkReceiveEvent += NetworkReceive;
                
                _netManager.MaxConnectAttempts = timeout * 1000 / _netManager.ReconnectDelay;
                _netManager.Start();
                _netManager.Connect(_address, _port, key);
                
                while (!connectResponded)
                {
                    await Task.Delay(15);
                }

                return connectionState;
            });
        }
        #endregion
        #region Send Methods
        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">EdenData structured sending data </param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public bool Send(string tag, EdenData data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            if (_peer != null)
            {
                EdenPacket packet = new EdenPacket {tag = tag, data = data};
                string jsonPacket = JsonSerializer.Serialize(packet, _options);
                NetDataWriter writer = new NetDataWriter();
                writer.Put(jsonPacket);
                _peer.Send(writer, deliveryMethod);
                
                if(log)
                    _logger?.Log($"Send({_serverId}/{writer.Data.Length,5}B) : [TAG] {tag} " + $"[DATA] {JsonSerializer.Serialize(data.data, _options)}");
                return true;
            }
            else
            {
                _logger?.Log($"Error! Send - Not connected to server");
                return false;
            }
        }
        
        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public bool Send(string tag, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return Send(tag, new EdenData(), deliveryMethod, log);
        }

        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object sending data </param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public bool Send(string tag, object? data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return Send(tag, new EdenData(data), deliveryMethod, log);
        }

        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data </param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public bool Send(string tag, object[] data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return Send(tag, new EdenData(data),deliveryMethod, log);
        }
        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">dictionary sending data </param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public bool Send(string tag, Dictionary<string, object> data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return Send(tag, new EdenData(data),deliveryMethod, log);
        }
        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">EdenData structured sending data </param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> SendAsync(string tag, EdenData data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return await Task.Run(() => Send(tag, data, deliveryMethod, log));
        }
        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> SendAsync(string tag, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return await SendAsync(tag, new EdenData(), deliveryMethod, log);
        }
        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data </param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> SendAsync(string tag, object? data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return await SendAsync(tag, new EdenData(data), deliveryMethod, log);
        }
        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data </param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> SendAsync(string tag, object[] data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return await SendAsync(tag, new EdenData(data), deliveryMethod, log);
        }
        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">dictionary sending data </param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> SendAsync(string tag, Dictionary<string, object> data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, bool log = true)
        {
            return await SendAsync(tag, new EdenData(data), deliveryMethod, log);
        }

        #endregion
        #region Request Methods
        /// <summary>
        /// Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">EdenData structured sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public EdenData Request(string tag, EdenData data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered,  int timeout = DEFAULT_TIMEOUT, bool log = true)
        {
            if (_peer == null) 
                return EdenData.Error("Error! Request - Client is not connected");
            
            tag = REQUEST_PREFIX + tag;
            if (!_responseEvents.ContainsKey(tag))
                _responseEvents.Add(tag, new Responser {data = null, log = log});
            
            //Request
            EdenPacket packet = new EdenPacket {tag = tag, data = data};
            string jsonPacket = JsonSerializer.Serialize(packet, _options);
            NetDataWriter writer = new NetDataWriter();
            writer.Put(jsonPacket);
            
            _peer.Send(writer, deliveryMethod);
                
            if(log)
                _logger?.Log($"Rqst({_serverId}/{writer.Data.Length,5}B) : [TAG] {tag} " + $"[DATA] {JsonSerializer.Serialize(data.data, _options)}");
            
            //Response
            double time = 0;
            Responser responser;
            do
            {
                Thread.Sleep(20);
                time += 0.05;

                if (_responseEvents.TryGetValue(tag, out responser!) && responser.data != null)
                    break;

            } while (timeout > time);
            _responseEvents.Remove(tag);
            if (timeout <= time) 
                return EdenData.Error("Error! Request - Request timeout");
            return responser.data!.Value;
        }
        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public EdenData Request(string tag, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered,  int timeout = DEFAULT_TIMEOUT, bool log = true)
        {
            return Request(tag, new EdenData(), deliveryMethod, timeout, log);
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public EdenData Request(string tag, object? data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered,  int timeout = DEFAULT_TIMEOUT, bool log = true)
        {
            return Request(tag, new EdenData(data), deliveryMethod, timeout, log);
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public EdenData Request(string tag, object[] data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered,  int timeout = DEFAULT_TIMEOUT, bool log = true)
        {
            return Request(tag, new EdenData(data), deliveryMethod, timeout, log);
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">Dictionary sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public EdenData Request(string tag, Dictionary<string, object> data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered,  int timeout = DEFAULT_TIMEOUT, bool log = true)
        {
            return Request(tag, new EdenData(data), deliveryMethod, timeout, log);
        }
        
        /// <summary>
        /// Request any data to server asynchronously and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">EdenData structured sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<EdenData> RequestAsync(string tag, EdenData data,DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered,  int timeout = DEFAULT_TIMEOUT, bool log = true)
        {
            return await Task.Run(async () =>
            {
                if (_peer == null) 
                    return EdenData.Error("Error! Request - Client is not connected");
            
                tag = REQUEST_PREFIX + tag;
                if (!_responseEvents.ContainsKey(tag))
                    _responseEvents.Add(tag, new Responser {data = null, log = log});
            
                //Request
                EdenPacket packet = new EdenPacket {tag = tag, data = data};
                string jsonPacket = JsonSerializer.Serialize(packet, _options);
                NetDataWriter writer = new NetDataWriter();
                writer.Put(jsonPacket);
            
                _peer.Send(writer, deliveryMethod);
                
                if(log)
                    _logger?.Log($"Rqst({_serverId}/{writer.Data.Length,5}B) : [TAG] {tag} " + $"[DATA] {JsonSerializer.Serialize(data.data, _options)}");

                double time = 0;
                Responser responser;
                do
                {
                    await Task.Delay(20);
                    time += 0.05;

                    if (_responseEvents.TryGetValue(tag, out responser!) && responser.data != null)
                        break;

                } while (timeout > time);
                _responseEvents.Remove(tag);
                if (timeout <= time) return EdenData.Error("Error! RequestAsync - Request timeout");
                else return responser.data!.Value;
            });
        }
        
        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<EdenData> RequestAsync(string tag, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, int timeout = DEFAULT_TIMEOUT, bool log = true)
        {
            return await RequestAsync(tag, new EdenData(), deliveryMethod,timeout, log);
        }

        
        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<EdenData> RequestAsync(string tag, object? data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, int timeout = DEFAULT_TIMEOUT, bool log = true)
        {
            return await RequestAsync(tag, new EdenData(data), deliveryMethod,timeout, log);
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<EdenData> RequestAsync(string tag, object[] data, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, int timeout = DEFAULT_TIMEOUT, bool log = true)
        {
            return await RequestAsync(tag, new EdenData(data), deliveryMethod,timeout, log);
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">Dictionary sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="deliveryMethod">Packet delivery method </param>
        /// <param name="log">Log send packet</param>
        public async Task<EdenData> RequestAsync(string tag,Dictionary<string, object> data,DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered, int timeout = DEFAULT_TIMEOUT, bool log = true)
        {
            return await RequestAsync(tag, new EdenData(data), deliveryMethod,timeout, log);
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

            //Read Json Packet
            try
            {
                var edenPacket = JsonSerializer.Deserialize<EdenPacket>(jsonPacket, _options);
                    
                edenPacket.data.CastJsonToType();
                //Process response
                if (edenPacket.tag.StartsWith(REQUEST_PREFIX))
                {
                    if (_responseEvents.ContainsKey(edenPacket.tag))
                    {
                        if(_responseEvents[edenPacket.tag].log)
                            _logger?.Log($"Resp({_serverId}/{packetLength,5}B) : [TAG] {edenPacket.tag} [DATA] {edenPacket.data.data}");
                        _responseEvents[edenPacket.tag].data = edenPacket.data;
                    }
                    else
                    {
                        if(!_ignoreUnknownPacket)
                            _logger?.Log($"Error! There is no packet tag {edenPacket.tag} from {_serverId}");
                    }
                }
                //Process receive
                else
                {
                    if (_receiveEvents.TryGetValue(edenPacket.tag, out var receiver))
                    {
                        if(receiver.log)
                            _logger?.Log($"Recv({_serverId}/{packetLength,5}B) : [TAG] {edenPacket.tag} [DATA] {edenPacket.data.data}");
                        try { receiver.receiveEvent(edenPacket.data); }
                        catch (Exception e) // Exception for every problem in PacketListenEvent
                        {
                            _logger?.Log($"Error! Error caught in ReceiveEvent : {edenPacket.tag} | {_serverId} \n {e.Message}");
                        }
                    }
                    else
                    {
                        if(!_ignoreUnknownPacket)
                            _logger?.Log($"Error! There is no packet tag {edenPacket.tag} from {_serverId}");
                    }
                }
            }
            catch (Exception e) // Exception for not formed packet data
            {
                _logger?.Log($"Error! Packet data is not JSON-formed on {_serverId}\n{e.Message}");
                _logger?.Log($"Not formatted message \n {jsonPacket}");
            }
        }
        #endregion
    }
}