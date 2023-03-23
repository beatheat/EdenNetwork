using System.Text;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using static EdenNetwork.Constant;

namespace EdenNetwork
{
    public class EdenNetServer
    {
        #region Pre-defined Class&Struct
        /// <summary>
        /// class : client info
        /// </summary>
        private class EdenClient
        {
            public EdenClient(TcpClient tcpClient, string id, int bufferSize)
            {
                this.tcpClient = tcpClient;
                this.stream = tcpClient.GetStream();
                this.id = id;
                this.readBuffer = new byte[bufferSize];
                this.startReadObject = false;
                this.dataObjectBuffer = Array.Empty<byte>();
                this.packetLengthBuffer = new byte[PACKET_LENGTH_BUFFER_SIZE];
                this.dataObjectBufferPointer = 0;
                this.packetLengthBufferPointer = 0;
            }
            
            public readonly TcpClient tcpClient;
            public readonly NetworkStream stream;
            public readonly string id;
            public readonly byte[] readBuffer;
            
            public readonly byte[] packetLengthBuffer;
            public int packetLengthBufferPointer;
            
            public byte[] dataObjectBuffer;
            public int dataObjectBufferPointer;
            
            public bool startReadObject;
        };
        #endregion

        #region Fields

        /// <summary>
        /// Constant : constant for infinite listen number of client
        /// </summary>
        public const int INF_CLIENT_ACCEPT = Int32.MaxValue;
        
        private readonly TcpListener _server;
        private readonly Dictionary<string, EdenClient> _clients;
        private readonly Dictionary<string, Action<string, EdenData>> _receiveEvents;
        private readonly Dictionary<string, Func<string, EdenData, EdenData>> _responseEvents;
        private bool _isListening;
        private Action<string>? _acceptEvent;
        private Action<string>? _disconnectEvent;
        private int _maxAcceptNum;

        private readonly Logger? _logger;

        private JsonSerializerOptions _options;

        private int _bufferSize;

        public int ClientNum => _clients.Count;

        #endregion

        #region Public Methods
        #region Constructor
        /// <summary>
        /// Constructor for EdenNetSerer
        /// </summary>
        /// <param name="ipAddress">IPv4 address to open server</param>
        /// <param name="port">Port number to open server</param>
        /// <param name="logPath">Path for log data to write</param>
        /// <exception cref="Exception">
        /// Two case of Exception exists
        /// case 1. Cannot create tcp listener 
        /// case 2. Cannot open stream for log file path
        /// </exception>
        public EdenNetServer(string ipAddress, int port, string logPath, bool printConsole = true, int flushInterval = Logger.DEFAULT_FLUSH_INTERVAL)
        {
            try
            {
                _server = new TcpListener(IPAddress.Parse(ipAddress), port);
            }
            catch (Exception e)
            {
                throw new Exception("Cannot create TcpListener on " + ipAddress + ":" + port, e);
            }
            _clients = new Dictionary<string, EdenClient>();
            _receiveEvents = new Dictionary<string, Action<string, EdenData>>();
            _responseEvents = new Dictionary<string, Func<string, EdenData, EdenData>>();
            _isListening = false;
            _acceptEvent = null;
            _disconnectEvent = null;
            _logger = null;
            _bufferSize = DEFAULT_BUFFER_SIZE;
            _options = new JsonSerializerOptions {IncludeFields = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull};
            if (logPath != "")
                _logger = new Logger(logPath, "EdenNetServer", printConsole, flushInterval);
        }
        /// <summary>
        /// Constructor for EdenNetSerer
        /// </summary>
        /// <param name="ipAddress">IPv4 address to open server</param>
        /// <param name="port">Port number to open server</param>
        ///         /// <exception cref="Exception">
        /// Two case of Exception exists
        /// case 1. Cannot create tcplistener 
        /// case 2. Cannot open stream for log file path
        /// </exception>
        public EdenNetServer(string ipAddress, int port) : this(ipAddress, port, "") { }
        /// <summary>
        /// Constructor for EdenNetSerer
        /// </summary>
        /// <param name="port">Port number to open server</param>
        /// <param name="logPath">Path for log data to write</param>
        ///         /// <exception cref="Exception">
        /// Two case of Exception exists
        /// case 1. Cannot create tcp listener 
        /// case 2. Cannot open stream for log file path
        /// </exception>
        public EdenNetServer(int port, string logPath,  bool printConsole = true, int flushInterval = Logger.DEFAULT_FLUSH_INTERVAL) : this("0.0.0.0", port, logPath, printConsole, flushInterval) { }
        /// <summary>
        /// Constructor for EdenNetSerer
        /// </summary>
        /// <param name="port">Port number to open server</param>
        /// <exception cref="Exception">
        /// Two case of Exception exists
        /// case 1. Cannot create tcp listener 
        /// case 2. Cannot open stream for log file path
        /// </exception>
        public EdenNetServer(int port) : this("0.0.0.0", port, "") { }
        #endregion

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
        /// Set data read buffer size in bytes, default value is 8192 Bytes
        /// </summary>
        /// <param name="size">buffer size in bytes</param>
        public void SetBufferSize(int size)
        {
            _bufferSize = DEFAULT_BUFFER_SIZE;
        }

