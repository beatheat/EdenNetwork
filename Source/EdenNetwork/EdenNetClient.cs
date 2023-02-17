using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using static EdenNetwork.Constant;


namespace EdenNetwork
{
    /// <summary>
    /// Enum for server connection state
    /// </summary>
    public enum ConnectionState
    {
        OK, FULL, NOT_LISTENING, ERROR
    }

    /// <summary>
    /// EdenNetwork Manager for Unity
    /// </summary>
    public class EdenNetClient
    {
        #region Fields

        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private Action<string>? _disconnectEvent;
        
        private string _serverId;

        private readonly byte[] _readBuffer;

        private readonly Dictionary<string, Action<EdenData>> _receiveEvents;
        private readonly Dictionary<string, EdenData?> _responseEvents;

        private readonly Logger? _logger;

        private readonly string _ipAddress;
        private readonly int _port;
        #endregion

        #region Public Methods

        /// <summary>
        /// EdenNetwork manager for UNITY and C#9.0 
        /// </summary>
        public EdenNetClient(string ipAddress, int port, string logPath = "", bool printConsole = true, int flushInterval = 3*60*1000)
        {
            _receiveEvents = new Dictionary<string, Action<EdenData>>();
            _responseEvents = new Dictionary<string, EdenData?>();
            _readBuffer = new byte[BUF_SIZE];
            _disconnectEvent = null;
            _tcpClient = null;
            _stream = null;
            _serverId = "";

            this._ipAddress = ipAddress;
            this._port = port;
            
            _logger = null;
            if (logPath != "")
                _logger = new Logger(logPath, "EdenNetClient", printConsole, flushInterval);
        }

