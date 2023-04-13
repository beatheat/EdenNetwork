using System.Net;
using System.Text;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using static EdenNetwork.Constant;


namespace EdenNetwork.Tcp
{
    public class EdenTcpClient
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

        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private Action? _disconnectEvent;
        
        private string _serverId;

        private byte[] _readBuffer;

        private readonly Dictionary<string, Receiver> _receiveEvents;
        private readonly Dictionary<string, Responser> _responseEvents;

        private readonly Logger? _logger;

        private readonly string _ipAddress;
        private readonly int _port;
        private JsonSerializerOptions _options;
        
        private bool _startReadObject;
        private int _packetLengthBufferPointer;
        private readonly byte[] _packetLengthBuffer;
        private byte[] _dataObjectBuffer;
        private int _dataObjectBufferPointer;

        private bool _ignoreUnknownPacket;
        #endregion

        #region Public Methods

        /// <summary>
        /// Constructor for EdenNetClient
        /// </summary>
        public EdenTcpClient(string ipAddress, int port, string logPath = "", bool printConsole = true, int flushInterval = 3*60*1000)
        {
            _receiveEvents = new Dictionary<string, Receiver>();
            _responseEvents = new Dictionary<string, Responser>();
            _readBuffer = new byte[DEFAULT_BUFFER_SIZE];
            _disconnectEvent = null;
            _tcpClient = null;
            _stream = null;
            _serverId = "";
            
            _startReadObject = false;
            _packetLengthBufferPointer = 0;
            _packetLengthBuffer = new byte[PACKET_LENGTH_BUFFER_SIZE];

            _dataObjectBuffer = Array.Empty<byte>();
            _dataObjectBufferPointer = 0;

            _ignoreUnknownPacket = false;
            
            this._ipAddress = ipAddress;
            this._port = port;

            _options = new JsonSerializerOptions {IncludeFields = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull};
            
            _logger = null;
            if (logPath != "")
                _logger = new Logger(logPath, printConsole, flushInterval);
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
        /// Set data read buffer size in bytes, default value is 8192 Bytes
        /// </summary>
        /// <param name="size">buffer size in bytes</param>
        /// <param name="size">buffer size in bytes</param>
        public void SetBufferSize(int size)
        {
            Array.Resize(ref _readBuffer, size);
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
        /// <returns></returns>
        public int GetLocalEndPort()
        {
            if (_tcpClient == null) return -1;
            return ((IPEndPoint) _tcpClient.Client.LocalEndPoint!).Port;
        }
        
        /// <summary>
        /// Connect to server by IP, port
        /// </summary>
        /// <returns>returns ConnectionState of server[OK, FULL, NOT_LISTENING, ERROR]</returns>
        public ConnectionState Connect(int timeout=10, int localPort = 0)
        {
            if (_tcpClient != null)
                Close();
            try
            {
                // bind local port
                if (localPort != 0)
                {
                    var host = Dns.GetHostEntry(Dns.GetHostName());
                    foreach (var ip in host.AddressList)
                    {
                        if (ip.AddressFamily == AddressFamily.InterNetwork)
                        {
                            IPEndPoint ipLocalEndPoint = new IPEndPoint(ip, localPort);
                            _tcpClient = new TcpClient(ipLocalEndPoint);
                            break;
                        }
                    }
                }
                else
                    _tcpClient = new TcpClient();
                
                // _tcpClient.Connect(_ipAddress, _port);
                var result = _tcpClient!.BeginConnect(_ipAddress, _port, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(timeout*1000, true);
                if (success) _tcpClient.EndConnect(result);
                else return ConnectionState.ERROR;
                
                _stream = _tcpClient.GetStream();
                byte[] buffer = new byte[128];
                _stream.Read(buffer);
                ConnectionState serverState = (ConnectionState)BitConverter.ToInt32(buffer);
                if (serverState != ConnectionState.OK)
                {
                    _logger?.Log($"Cannot connect to server : SERVER IS {serverState.ToString()}");
                    _stream.Close();
                    _tcpClient.Close();
                    return serverState;
                }
            }
            catch (Exception e)
            {
                _logger?.Log($"Cannot connect to server : {e.Message}");
                return ConnectionState.ERROR;
            }
            
            try
            {
                _stream.BeginRead(_readBuffer, 0, _readBuffer.Length, ReadBuffer, null);
                _logger?.Log($"Connection success to {_ipAddress}:{_port}");
                _serverId = _ipAddress + ":" + _port;
            }
            catch(Exception e)
            {
                _logger?.Log($"Error! Cannot read network stream : " + e.Message);
                Close();
                return ConnectionState.ERROR;
            }

            return ConnectionState.OK;
        }

        /// <summary>
        /// Connect to server asynchronously by IP, port
        /// </summary>
        /// <returns>returns ConnectionState of server[OK, FULL, NOT_LISTENING, ERROR]</returns>
        public async Task<ConnectionState> ConnectAsync(int timeout=10, int localPort = 0)
        {
            return await Task.Run(() => Connect(timeout, localPort));
        }

        /// <summary>
        /// Connect to server asynchronously by IP, port
        /// </summary>
        /// <param name="callback">callback method execute after connection success or fail</param>
        public void BeginConnect(Action<ConnectionState> callback,int timeout=10, int localPort = 0)
        {
            Task.Run(() =>
            {
                callback(Connect(timeout, localPort));
            });

        }

        #region Send Methods
        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">EdenData structured sending data </param>
        /// <param name="log">Log send packet</param>
        public bool Send(string tag, EdenData data, bool log = true)
        {
            if (_stream is {CanWrite: true})
            {
                EdenPacket packet = new EdenPacket {tag = tag, data = data};
                string jsonPacket = JsonSerializer.Serialize(packet, _options);
                byte[] bytes = Encoding.UTF8.GetBytes(jsonPacket);
                byte[] sendObj = BitConverter.GetBytes(bytes.Length);
                sendObj = sendObj.Concat(bytes).ToArray();
                
                _stream.Write(sendObj, 0, sendObj.Length);
                if(log)
                    _logger?.Log($"Send({_serverId}/{bytes.Length,4}B) : [TAG] {tag} " +
                        $"[DATA] {JsonSerializer.Serialize(data.data, _options)}");
                return true;
            }
            else
            {
                _logger?.Log($"Error! Send - NetworkStream cannot write");
                return false;
            }
        }
        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="log">Log send packet</param>
        public bool Send(string tag, bool log = true)
        {
            return Send(tag, new EdenData(), log);
        }

        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object sending data </param>
        /// <param name="log">Log send packet</param>
        public bool Send(string tag, object? data, bool log = true)
        {
            return Send(tag, new EdenData(data), log);
        }

        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data </param>
        /// <param name="log">Log send packet</param>
        public bool Send(string tag, object[] data, bool log = true)
        {
            return Send(tag, new EdenData(data), log);
        }
        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">dictionary sending data </param>
        /// <param name="log">Log send packet</param>
        public bool Send(string tag, Dictionary<string, object> data, bool log = true)
        {
            return Send(tag, new EdenData(data), log);
        }


        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">EdenData structured sending data </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> SendAsync(string tag, EdenData data, bool log = true)
        {
            return await Task.Run(async () =>
            {
                if (_stream is {CanWrite: true})
                {
                    EdenPacket packet = new EdenPacket {tag = tag, data = data};

                    string jsonPacket = JsonSerializer.Serialize(packet, _options);
                    byte[] bytes = Encoding.UTF8.GetBytes(jsonPacket);
                    byte[] sendObj = BitConverter.GetBytes(bytes.Length);
                    sendObj = sendObj.Concat(bytes).ToArray();
                    
                    await _stream.WriteAsync(sendObj, 0, sendObj.Length);
                    if(log)
                        _logger?.Log($"Send({_serverId}/{bytes.Length,4}B) : [TAG] {tag} " +
                            $"[DATA] {JsonSerializer.Serialize(data.data, _options)}");

                    return true;
                }
                else
                {
                    _logger?.Log($"Error! SendAsync - NetworkStream cannot write on server");
                    return false;
                }
            });
        }
        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> SendAsync(string tag, bool log = true)
        {
            return await SendAsync(tag, new EdenData(), log);
        }
        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> SendAsync(string tag, object? data, bool log = true)
        {
            return await SendAsync(tag, new EdenData(data), log);
        }
        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> SendAsync(string tag, object[] data, bool log = true)
        {
            return await SendAsync(tag, new EdenData(data), log);
        }
        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">dictionary sending data </param>
        /// <param name="log">Log send packet</param>
        public async Task<bool> SendAsync(string tag, Dictionary<string, object> data, bool log = true)
        {
            return await SendAsync(tag, new EdenData(data), log);
        }



        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="callback">callback function[bool success] run after data send</param>
        /// <param name="data">EdenData structured sending data</param>
        /// <param name="log">Log send packet</param>
        public void BeginSend(string tag, Action<bool> callback, EdenData data, bool log = true)
        {
            Task.Run(() => callback(Send(tag, data, log)));
        }
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="callback">callback function[bool success ] run after data send</param>
        /// <param name="log">Log send packet</param>
        public void BeginSend(string tag, Action<bool> callback, bool log = true)
        {
            BeginSend(tag, callback, new EdenData(), log);
        }
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="callback">callback function[bool success ] run after data send</param>
        /// <param name="data">object array sending data</param>
        /// <param name="log">Log send packet</param>
        public void BeginSend(string tag, Action<bool> callback, object? data, bool log = true)
        {
            BeginSend(tag, callback, new EdenData(data), log);
        }
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="callback">callback function[bool success ] run after data send</param>
        /// <param name="data">object array sending data</param>
        /// <param name="log">Log send packet</param>
        public void BeginSend(string tag, Action<bool> callback, object[] data, bool log = true)
        {
            BeginSend(tag, callback, new EdenData(data), log);
        }
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="callback">callback function[bool success] run after data send</param>
        /// <param name="data">dictionary array sending data</param>
        /// <param name="log">Log send packet</param>
        public void BeginSend(string tag, Action<bool> callback, Dictionary<string, object> data, bool log = true)
        {
            BeginSend(tag, callback, new EdenData(data), log);
        }
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">sending data</param>
        /// <param name="log">Log send packet</param>
        public void BeginSend(string tag, EdenData data, bool log = true)
        {
            Task.Run(() => Send(tag, data, log));
        }
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data</param>
        /// <param name="log">Log send packet</param>
        public void BeginSend(string tag, object? data, bool log = true)
        {
            BeginSend(tag, new EdenData(data), log);
        }
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data</param>
        /// <param name="log">Log send packet</param>
        public void BeginSend(string tag, object[] data, bool log = true)
        {
            BeginSend(tag, new EdenData(data), log);
        }
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">dictionary sending data</param>
        /// <param name="log">Log send packet</param>
        public void BeginSend(string tag, Dictionary<string, object> data, bool log = true)
        {
            BeginSend(tag, new EdenData(data), log);
        }
        #endregion

        #region Request Methods
        /// <summary>
        /// Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">EdenData structured sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="log">Log send packet</param>
        public EdenData Request(string tag, int timeout, EdenData data, bool log = true)
        {
            tag = REQUEST_PREFIX + tag;
            if (!_responseEvents.ContainsKey(tag))
                _responseEvents.Add(tag, new Responser {data = null, log = log});
            bool result = Send(tag, data, log);
            if (result == false) 
                return EdenData.Error("ERR:Request send failed");
            double time = 0;
            Responser responser;
            do
            {
                Thread.Sleep(100);
                time += 0.1;

                if (_responseEvents.TryGetValue(tag, out responser!) && responser.data != null)
                    break;

            } while (timeout > time);
            _responseEvents.Remove(tag);
            if (timeout <= time) 
                return EdenData.Error("ERR:Request timeout");
            return responser.data!.Value;
        }
        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="log">Log send packet</param>
        public EdenData Request(string tag, int timeout, bool log = true)
        {
            return Request(tag, timeout, new EdenData(), log);
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="log">Log send packet</param>
        public EdenData Request(string tag, int timeout, object? data, bool log = true)
        {
            return Request(tag, timeout, new EdenData(data), log);
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="log">Log send packet</param>
        public EdenData Request(string tag, int timeout, object[] data, bool log = true)
        {
            return Request(tag, timeout, new EdenData(data), log);
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">Dictionary sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="log">Log send packet</param>
        public EdenData Request(string tag, int timeout, Dictionary<string, object> data, bool log = true)
        {
            return Request(tag, timeout, new EdenData(data), log);
        }

        /// <summary>
        /// Request any data to server asynchronously and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">EdenData structured sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="log">Log send packet</param>
        public async Task<EdenData> RequestAsync(string tag, int timeout, EdenData data, bool log = true)
        {
            return await Task.Run(async () =>
            {
                tag = REQUEST_PREFIX + tag;
                if (!_responseEvents.ContainsKey(tag))
                    _responseEvents.Add(tag, new Responser{log = log, data = null});
                bool result = await SendAsync(tag, data, log);
                if (result == false) 
                    return EdenData.Error("ERR:Request send failed");
                double time = 0;
                Responser responser;
                do
                {
                    await Task.Delay(100);
                    time += 0.1;

                    if (_responseEvents.TryGetValue(tag, out responser!) && responser.data != null)
                        break;

                } while (timeout > time);
                _responseEvents.Remove(tag);
                if (timeout <= time) return EdenData.Error("ERR:Request timeout");
                else return responser.data!.Value;
            });
        }
        
        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="log">Log send packet</param>
        public async Task<EdenData> RequestAsync(string tag, int timeout, bool log = true)
        {
            return await RequestAsync(tag, timeout, new EdenData(), log);
        }

        
        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="log">Log send packet</param>
        public async Task<EdenData> RequestAsync(string tag, int timeout, object? data, bool log = true)
        {
            return await RequestAsync(tag, timeout, new EdenData(data), log);
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="log">Log send packet</param>
        public async Task<EdenData> RequestAsync(string tag, int timeout, object[] data, bool log = true)
        {
            return await RequestAsync(tag, timeout, new EdenData(data), log);
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">Dictionary sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="log">Log send packet</param>
        public async Task<EdenData> RequestAsync(string tag, int timeout, Dictionary<string, object> data, bool log = true)
        {
            return await RequestAsync(tag, timeout, new EdenData(data), log);
        }


        /// <summary>
        /// Request any data to server asynchronously and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="response">ReceiveEvent of same packet tag</param>
        /// <param name="data">EdenData structured sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="log">Log send packet</param>
        public void BeginRequest(string tag, int timeout, Action<EdenData> response, EdenData data, bool log = true)
        {
            Task.Run(() =>
            {
                tag = REQUEST_PREFIX + tag;
                if (!_responseEvents.ContainsKey(tag))
                    _responseEvents.Add(tag, new Responser{log = log, data = null});
                bool result = Send(tag, data, log);
                if (result == false) 
                    response(EdenData.Error("ERR:Request send failed"));
                double time = 0;
                Responser responser;
                do
                {
                    Thread.Sleep(100);
                    time += 0.1;

                    if (_responseEvents.TryGetValue(tag, out responser!) && responser.data != null)
                        break;
                } while (timeout > time);

                _responseEvents.Remove(tag);
                if (timeout <= time) response(EdenData.Error("ERR:Request timeout"));
                else response(responser.data!.Value);
            });
        }
        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="response">ReceiveEvent of same packet tag</param>
        /// <param name="data">object sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="log">Log send packet</param>
        public void BeginRequest(string tag, int timeout, Action<EdenData> response, bool log = true)
        {
            BeginRequest(tag, timeout, response, new EdenData(), log);
        }
        
        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="response">ReceiveEvent of same packet tag</param>
        /// <param name="data">object sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="log">Log send packet</param>
        public void BeginRequest(string tag, int timeout, Action<EdenData> response, object? data, bool log = true)
        {
            BeginRequest(tag, timeout, response, new EdenData(data), log);
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="response">ReceiveEvent of same packet tag</param>
        /// <param name="data">object array sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="log">Log send packet</param>
        public void BeginRequest(string tag, int timeout, Action<EdenData> response, object[] data, bool log = true)
        {
            BeginRequest(tag, timeout, response, new EdenData(data), log);
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="response">ReceiveEvent of same packet tag</param>
        /// <param name="data">dictionary structured sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        /// <param name="log">Log send packet</param>
        public void BeginRequest(string tag, int timeout, Action<EdenData> response, Dictionary<string, object> data, bool log = true)
        {
            BeginRequest(tag, timeout, response, new EdenData(data), log);
        }


        #endregion

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
        /// Close server and release 
        /// </summary>
        public void Close()
        {
            _stream?.Close();
            _tcpClient?.Close();
            _logger?.Log($"Stream {_serverId} is closed");
            _logger?.Close();
        }
        #endregion

        #region Private Methods

        /// <summary>
        /// Async method for read packet and tcp buffer
        /// </summary>
        /// <param name="ar">ar.AsyncState is EdenClient which sent data</param>
        private void ReadBuffer(IAsyncResult ar)
        {
            int numberOfBytes;
            try
            {
                numberOfBytes = _stream!.EndRead(ar);
            }
            catch (ObjectDisposedException)
            {
                return; // may be connection is closed safely
            }
            catch (Exception e)
            {
                _logger?.Log($"Forced disconnection from server. {_serverId}\n{e.Message}");
                Close();
                _disconnectEvent?.Invoke();
                return;
            }

            if (numberOfBytes == 0) // this happens when the client is disconnected
            {
                _logger?.Log($"Server disconnected. {_serverId}");
                Close();
                _disconnectEvent?.Invoke();
                return;
            }

  
            int bytePointer = 0;
            //read TCP Buffer
            while (bytePointer < numberOfBytes)
            {
                int remainReadBufferLength = _readBuffer.Length - bytePointer;
                //Read Packet Length
                if (!_startReadObject)
                {
                    int remainObjectLengthBufferSize = PACKET_LENGTH_BUFFER_SIZE - _packetLengthBufferPointer;
                    //Read Length
                    if (remainReadBufferLength > remainObjectLengthBufferSize)
                    {
                        Array.Copy(_readBuffer,bytePointer, _packetLengthBuffer, _packetLengthBufferPointer, remainObjectLengthBufferSize);
                        var packetLength = BitConverter.ToInt32(_packetLengthBuffer);
                        _startReadObject = true;
                        _dataObjectBuffer = new byte[packetLength];
                        _dataObjectBufferPointer = 0;
                        _packetLengthBufferPointer = 0;
                        bytePointer += remainObjectLengthBufferSize;
                    }
                    //Stack part of length data to buffer
                    else
                    {
                        Array.Copy(_readBuffer,bytePointer, _packetLengthBuffer, _packetLengthBufferPointer, remainReadBufferLength);
                        _packetLengthBufferPointer += remainReadBufferLength;
                        bytePointer += remainReadBufferLength;
                    }
                }
                //Read Packet Data
                if (_startReadObject)
                {
                    var remainPacketLength = _dataObjectBuffer.Length - _dataObjectBufferPointer;
                    remainReadBufferLength = _readBuffer.Length - bytePointer;
                    //Stack part of packet data to buffer
                    if (remainPacketLength > remainReadBufferLength)
                    {
                        Array.Copy(_readBuffer, bytePointer, _dataObjectBuffer, _dataObjectBufferPointer, remainReadBufferLength);
                        _dataObjectBufferPointer += remainReadBufferLength;
                        bytePointer += remainReadBufferLength;
                    }
                    //Read packet data
                    else
                    {
                        Array.Copy(_readBuffer, bytePointer, _dataObjectBuffer, _dataObjectBufferPointer, remainPacketLength);
                        ReadJsonObject();
                        bytePointer += remainPacketLength;
                    }
                }

            }
            
            try
            {
                if (_stream.CanRead)
                    _stream.BeginRead(_readBuffer, 0, _readBuffer.Length, ReadBuffer, null);
                else // Exception for network stream read is not ready
                {
                    Close();
                    _logger?.Log($"Error! Cannot read network stream");
                }
            }
            catch (Exception e)
            {
                Close();
                _logger?.Log($"Error! Cannot read network stream : " + e.Message);
            }

            void ReadJsonObject()
            {
                _startReadObject = false;

                var jsonObject = new ArraySegment<byte>(_dataObjectBuffer, 0, _dataObjectBuffer.Length).ToArray();
                
                try
                {
                    var packet = JsonSerializer.Deserialize<EdenPacket>(jsonObject, _options);
                    
                    packet.data.CastJsonToType();
                    //Process response
                    if (packet.tag.StartsWith(REQUEST_PREFIX))
                    {
                        if (_responseEvents.ContainsKey(packet.tag))
                        {
                            _responseEvents[packet.tag].data = packet.data;
                            if(_responseEvents[packet.tag].log)
                                _logger?.Log($"Resp({_serverId}/{jsonObject.Length,6}B) : [TAG] {packet.tag} [DATA] {packet.data.data}");
                        }
                        else
                        {
                            if(!_ignoreUnknownPacket)
                                _logger?.Log($"Error! There is no packet tag {packet.tag} from {_serverId}");
                        }
                    }
                    //Process receive
                    else
                    {
                        if (_receiveEvents.TryGetValue(packet.tag, out var receiver))
                        {
                            if(receiver.log)
                                _logger?.Log($"Recv({_serverId}/{jsonObject.Length,6}B) : [TAG] {packet.tag} [DATA] {packet.data.data}");
                            try { receiver.receiveEvent(packet.data); }
                            catch (Exception e) // Exception for every problem in PacketListenEvent
                            {
                                _logger?.Log($"Error! Error caught in ReceiveEvent : {packet.tag} | {_serverId} \n {e.Message}");
                            }
                        }
                        else
                        {
                            if(!_ignoreUnknownPacket)
                                _logger?.Log($"Error! There is no packet tag {packet.tag} from {_serverId}");
                        }
                    }


                }
                catch (Exception e)
                {
                    _logger?.Log($"Error! Packet data is not JSON-formed on {_serverId}\n{e.Message}");
                    _logger?.Log($"Not formatted message \n {Encoding.UTF8.GetString(_readBuffer)}");
                }
            }
        }
        
        #endregion
    }
}