        /// <summary>
        /// Check specific TCP port is available
        /// </summary>
        /// <param name="port"></param>
        public static bool IsPortAvailable(int port)
        {
            bool isAvailable = true;

            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpConnInfoArray = ipGlobalProperties.GetActiveTcpConnections();

            foreach (var tcpInfo in tcpConnInfoArray)
            {
                if (tcpInfo.LocalEndPoint.Port==port)
                {
                    isAvailable = false;
                    break;
                }
            }

            return isAvailable;
        }
        
        /// <summary>
        /// Listen and wait for client connection
        /// </summary>
        /// <param name="maxAcceptNum">max number of clients to accept</param>
        /// <param name="callback">  
        ///     callback for run after client accepted <br/>
        ///     arg1 = string client id
        /// </param>
        public void Listen(int maxAcceptNum, Action<string> callback)
        {
            _server.Start();
            this._maxAcceptNum = maxAcceptNum;
            _isListening = true;
            var ipInfo = (IPEndPoint)_server.LocalEndpoint;
            _acceptEvent = callback;
            _server.BeginAcceptTcpClient(Listening, null);
            
            _logger?.Log("Eden Server is listening on  " + ipInfo);
        }
        /// <summary>
        /// Listen and wait for client connection
        /// </summary>
        /// <param name="callback">  
        ///     callback for run after client accepted <br/>
        ///     arg1 = string client_id
        /// </param>
        public void Listen(Action<string> callback)
        {
            Listen(INF_CLIENT_ACCEPT, callback);
        }
        /// <summary>
        /// Listen and wait for client connection
        /// </summary>
        /// <param name="maxAcceptNum">max number of clients to accept</param>
        public void Listen(int maxAcceptNum)
        {
            Listen(maxAcceptNum, _ => { });
        }
        /// <summary>
        /// Listen and wait for client connection
        /// </summary>
        public void Listen()
        {
            Listen(INF_CLIENT_ACCEPT, _ => { });
        }

        /// <summary>
        /// Stop Listening and waiting for client connection 
        /// </summary>
        public void StopListen()
        {
            _isListening = false;
        }