        /// <summary>
        /// Connect to server by IP, port
        /// </summary>
        /// <returns>returns ConnectionState of server[OK, FULL, NOT_LISTENING, ERROR]</returns>
        public ConnectionState Connect()
        {
            if (_tcpClient != null)
                Close();
            try
            {
                _tcpClient = new TcpClient(_ipAddress, _port);
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
            
            _logger?.Log($"Connection success to {_ipAddress}:{_port}");
            _serverId = _ipAddress + ":" + _port;
            _stream.BeginRead(_readBuffer, 0, _readBuffer.Length, ReadBuffer, null);
            return ConnectionState.OK;
        }

        /// <summary>
        /// Connect to server asynchronously by IP, port
        /// </summary>
        /// <returns>returns ConnectionState of server[OK, FULL, NOT_LISTENING, ERROR]</returns>
        public async Task<ConnectionState> ConnectAsync()
        {
            return await Task.Run(async () =>
            {
                if (_tcpClient != null)
                    Close();
                try
                {
                    _tcpClient = new TcpClient(AddressFamily.InterNetwork);

                    _tcpClient.Connect(_ipAddress, _port);
                    await _tcpClient.ConnectAsync(_ipAddress, _port);
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
            
                _logger?.Log($"Connection success to {_ipAddress}:{_port}");
                _serverId = _ipAddress + ":" + _port;
                _stream.BeginRead(_readBuffer, 0, _readBuffer.Length, ReadBuffer, null);
                return ConnectionState.OK;
            });
        }

        /// <summary>
        /// Connect to server asyncronously by IP, port
        /// </summary>
        /// <param name="callback">callback method execute after connection success or fail</param>
        public void BeginConnect(Action<ConnectionState> callback)
        {
            Task.Run(() =>
            {
                if (_tcpClient != null)
                    Close();
                _serverId = _ipAddress + ":" + _port;
                _tcpClient = new TcpClient(AddressFamily.InterNetwork);
                try
                {
                    _tcpClient.Connect(_ipAddress, _port);
                    _stream = _tcpClient.GetStream();

                    byte[] buffer = new byte[128];
                    _stream.Read(buffer);
                    ConnectionState serverState = (ConnectionState)BitConverter.ToInt32(buffer);
                    if (serverState != ConnectionState.OK)
                    {
                        _logger?.Log($"Cannot connect to server : SERVER IS {serverState.ToString()}");
                        _stream.Close();
                        _tcpClient.Close();
                        callback(serverState);
                        return;
                    }
                }
                catch
                {
                    _logger?.Log("Cannot connect to server");
                    callback(ConnectionState.ERROR);
                    return;
                }
                _logger?.Log($"Connection success to {_ipAddress}:{_port}");
                _serverId = _ipAddress + ":" + _port;
                _stream.BeginRead(_readBuffer, 0, _readBuffer.Length, ReadBuffer, null);
                callback(ConnectionState.OK);
            });

        }

        #region Send Methods
        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">EdenData structured sending data </param>
        public bool Send(string tag, EdenData data)
        {
            if (_stream is {CanWrite: true})
            {
                EdenPacket packet = new EdenPacket {tag = tag, data = data};
                string jsonPacket = JsonSerializer.Serialize(packet, new JsonSerializerOptions { IncludeFields = true });
                byte[] bytes = Encoding.UTF8.GetBytes(jsonPacket);
                byte[] sendObj = BitConverter.GetBytes(bytes.Length);
                sendObj = sendObj.Concat(bytes).ToArray();

                if (sendObj.Length >= BUF_SIZE)
                {
                    _logger?.Log($"Error! Send - Too big data to send once, EdenNetProtocol support size under ({BUF_SIZE})KB");
                    return false;
                }
                _stream.Write(sendObj, 0, sendObj.Length);
                _logger?.Log($"Send({_serverId}/{bytes.Length}Bytes) : [TAG] {tag} " +
                    $"[DATA] {JsonSerializer.Serialize(data.data, new JsonSerializerOptions { IncludeFields = true })}");
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
        /// <param name="data">object array sending data </param>
        public bool Send(string tag, params object[]? data)
        {
            if (data == null || data.Length == 0)
            {
                return Send(tag, new EdenData());
            }
            else if (data.Length == 1)
            {
                return Send(tag, new EdenData(data[0]));
            }
            else
                return Send(tag, new EdenData(data));
        }
        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">dictionary sending data </param>
        public bool Send(string tag, Dictionary<string, object>? data)
        {
            if (data == null)
            {
                return Send(tag, new EdenData());
            }
            else
                return Send(tag, new EdenData(data));
        }


        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">EdenData structured sending data </param>
        public async Task<bool> SendAsync(string tag, EdenData data)
        {
            return await Task.Run(async () =>
            {
                if (_stream is {CanWrite: true})
                {
                    EdenPacket packet = new EdenPacket {tag = tag, data = data};

                    string jsonPacket = JsonSerializer.Serialize(packet, new JsonSerializerOptions { IncludeFields = true });
                    byte[] bytes = Encoding.UTF8.GetBytes(jsonPacket);
                    byte[] sendObj = BitConverter.GetBytes(bytes.Length);
                    sendObj = sendObj.Concat(bytes).ToArray();

                    if (sendObj.Length >= BUF_SIZE)
                    {
                        _logger?.Log($"Error! SendAsync - Too big data to send once, EdenNetProtocol support data size under ({BUF_SIZE})");
                        return false;
                    }
                    await _stream.WriteAsync(sendObj, 0, sendObj.Length);
                    _logger?.Log($"Send({_serverId}/{bytes.Length}Bytes) : [TAG] {tag} " +
                        $"[DATA] {JsonSerializer.Serialize(data.data, new JsonSerializerOptions { IncludeFields = true })}");

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
        /// <param name="data">object array sending data </param>
        public async Task<bool> SendAsync(string tag, params object[]? data)
        {
            if (data == null || data.Length == 0)
            {
                return await SendAsync(tag, new EdenData());
            }
            else if (data.Length == 1)
            {
                return await SendAsync(tag, new EdenData(data[0]));
            }
            else
                return await SendAsync(tag, new EdenData(data));
        }
        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">dictionary sending data </param>
        public async Task<bool> SendAsync(string tag, Dictionary<string, object>? data)
        {
            if (data == null)
            {
                return await SendAsync(tag, new EdenData());
            }
            else
                return await SendAsync(tag, new EdenData(data));
        }



        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="callback">callback function[bool success] run after data send</param>
        /// <param name="data">EdenData structured sending data</param>
        public void BeginSend(string tag, Action<bool> callback, EdenData data)
        {
            Task.Run(() => callback(Send(tag, data)));
        }
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="callback">callback function[bool success ] run after data send</param>
        /// <param name="data">object array sending data</param>
        public void BeginSend(string tag, Action<bool> callback, params object[]? data)
        {
            if (data == null || data.Length == 0)
            {
                BeginSend(tag, callback, new EdenData());
            }
            else if (data.Length == 1)
            {
                BeginSend(tag, callback, new EdenData(data[0]));
            }
            else
                BeginSend(tag, callback, new EdenData(data));
        }
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="callback">callback function[bool success] run after data send</param>
        /// <param name="data">dictionary array sending data</param>
        public void BeginSend(string tag, Action<bool> callback, Dictionary<string, object>? data)
        {
            if (data == null)
                BeginSend(tag, callback, new EdenData());
            else
                BeginSend(tag, callback, new EdenData(data));
        }
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">sending data</param>
        public void BeginSend(string tag, EdenData data)
        {
            Task.Run(() => Send(tag, data));
        }
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data</param>
        public void BeginSend(string tag, params object[]? data)
        {
            if (data == null || data.Length == 0)
            {
                BeginSend(tag, new EdenData());
            }
            else if (data.Length == 1)
            {
                BeginSend(tag, new EdenData(data[0]));
            }
            else
                BeginSend(tag, new EdenData(data));
        }
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">dictionary sending data</param>
        public void BeginSend(string tag, Dictionary<string, object>? data)
        {
            if (data == null)
                BeginSend(tag, new EdenData());
            else
                BeginSend(tag, new EdenData(data));
        }
        #endregion

        #region Request Methods
        /// <summary>
        /// Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">EdenData structured sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        public EdenData Request(string tag, int timeout, EdenData data)
        {
            tag = REQUEST_PREFIX + tag;
            if (!_responseEvents.ContainsKey(tag))
                _responseEvents.Add(tag, null);
            bool result = Send(tag, data);
            if (result == false) 
                return EdenData.Error("ERR:Request send failed");
            double time = 0;
            EdenData? rdata;
            do
            {
                Thread.Sleep(100);
                time += 0.1;

                if (_responseEvents.TryGetValue(tag, out rdata) && rdata != null)
                    break;

            } while (timeout > time);
            _responseEvents.Remove(tag);
            if (timeout <= time) 
                return EdenData.Error("ERR:Request timeout");
            return rdata!.Value;
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        public EdenData Request(string tag, int timeout, params object[]? data)
        {
            if (data == null || data.Length == 0)
            {
                return Request(tag, timeout, new EdenData());
            }
            else if (data.Length == 1)
            {
                return Request(tag, timeout, new EdenData(data[0]));
            }
            else
                return Request(tag, timeout, new EdenData(data));
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">Dictionary sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        public EdenData Request(string tag, int timeout, Dictionary<string, object>? data)
        {
            if (data == null)
                return Request(tag, timeout, new EdenData());
            else
                return Request(tag, timeout, new EdenData(data));
        }

        /// <summary>
        /// Request any data to server asynchronously and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">EdenData structured sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        public async Task<EdenData> RequestAsync(string tag, int timeout, EdenData data)
        {
            return await Task.Run(async () =>
            {
                tag = REQUEST_PREFIX + tag;
                if (!_responseEvents.ContainsKey(tag))
                    _responseEvents.Add(tag, null);
                bool result = await SendAsync(tag, data);
                if (result == false) 
                    return EdenData.Error("ERR:Request send failed");
                double time = 0;
                EdenData? rdata;
                do
                {
                    await Task.Delay(100);
                    time += 0.1;

                    if (_responseEvents.TryGetValue(tag, out rdata) && rdata != null)
                        break;

                } while (timeout > time);
                _responseEvents.Remove(tag);
                if (timeout <= time) return EdenData.Error("ERR:Request timeout");
                else return rdata!.Value;
            });
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        public async Task<EdenData> RequestAsync(string tag, int timeout, params object[]? data)
        {
            if (data == null || data.Length == 0)
            {
                return await RequestAsync(tag, timeout, new EdenData());
            }
            else if (data.Length == 1)
            {
                return await RequestAsync(tag, timeout, new EdenData(data[0]));
            }
            else
                return await RequestAsync(tag, timeout, new EdenData(data));
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">Dictionary sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        public async Task<EdenData> RequestAsync(string tag, int timeout, Dictionary<string, object>? data)
        {
            if (data == null)
                return await RequestAsync(tag, timeout, new EdenData());
            else
                return await RequestAsync(tag, timeout, new EdenData(data));

        }


        /// <summary>
        /// Request any data to server asynchronously and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="response">ReceiveEvent of same packet tag</param>
        /// <param name="data">EdenData structured sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        public void BeginRequest(string tag, int timeout, Action<EdenData> response, EdenData data)
        {
            Task.Run(() =>
            {
                tag = REQUEST_PREFIX + tag;
                if (!_responseEvents.ContainsKey(tag))
                    _responseEvents.Add(tag, null);
                bool result = Send(tag, data);
                if (result == false) 
                    response(EdenData.Error("ERR:Request send failed"));
                double time = 0;
                EdenData? rdata;
                do
                {
                    Thread.Sleep(100);
                    time += 0.1;

                    if (_responseEvents.TryGetValue(tag, out rdata) && rdata != null)
                        break;
                } while (timeout > time);

                _responseEvents.Remove(tag);
                if (timeout <= time) response(EdenData.Error("ERR:Request timeout"));
                else response(rdata!.Value);
            });
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="response">ReceiveEvent of same packet tag</param>
        /// <param name="data">EdenData structured sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        public void BeginRequest(string tag, int timeout, Action<EdenData> response, params object[]? data)
        {
            if (data == null || data.Length == 0)
            {
                BeginRequest(tag, timeout, response, new EdenData());
            }
            else if (data.Length == 1)
            {
                BeginRequest(tag, timeout, response, new EdenData(data[0]));
            }
            else
                BeginRequest(tag, timeout, response, new EdenData(data));
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="response">ReceiveEvent of same packet tag</param>
        /// <param name="data">EdenData structured sending data </param>
        /// <param name="timeout">timeout for response from server</param>
        public void BeginRequest(string tag, int timeout, Action<EdenData> response, Dictionary<string, object>? data)
        {
            if (data == null)
                BeginRequest(tag, timeout, response, new EdenData());
            else
                BeginRequest(tag, timeout, response, new EdenData(data));

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
        public void AddReceiveEvent(string tag, Action<EdenData> receiveEvent)
        {
            if (_receiveEvents.ContainsKey(tag))
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
        ///     callback run after client disconnected <br/>
        ///     arg1 = string client_id
        /// </param>
        public void SetServerDisconnectEvent(Action<string> callback)
        {
            _disconnectEvent = callback;
        }

        /// <summary>
        /// set null to the event activates when client connection close
        /// </summary>
        public void ReSetServerDisconnectEvent()
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
            int numberOfBytes = 0;
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
                _disconnectEvent?.Invoke(_serverId);
                return;
            }
            if (numberOfBytes == 0)// this happens when the client is disconnected
            {
                _logger?.Log($"Server disconnected. {_serverId}");
                Close();
                _disconnectEvent?.Invoke(_serverId);
                return;
            }

            int bytePointer = 0;
            while (bytePointer < numberOfBytes)
            {
                var packetLength = BitConverter.ToInt32(new ArraySegment<byte>(_readBuffer, bytePointer, 4));
                bytePointer += 4;
                byte[] jsonObject = (new ArraySegment<byte>(_readBuffer, bytePointer, packetLength)).ToArray();
                bytePointer += packetLength;

                try
                {
                    var packet = JsonSerializer.Deserialize<EdenPacket>(jsonObject, new JsonSerializerOptions { IncludeFields = true });
                    _logger?.Log($"Recv({_serverId}/{packetLength}Bytes) : [TAG] {packet.tag} [DATA] {packet.data.data}");

                    packet.data.CastJsonToType();

                    if (packet.tag.StartsWith(REQUEST_PREFIX))
                    {
                        if (_responseEvents.ContainsKey(packet.tag))
                        {
                            _responseEvents[packet.tag] = packet.data;
                        }
                        else
                        {
                            _logger?.Log($"Error! There is no packet tag {packet.tag} from {_serverId}");
                        }
                    }
                    else
                    {
                        if (_receiveEvents.TryGetValue(packet.tag, out var packetListenEvent))
                        {
                            packet.data.CastJsonToType();
                            try { packetListenEvent(packet.data); }
                            catch (Exception e) // Exception for every problem in PacketListenEvent
                            {
                                _logger?.Log($"Error! Error caught in ReceiveEvent : {packet.tag} | {_serverId} \n {e.Message}");
                            }
                        }
                        else
                        {
                            _logger?.Log($"Error! There is no packet tag {packet.tag} from {_serverId}");
                        }
                    }


                }
                catch (Exception e)
                {
                    _logger?.Log($"Error! Packet data is not JSON-formed on {_serverId}\n{e.Message}");
                }
            }
            if (_stream.CanRead)
                _stream.BeginRead(_readBuffer, 0, _readBuffer.Length, ReadBuffer, null);
            else
            {
                _logger?.Log($"Error! Cannot read network stream on server {_serverId}");
            }
        }
        
        #endregion
    }
}