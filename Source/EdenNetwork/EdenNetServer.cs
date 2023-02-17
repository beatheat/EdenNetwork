using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using static EdenNetwork.Constant;

namespace EdenNetwork
{
    public class EdenNetServer
    {
        #region Pre-defined Class&Struct
        /// <summary>
        /// Struct : struct for saving client info
        /// </summary>
        private struct EdenClient
        {
            public EdenClient(TcpClient tcpClient, string id)
            {
                this.tcpClient = tcpClient;
                this.stream = tcpClient.GetStream();
                this.id = id;
                this.readBuffer = new byte[BUF_SIZE];
            }

            public readonly TcpClient tcpClient;
            public readonly NetworkStream stream;
            public readonly string id;
            public readonly byte[] readBuffer;
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

        public int ClientNum => _clients.Count;

        #endregion

        #region Public Methods
        #region Constructor
        /// <summary>
        /// Constructor for EdenNetSerer
        /// </summary>
        /// <param name="ipAddress">IPv4 address to open server</param>
        /// <param name="port">Port number to open server</param>
        /// <param name="logPath">Path for log data to wrtie</param>
        /// <exception cref="Exception">
        /// Two case of Exception exists
        /// case 1. Cannot create tcplistener 
        /// case 2. Cannot open stream for log file path
        /// </exception>
        public EdenNetServer(string ipAddress, int port, string logPath, bool printConsole = true, int flushInterval = 3*60*1000)
        {
            try
            {
                _server = new TcpListener(IPAddress.Parse(ipAddress), port);
            }
            catch (Exception e)
            {
                throw new Exception("Cannot create TcpListener on " + ipAddress + ":" + port + "\n" + e.Message);
            }
            _clients = new Dictionary<string, EdenClient>();
            _receiveEvents = new Dictionary<string, Action<string, EdenData>>();
            _responseEvents = new Dictionary<string, Func<string, EdenData, EdenData>>();
            _isListening = false;
            _acceptEvent = null;
            _disconnectEvent = null;
            _logger = null;
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
        /// <param name="logPath">Path for log data to wrtie</param>
        ///         /// <exception cref="Exception">
        /// Two case of Exception exists
        /// case 1. Cannot create tcplistener 
        /// case 2. Cannot open stream for log file path
        /// </exception>
        public EdenNetServer(int port, string logPath) : this("0.0.0.0", port, logPath) { }
        /// <summary>
        /// Constructor for EdenNetSerer
        /// </summary>
        /// <param name="port">Port number to open server</param>
        /// <exception cref="Exception">
        /// Two case of Exception exists
        /// case 1. Cannot create tcplistener 
        /// case 2. Cannot open stream for log file path
        /// </exception>
        public EdenNetServer(int port) : this("0.0.0.0", port, "") { }
        #endregion
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
            
            _logger?.Log("Eden Server is listening on  " + ipInfo.ToString());
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
            Listen(maxAcceptNum, (string clientId) => { });
        }
        /// <summary>
        /// Listen and wait for client connection
        /// </summary>
        public void Listen()
        {
            Listen(INF_CLIENT_ACCEPT, (string clientId) => { });
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
                client.stream?.Close();
                client.tcpClient?.Close();
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

                string jsonPacket = JsonSerializer.Serialize(packet, new JsonSerializerOptions { IncludeFields = true });
                byte[] bytes = Encoding.UTF8.GetBytes(jsonPacket);
                byte[] sendObj = BitConverter.GetBytes(bytes.Length);
                sendObj = sendObj.Concat(bytes).ToArray();
                if (sendObj.Length >= BUF_SIZE) // Exception for too big data size
                {
                    _logger?.Log($"Error! Send - Too big data to send once, EdenNetProtocol support size under ({BUF_SIZE})KB");
                    return false;
                }
                // Begin sending packet
                stream.Write(sendObj, 0, sendObj.Length);
                _logger?.Log($"Send({clientId}/{bytes.Length,4}B) : [TAG] {tag} " +
                    $"[DATA] {JsonSerializer.Serialize(data.data, new JsonSerializerOptions { IncludeFields = true })}");
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
        /// <param name="data">Edendata structured sending data</param>
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

                    string jsonPacket = JsonSerializer.Serialize(packet, new JsonSerializerOptions { IncludeFields = true });
                    byte[] bytes = Encoding.UTF8.GetBytes(jsonPacket);
                    byte[] sendObj = BitConverter.GetBytes(bytes.Length);
                    sendObj = sendObj.Concat(bytes).ToArray();
                    if (sendObj.Length >= BUF_SIZE) // Exception for too big data size
                    {
                        _logger?.Log($"Error! SendAsync - Too big data to send once, EdenNetProtocol support data size under ({BUF_SIZE})");
                        return false;
                    }
                    // Begin sending packet
                    await stream.WriteAsync(sendObj, 0, sendObj.Length);
                    _logger?.Log($"Send({clientId}/{bytes.Length,4}B) : [TAG] {tag} " +
                        $"[DATA] {JsonSerializer.Serialize(data.data, new JsonSerializerOptions { IncludeFields = true })}");
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
            IPEndPoint remoteEndPoint = (IPEndPoint)client.Client.RemoteEndPoint!;
            //remoteEndPoint cannot be null
            if (!_isListening || _clients.Count >= _maxAcceptNum)
            {
                if (!_isListening)
                {
                    log = $"{remoteEndPoint} is rejected by SERVER NOT LISTENING STATE";
                    client.GetStream().Write(BitConverter.GetBytes((int)ConnectionState.NOT_LISTENING));
                }
                else if (_clients.Count >= _maxAcceptNum)
                {
                    log = $"{remoteEndPoint} is rejected by SERVER FULL STATE";
                    client.GetStream().Write(BitConverter.GetBytes((int)ConnectionState.FULL));
                }
                _logger?.Log(log);
                client.GetStream().Close();
                client.Close();
                return;
            }

            _logger?.Log($"Remote client connected {remoteEndPoint}");
            
            EdenClient edenClient = new EdenClient(client, remoteEndPoint.ToString())!;
            lock (_clients)
            {
                _clients.Add(edenClient.id, edenClient);
            }
            _acceptEvent?.Invoke(edenClient.id);

            NetworkStream stream = client.GetStream();

            //OK sign to client
            stream.Write(BitConverter.GetBytes((int)ConnectionState.OK));

            _server.BeginAcceptTcpClient(Listening, null);

            if (stream.CanRead)
                stream.BeginRead(edenClient.readBuffer, 0, edenClient.readBuffer.Length, ReadBuffer, edenClient);
            else // Exception for network stream  read is not ready
            {
                _logger?.Log($"Error! Cannot read network stream of : {edenClient.id}");
            }
        }