        /// <summary>
        /// Force disconnect client connection from server
        /// </summary>
        /// <param name="clientId">client id</param>
        public void DisconnectClient(string clientId)
        {
            _disconnectEvent?.Invoke(clientId);
            if (_clients.ContainsKey(clientId))
            {
                _clients[clientId].stream.Close();
                _clients[clientId].tcpClient.Close();
            }
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
        public void AddResponse(string tag, Func<string, EdenData, EdenData> response)
        {
            if (_responseEvents.ContainsKey(REQUEST_PREFIX + tag))
            {
                _logger?.Log($"Error! AddResponse - receive event tag({tag}) already exists");
                return;
            }
            _responseEvents.Add(REQUEST_PREFIX + tag, response);
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
        /// Append receive event which response for packet named with specific tag 
        /// </summary>
        /// <param name="tag">responsible tag name for packet received</param>
        /// <param name="receiveEvent">
        ///     event that processed by packet received <br/>
        ///     arg1 = string client_id <br/>
        ///     arg2 = EdenData data
        /// </param>
        public void AddReceiveEvent(string tag, Action<string, EdenData> receiveEvent)
        {
            if(_receiveEvents.ContainsKey(tag))
            {
                _logger?.Log($"Error! AddReceiveEvent - receive event tag({tag}) already exists");
                return;
            }
            _receiveEvents.Add(tag, receiveEvent);
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
        /// Append event activates when client connection close
        /// </summary>
        /// <param name="callback">
        ///     callback for run after client disconnected <br/>
        ///     arg1 = string clientId
        /// </param>
        public void SetClientDisconnectEvent(Action<string> callback)
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
        /// Check whether client is connected
        /// </summary>
        public bool StillConnected(string clientId)
        {
            return _clients.ContainsKey(clientId);
        }

        /// <summary>
        /// Close server and release 
        /// </summary>
        public void Close()
        {
            StopListen();
            foreach (var client in _clients.Values)
            {
                client.stream.Close();
                client.tcpClient.Close();
            }
            _isListening = false;
            _server.Stop();
            _logger?.Log("EdenNetServer is closed");
            _logger?.Close();
        }

        #region Send Methods
        /// <summary>
        /// Send data json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for send</param>
        /// <param name="data">EdenData structured sending data </param>
        public bool Send(string tag, string clientId, EdenData data)
        {
            if (!_clients.ContainsKey(clientId)) return false;
            NetworkStream stream = _clients[clientId].stream;
            if (stream.CanWrite)
            {
                EdenPacket packet = new EdenPacket {tag = tag, data = data};

                string jsonPacket = JsonSerializer.Serialize(packet, _options);
                byte[] bytes = Encoding.UTF8.GetBytes(jsonPacket);
                byte[] sendObj = BitConverter.GetBytes(bytes.Length);
                sendObj = sendObj.Concat(bytes).ToArray();
                // Begin sending packet
                stream.Write(sendObj, 0, sendObj.Length);
                _logger?.Log($"Send({clientId}/{bytes.Length,4}B) : [TAG] {tag} " +
                    $"[DATA] {JsonSerializer.Serialize(data.data, _options)}");
                return true;
            }
            else // Exception for network stream write is not ready
            {
                _logger?.Log($"Error! Send - NetworkStream cannot write on client_id : {_clients[clientId].id}");
                return false;
            }
        }
        /// <summary>
        /// Send data json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for send</param>
        /// <param name="data">object array sending data </param>
        public bool Send(string tag, string clientId, params object[]? data)
        {
            if (data == null || data.Length == 0)
            {
                return Send(tag, clientId, new EdenData());
            }
            else if (data.Length == 1)
            {
                return Send(tag, clientId, new EdenData(data[0]));
            }
            else
                return Send(tag, clientId, new EdenData(data));
        }
        /// <summary>
        /// Send data json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for send</param>
        /// <param name="data">dictionary sending data </param>
        public bool Send(string tag, string clientId, Dictionary<string, object>? data)
        {
            if (data == null)
                return Send(tag, clientId, new EdenData());
            else
                return Send(tag, clientId, new EdenData(data));
        }

        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">Eden data structured sending data</param>
        public bool Broadcast(string tag, EdenData data)
        {
            foreach (var client in _clients.Values)
            {
                if (Send(tag, client.id, data) == false)
                    return false;
            }
            return true;
        }
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data</param>
        public bool Broadcast(string tag, params object[]? data)
        {
            if (data == null! || data.Length == 0)
            {
                return Broadcast(tag, new EdenData());
            }
            else if (data.Length == 1)
            {
                return Broadcast(tag, new EdenData(data[0]));
            }
            else
                return Broadcast(tag, new EdenData(data));
        }
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">dictionary sending data</param>
        public bool Broadcast(string tag, Dictionary<string, object>? data)
        {
            if (data == null)
                return Broadcast(tag, new EdenData());
            else
                return Broadcast(tag, new EdenData(data));
        }

        /// <summary>
        /// Broadcast data to all connected client except specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">EdenData structured sending data</param>
        public bool BroadcastExcept(string tag, string clientId, EdenData data)
        {
            foreach (var client in _clients.Values)
            {
                if (clientId == client.id)
                    continue;
                if (Send(tag, client.id, data) == false)
                    return false;
            }
            return true;
        }
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data</param>
        public bool BroadcastExcept(string tag, string clientId, params object[]? data)
        {
            if (data == null || data.Length == 0)
            {
                return BroadcastExcept(tag, clientId, new EdenData());
            }
            else if (data.Length == 1)
            {
                return BroadcastExcept(tag, clientId, new EdenData(data[0]));
            }
            else
                return BroadcastExcept(tag, clientId, new EdenData(data));
        }
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">dictionary sending data</param>
        public bool BroadcastExcept(string tag, string clientId, Dictionary<string, object>? data)
        {
            if (data == null)
                return BroadcastExcept(tag, clientId, new EdenData());
            else
                return BroadcastExcept(tag, clientId, new EdenData(data));
        }
        #endregion

        #region AsyncSend Methods
        /// <summary>
        /// Send data asynchronously json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for send</param>
        /// <param name="callback">callback function[bool success, object state] run after data send</param>
        /// <param name="data">EdenData structured sending data</param>
        public void BeginSend(string tag, string clientId, Action<bool> callback, EdenData data)
        {
            Task.Run(() => callback(Send(tag, clientId, data)));
        }
        /// <summary>
        /// Send data asynchronously json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for send</param>
        /// <param name="callback">callback function[bool success, object state] run after data send</param>
        /// <param name="data">object array sending data</param>
        public void BeginSend(string tag, string clientId, Action<bool> callback, params object[]? data)
        {
            if (data == null || data.Length == 0)
            {
                BeginSend(tag, clientId, callback, new EdenData());
            }
            else if (data.Length == 1)
            {
                BeginSend(tag, clientId, callback, new EdenData(data[0]));
            }
            else
                BeginSend(tag, clientId, callback, new EdenData(data));
        }
        /// <summary>
        /// Send data asynchronously json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for send</param>
        /// <param name="callback">callback function[bool success, object state] run after data send</param>
        /// <param name="data">dictionary array sending data</param>
        public void BeginSend(string tag, string clientId, Action<bool> callback, Dictionary<string, object>? data)
        {
            if (data == null)
                BeginSend(tag, clientId, callback, new EdenData());
            else
                BeginSend(tag, clientId, callback, new EdenData(data));
        }

        /// <summary>
        /// Send data asynchronously json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for send</param>
        /// <param name="data">sending data</param>
        public async Task<bool> SendAsync(string tag, string clientId, EdenData data)
        {
            return await Task.Run(async () =>
            {
                if (!_clients.ContainsKey(clientId)) return false;
                NetworkStream stream = _clients[clientId].stream;
                if (stream.CanWrite)
                {
                    EdenPacket packet = new EdenPacket {tag = tag, data = data};

                    string jsonPacket = JsonSerializer.Serialize(packet, _options);
                    byte[] bytes = Encoding.UTF8.GetBytes(jsonPacket);
                    byte[] sendObj = BitConverter.GetBytes(bytes.Length);
                    sendObj = sendObj.Concat(bytes).ToArray();
                    // Begin sending packet
                    await stream.WriteAsync(sendObj, 0, sendObj.Length);
                    _logger?.Log($"Send({clientId}/{bytes.Length,4}B) : [TAG] {tag} " +
                        $"[DATA] {JsonSerializer.Serialize(data.data, _options)}");
                    return true;
                }
                else // Exception for network stream write is not ready
                {
                    _logger?.Log($"Error! SendAsync - NetworkStream cannot write on client_id : {_clients[clientId].id}");
                    return false;
                }
            });
        }
        /// <summary>
        /// Send data asynchronously json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for send</param>
        /// <param name="data">object array sending data</param>
        public async Task<bool> SendAsync(string tag, string clientId, params object[]? data)
        {
            if (data == null || data.Length == 0)
            {
                return await SendAsync(tag, clientId, new EdenData());
            }
            else if (data.Length == 1)
            {
                return await SendAsync(tag, clientId, new EdenData(data[0]));
            }
            else
                return await SendAsync(tag, clientId, new EdenData(data));
        }
        /// <summary>
        /// Send data asynchronously json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for send</param>
        /// <param name="data">dictionary sending data</param>
        public async Task<bool> SendAsync(string tag, string clientId, Dictionary<string, object>? data)
        {
            if (data == null)
                return await SendAsync(tag, clientId, new EdenData());
            else
                return await SendAsync(tag, clientId, new EdenData(data));
        }


        //============================================================================

        /// <summary>
        /// Broadcast data asynchronously to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="callback">callback function[bool success, object state] run after data send</param>
        /// <param name="data">sending data</param>
        public void BeginBroadcast(string tag, Action<bool> callback, EdenData data)
        {
            Task.Run(() => callback(Broadcast(tag, data)));
        }
        /// <summary>
        /// Broadcast data asynchronously to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="callback">callback function[bool success, object state] run after data send</param>
        /// <param name="data">object array sending data</param>
        public void BeginBroadcast(string tag, Action<bool> callback, params object[]? data)
        {
            if (data == null || data.Length == 0)
            {
                BeginBroadcast(tag, callback, new EdenData());
            }
            else if (data.Length == 1)
            {
                BeginBroadcast(tag, callback, new EdenData(data[0]));
            }
            else
                BeginBroadcast(tag, callback, new EdenData(data));
        }
        /// <summary>
        /// Broadcast data asynchronously to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="callback">callback function[bool success, object state] run after data send</param>
        /// <param name="data">dictionary sending data</param>
        public void BeginBroadcast(string tag, Action<bool> callback, Dictionary<string, object>? data)
        {
            if (data == null)
                BeginBroadcast(tag, callback, new EdenData());
            else
                BeginBroadcast(tag, callback, new EdenData(data));
        }
        /// <summary>
        /// Broadcast data asynchronously to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">sending data</param>
        public async Task<bool> BroadcastAsync(string tag, EdenData data)
        {
            return await Task.Run(async () =>
            {
                List<Task<bool>> tasks = new List<Task<bool>>();
                foreach (var client in _clients.Values)
                {
                    var task = Task.Run(async () => await SendAsync(tag, client.id, data));
                    tasks.Add(task);
                }
                bool success = true;
                foreach(var task in tasks)
                {
                    success = success && (await task);
                }
                return success;
            });
        }
        /// <summary>
        /// Broadcast data asynchronously to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data</param>
        public async Task<bool> BroadcastAsync(string tag, params object[]? data)
        {
            if (data == null || data.Length == 0)
            {
                return await BroadcastAsync(tag, new EdenData());
            }
            else if (data.Length == 1)
            {
                return await BroadcastAsync(tag, new EdenData(data[0]));
            }
            else
                return await BroadcastAsync(tag, new EdenData(data));
        }
        /// <summary>
        /// Broadcast data asynchronously to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">dictionary sending data</param>
        public async Task<bool> BroadcastAsync(string tag, Dictionary<string, object>? data)
        {
            if (data == null)
                return await BroadcastAsync(tag, new EdenData());
            else
                return await BroadcastAsync(tag, new EdenData(data));
        }

        /// <summary>
        /// Broadcast data to all connected client except specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for except  when broadcast</param>
        /// <param name="callback">callback function[bool success, object state] run after data send</param>
        /// <param name="data">EdenData structured sending data</param>
        public void BeginBroadcastExcept(string tag, string clientId, Action<bool> callback, EdenData data)
        {
            Task.Run(() => callback(BroadcastExcept(tag, clientId, data)));
        }
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for except  when broadcast</param>
        /// <param name="callback">callback function[bool success, object state] run after data send</param>
        /// <param name="data">object array sending data</param>
        public void BeginBroadcastExcept(string tag, string clientId, Action<bool> callback, params object[]? data)
        {
            if (data == null || data.Length == 0)
            {
                BeginBroadcastExcept(tag, clientId, callback, new EdenData());
            }
            else if (data.Length == 1)
            {
                BeginBroadcastExcept(tag, clientId, callback, new EdenData(data[0]));
            }
            else
                BeginBroadcastExcept(tag, clientId, callback, new EdenData(data));
        }
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for except  when broadcast</param>
        /// <param name="callback">callback function[bool success, object state] run after data send</param>
        /// <param name="data">dictionary sending data</param>
        public void BeginBroadcastExcept(string tag, string clientId, Action<bool> callback, Dictionary<string, object>? data)
        {
            if (data == null)
                BeginBroadcastExcept(tag, clientId, callback, new EdenData());
            else
                BeginBroadcastExcept(tag, clientId, callback, new EdenData(data));
        }
        /// <summary>
        /// Broadcast data to all connected client except specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for except  when broadcast</param>
        /// <param name="data">EdenData structured sending data</param>
        public async Task<bool> BroadcastExceptAsync(string tag, string clientId, EdenData data)
        {
            return await Task.Run(async () =>
            {
                List<Task<bool>> tasks = new List<Task<bool>>();
                foreach (var client in _clients.Values)
                {
                    if (client.id == clientId) continue;
                    var task = Task.Run(async () => await SendAsync(tag, client.id, data));
                    tasks.Add(task);
                }
                bool success = true;
                foreach (var task in tasks)
                {
                    success = success && (await task);
                }
                return success;
            });
        }
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for except  when broadcast</param>
        /// <param name="data">object array sending data</param>
        public async Task<bool> BroadcastExceptAsync(string tag, string clientId, params object[]? data)
        {
            if (data == null || data.Length == 0)
            {
                return await BroadcastExceptAsync(tag, clientId, new EdenData());
            }
            else if (data.Length == 1)
            {
                return await BroadcastExceptAsync(tag, clientId, new EdenData(data[0]));
            }
            else
                return await BroadcastExceptAsync(tag, clientId, new EdenData(data));
        }
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="clientId">client id for except  when broadcast</param>
        /// <param name="data">dictionary sending data</param>
        public async Task<bool> BroadcastExceptAsync(string tag, string clientId, Dictionary<string, object>? data)
        {
            if (data == null)
                return await BroadcastExceptAsync(tag, clientId, new EdenData());
            else
                return await BroadcastExceptAsync(tag, clientId, new EdenData(data));
        }
        #endregion

        #endregion

        #region Private Methods

        /// <summary>
        /// Async method for listening client connection
        /// </summary>
        /// <param name="ar">ar.AsyncState is DoAfterClientAccept Action</param>
        private void Listening(IAsyncResult ar)
        {
            TcpClient client;
            try
            {
                client = _server.EndAcceptTcpClient(ar);
            }
            catch
            {
                //maybe server is closed
                return;
            }

            string log = "";
            //client cannot be null
            IPEndPoint remoteEndPoint = (IPEndPoint) client.Client.RemoteEndPoint!;
            //remoteEndPoint cannot be null
            if (!_isListening || _clients.Count >= _maxAcceptNum)
            {
                if (!_isListening)
                {
                    log = $"{remoteEndPoint} is rejected by SERVER NOT LISTENING STATE";
                    client.GetStream().Write(BitConverter.GetBytes((int) ConnectionState.NOT_LISTENING));
                }
                else if (_clients.Count >= _maxAcceptNum)
                {
                    log = $"{remoteEndPoint} is rejected by SERVER FULL STATE";
                    client.GetStream().Write(BitConverter.GetBytes((int) ConnectionState.FULL));
                }

                _logger?.Log(log);
                client.GetStream().Close();
                client.Close();
                return;
            }

            _logger?.Log($"Remote client connected {remoteEndPoint}");

            EdenClient edenClient = new EdenClient(client, remoteEndPoint.ToString(), _bufferSize);
            lock (_clients)
            {
                _clients.Add(edenClient.id, edenClient);
            }

            _acceptEvent?.Invoke(edenClient.id);

            NetworkStream stream = client.GetStream();

            _server.BeginAcceptTcpClient(Listening, null);

            try
            {
                if (stream.CanRead)
                {
                    stream.BeginRead(edenClient.readBuffer, 0, edenClient.readBuffer.Length, ReadBuffer, edenClient);
                    //OK sign to client
                    stream.WriteAsync(BitConverter.GetBytes((int) ConnectionState.OK));
                }
                else // Exception for network stream  read is not ready
                {
                    RollBackClient();
                    _logger?.Log($"Error! Cannot read network stream of : {edenClient.id}");
                }
            }
            catch (Exception e)
            {
                RollBackClient();
                _logger?.Log($"Error! Cannot read network stream : " + e.Message);
            }
            
            void RollBackClient()
            {
                stream.Close();
                client.Close();
                lock (_clients)
                {
                    _clients.Remove(edenClient.id);
                }             
            }
        }

        private void CloseClient(EdenClient edenClient)
        {
            edenClient.stream.Close();
            edenClient.tcpClient.Close();

            _disconnectEvent?.Invoke(edenClient.id);

            lock (_clients)
            {
                _clients.Remove(edenClient.id);
            }
        }


        /// <summary>
        /// Async method for read packet and tcp buffer
        /// </summary>
        /// <param name="ar">ar.AsyncState is EdenClient which means sent data</param>
        private void ReadBuffer(IAsyncResult ar)
        {
            //ar.AsyncState cannot be null
            EdenClient edenClient = (EdenClient) ar.AsyncState!;
            NetworkStream stream = edenClient.stream;
            int numberOfBytes;
            try
            {
                numberOfBytes = stream.EndRead(ar);
            }
            catch (Exception e) // Exception for forced disconnection to client
            {
                CloseClient(edenClient);
                _logger?.Log($"Forced disconnection from client. {edenClient.id}\n{e.Message}");
                return;
            }

            if (numberOfBytes == 0) // Process for client disconnection
            {
                CloseClient(edenClient);
                _logger?.Log($"Client disconnected.{edenClient.id}");
                return;
            }

            int bytePointer = 0;
            //read TCP Buffer
            while (bytePointer < numberOfBytes)
            {
                int remainReadBufferLength = edenClient.readBuffer.Length - bytePointer;
                //Read Packet Length
                if (!edenClient.startReadObject)
                {
                    int remainObjectLengthBufferSize = PACKET_LENGTH_BUFFER_SIZE - edenClient.packetLengthBufferPointer;
                    //Read Length
                    if (remainReadBufferLength > remainObjectLengthBufferSize)
                    {
                        Array.Copy(edenClient.readBuffer,bytePointer, edenClient.packetLengthBuffer, edenClient.packetLengthBufferPointer, remainObjectLengthBufferSize);
                        var packetLength = BitConverter.ToInt32(edenClient.packetLengthBuffer);
                        edenClient.startReadObject = true;
                        edenClient.dataObjectBuffer = new byte[packetLength];
                        edenClient.dataObjectBufferPointer = 0;
                        edenClient.packetLengthBufferPointer = 0;
                        bytePointer += remainObjectLengthBufferSize;
                    }
                    //Stack part of length data to buffer
                    else
                    {
                        Array.Copy(edenClient.readBuffer,bytePointer, edenClient.packetLengthBuffer, edenClient.packetLengthBufferPointer, remainReadBufferLength);
                        edenClient.packetLengthBufferPointer += remainReadBufferLength;
                        bytePointer += remainReadBufferLength;
                    }
                }
                //Read Packet Data
                if (edenClient.startReadObject)
                {
                    var remainPacketLength = edenClient.dataObjectBuffer.Length - edenClient.dataObjectBufferPointer;
                    remainReadBufferLength = edenClient.readBuffer.Length - bytePointer;
                    //Stack part of packet data to buffer
                    if (remainPacketLength > remainReadBufferLength)
                    {
                        Array.Copy(edenClient.readBuffer, bytePointer, edenClient.dataObjectBuffer, edenClient.dataObjectBufferPointer, remainReadBufferLength);
                        edenClient.dataObjectBufferPointer += remainReadBufferLength;
                        bytePointer += remainReadBufferLength;
                    }
                    //Read packet data
                    else
                    {
                        Array.Copy(edenClient.readBuffer, bytePointer, edenClient.dataObjectBuffer, edenClient.dataObjectBufferPointer, remainPacketLength);
                        ReadJsonObject();
                        bytePointer += remainPacketLength;
                    }
                }

            }
            
            try
            {
                if (stream.CanRead)
                    stream.BeginRead(edenClient.readBuffer, 0, edenClient.readBuffer.Length, ReadBuffer, edenClient);
                else // Exception for network stream read is not ready
                {
                    CloseClient(edenClient);
                    _logger?.Log($"Error! Cannot read network stream of {edenClient.id}");
                }
            }
            catch (Exception e)
            {
                CloseClient(edenClient);
                _logger?.Log($"Error! Cannot read network stream : " + e.Message);
            }

            void ReadJsonObject()
            {
                edenClient.startReadObject = false;

                var jsonObject = new ArraySegment<byte>(edenClient.dataObjectBuffer, 0, edenClient.dataObjectBuffer.Length).ToArray();
                
                try
                {
                    var packet = JsonSerializer.Deserialize<EdenPacket>(jsonObject, _options);
                    _logger?.Log($"Recv({edenClient.id}/{jsonObject.Length,6}B) : [TAG] {packet.tag} [DATA] {packet.data.data}");

                    packet.data.CastJsonToType();

                    //Process request
                    if (packet.tag.StartsWith(REQUEST_PREFIX))
                    {
                        if (_responseEvents.TryGetValue(packet.tag, out var packetListenEvent))
                        {
                            try
                            {
                                if (!Send(packet.tag, edenClient.id, packetListenEvent(edenClient.id, packet.data)))
                                {
                                    _logger?.Log($"Error! Response Fail in ResponseEvent : {packet.tag} | {edenClient.id}");
                                }
                            }
                            catch (Exception e) // Exception for every problem in PacketListenEvent
                            {
                                _logger?.Log($"Error! Error caught in ResponseEvent : {packet.tag} | {edenClient.id} \n {e.Message}");
                            }
                        }
                        else // Exception for packet tag not registered
                        {
                            _logger?.Log($"Error! There is no packet tag {packet.tag} from {edenClient.id}");
                        }
                    }
                    //Process receive event
                    else
                    {
                        if (_receiveEvents.TryGetValue(packet.tag, out var packetListenEvent))
                        {
                            try
                            {
                                packetListenEvent(edenClient.id, packet.data);
                            }
                            catch (Exception e) // Exception for every problem in PacketListenEvent
                            {
                                _logger?.Log($"Error! Error caught in ReceiveEvent : {packet.tag} | {edenClient.id} \n {e.Message}");
                            }
                        }
                        else // Exception for packet tag not registered
                        {
                            _logger?.Log($"Error! There is no packet tag {packet.tag} from {edenClient.id}");
                        }
                    }
                }
                catch (Exception e) // Exception for not formed packet data
                {
                    _logger?.Log($"Error! Packet data is not JSON-formed on {edenClient.id}\n{e.Message}");
                    _logger?.Log($"Not formatted message \n {Encoding.UTF8.GetString(edenClient.readBuffer)}");
                }
            }
            
        }
        #endregion
    }
}