        /// <summary>
        /// Async method for read packet and tcp buffer
        /// </summary>
        /// <param name="ar">ar.AsyncState is EdenClient which means sent data</param>
        private void ReadBuffer(IAsyncResult ar)
        {
            //ar.AsyncState cannot be null
            EdenClient edenClient = (EdenClient)ar.AsyncState!;
            NetworkStream stream = edenClient.stream;
            int numberOfBytes;
            try
            {
                numberOfBytes = stream.EndRead(ar);
            }
            catch (Exception e) // Exception for forced disconnection to client
            {
                edenClient.stream.Close();
                edenClient.tcpClient.Close();

                _disconnectEvent?.Invoke(edenClient.id);

                lock (_clients)
                {
                    _clients.Remove(edenClient.id);
                }

                _logger?.Log($"Forced disconnection from client. {edenClient.id}\n{e.Message}");
                return;
            }

            if (numberOfBytes == 0)// Process for client disconnection
            {
                edenClient.stream.Close();
                edenClient.tcpClient.Close();

                _disconnectEvent?.Invoke(edenClient.id);

                lock (_clients)
                {
                    _clients.Remove(edenClient.id);
                }

                _logger?.Log($"Client disconnected.{edenClient.id}");
                return;
            }

            int bytePointer = 0;
            while(bytePointer < numberOfBytes)
            {
                var packetLength = BitConverter.ToInt32(new ArraySegment<byte>(edenClient.readBuffer, bytePointer , 4));
                bytePointer += 4;
                byte[] jsonObject = (new ArraySegment<byte>(edenClient.readBuffer, bytePointer, packetLength).ToArray());
                bytePointer += packetLength;

                try
                {
                    var packet = JsonSerializer.Deserialize<EdenPacket>(jsonObject, new JsonSerializerOptions { IncludeFields = true });
                    _logger?.Log($"Recv({edenClient.id}/{packetLength,4}B) : [TAG] {packet.tag} [DATA] {packet.data.data}");

                    if (packet.tag.StartsWith(REQUEST_PREFIX))
                    {
                        if (_responseEvents.TryGetValue(packet.tag, out var packetListenEvent))
                        {
                            packet.data.CastJsonToType();
                            try 
                            { 
                                if(!Send(packet.tag, edenClient.id, packetListenEvent(edenClient.id, packet.data)))
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
                    else
                    {
                        if (_receiveEvents.TryGetValue(packet.tag, out var packetListenEvent))
                        {
                            packet.data.CastJsonToType();
                            try { packetListenEvent(edenClient.id, packet.data); }
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
                }
            }

            lock (stream)
            {
                try
                {
                    if (stream.CanRead)
                        stream.BeginRead(edenClient.readBuffer, 0, edenClient.readBuffer.Length, ReadBuffer, edenClient);
                    else // Exception for network stream read is not ready
                    {
                        _logger?.Log($"Error! Cannot read network stream of {edenClient.id}");
                    }
                }
                catch(Exception e)
                {
                    _logger?.Log(e.Message);
                }
            }
        }
        

        #endregion
    }
}
