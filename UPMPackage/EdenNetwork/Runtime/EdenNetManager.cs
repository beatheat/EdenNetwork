using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;
using System.Threading;
using Cysharp.Threading.Tasks;
using static EdenNetwork.Constant;

//EdenNetwork for unity & c# 9.0 
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
    public class EdenNetManager : MonoBehaviour
    {
        #region Fields

        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private byte[] _readBuffer;
        private int _bufferSize;
        private string _serverId;
        private readonly Dictionary<string, Action<EdenData>> _receiveEvents;
        private readonly Dictionary<string, EdenData> _responseEvents;
        private Action<string> _disconnectEvent;

        private Logger _logger;

        private bool _startReadObject;
        private int _packetLengthBufferPointer;
        private readonly byte[] _packetLengthBuffer;
        private byte[] _dataObjectBuffer;
        private int _dataObjectBufferPointer;
        
        #endregion

        #region Unity Dependents

        #region UnityFields

        [SerializeField]
        public string IPAddress;
        [SerializeField]
        public int port;
        [SerializeField]
        private bool Logging = true;
        [SerializeField]
        private string LogPath = "EdenNetworkLog.txt";

        /// <summary>
        /// Struct to  synchronize receive event in Unity Main Thread
        /// </summary>
        private struct SyncReceiveEventForm
        {
            public string tag;
            public Action<EdenData> receiveEvent;
            public EdenData data;
        }

        /// <summary>
        /// Struct to synchronize response event in Unity Main Thread
        /// </summary>
        private struct SyncResponseEventForm
        {
            public string tag;
            public Action<EdenData> responseEvent;
            public float startTime;
            public int timeout;
        }

        /// <summary>
        /// Struct to  synchronize connect event in Unity Main Thread
        /// </summary>
        private struct SyncConnectEventForm
        {
            public Action<ConnectionState> connectionEvent;
            public ConnectionState state;
        }

        /// <summary>
        /// Struct to  synchronize send event in Unity Main Thread
        /// </summary>
        private struct SyncSendEventForm
        {
            public Action<bool> sendEvent;
            public bool success;
        }
        
        private SyncConnectEventForm? _connectEvent = null;
        private readonly Queue<SyncSendEventForm> _sendEventQueue = new Queue<SyncSendEventForm>();
        private readonly Queue<SyncReceiveEventForm> _receiveEventQueue = new Queue<SyncReceiveEventForm>();
        private readonly List<SyncResponseEventForm> _responseEventList = new List<SyncResponseEventForm>();

        public static EdenNetManager Instance { get; private set; }
        #endregion

        /// <summary>
        /// UnityAwake 
        /// </summary>
        private void Awake()
        {
            DontDestroyOnLoad(this);
            Instance = this;
            if (Logging)
            {
                try
                {
                    _logger = new Logger(LogPath, "EdenNetwork");
                }
                catch (Exception)
                { 
                    Debug.LogError("[EdenNetwork] Cannot load log path");
                }
            }
        }

        /// <summary>
        /// UnityUpdate execute receive event if it is in queue
        /// </summary>
        public void Update()
        {
            //connection
            if(_connectEvent != null)
            {
                var connectEvent = _connectEvent.Value;
                _connectEvent = null;
                try
                {
                    connectEvent.connectionEvent(connectEvent.state);
                }
                catch (Exception e)
                {
                    _logger?.LogError($"Error! Error occurs in connect event : {e.Message}");
                }
            }

            //receive event
            while (_receiveEventQueue.Count > 0)
            {
                SyncReceiveEventForm form = _receiveEventQueue.Dequeue();
                try
                {
                    form.receiveEvent(form.data);
                }
                catch (Exception e)
                {
                    _logger?.LogError("Error! Error occurs in receive event : " + form.tag + " | " + _serverId + "\n" + e.Message);
                }
            }

            //send event
            while(_sendEventQueue.Count > 0)
            {
                SyncSendEventForm form = _sendEventQueue.Dequeue();
                try
                {
                    form.sendEvent(form.success);
                }
                catch(Exception e)
                {
                    _logger?.LogError("Error! Error occurs in send callback : " + _serverId + "\n" + e.Message);
                }
            }

            //response event
            for (int i = 0; i < _responseEventList.Count; i++)
            {
                SyncResponseEventForm form = _responseEventList[i];
                if (Time.unscaledTime - form.startTime > form.timeout)
                {
                    try
                    {
                        form.responseEvent(EdenData.Error("Response timeout"));
                    }
                    catch (Exception e)
                    {
                        _logger?.LogError("Error! Error occurs in response event : " + _serverId + "\n" + e.Message);
                    }
                    finally
                    {
                        _responseEvents.Remove(form.tag);
                        _responseEventList.Remove(_responseEventList[i--]);
                    }
                    continue;
                }
                if (_responseEvents.TryGetValue(form.tag, out var data) && data != null)
                {
                    try
                    {
                        form.responseEvent(data);
                    }
                    catch (Exception e)
                    {
                        _logger?.LogError("Error! Error occurs in response event : " + _serverId + "\n" + e.Message);
                    }
                    finally
                    {
                        _responseEvents.Remove(form.tag);
                        _responseEventList.Remove(_responseEventList[i--]);
                    }
                }
            }
        }

        /// <summary>
        /// Unity OnDestroy
        /// </summary>
        public void OnDestroy()
        {
            Instance.Close();
        }
        
        #endregion

        #region Public Methods

        /// <summary>
        /// EdenNetwork manager for UNITY and C#9.0 
        /// </summary>
        public EdenNetManager()
        {
            _receiveEvents = new Dictionary<string, Action<EdenData>>();
            _responseEvents = new Dictionary<string, EdenData>();
            _readBuffer = new byte[DEFAULT_BUFFER_SIZE];
            _disconnectEvent = null;
            _tcpClient = null;
            _stream = null;
            _serverId = null;
            _logger = null;
            
            _startReadObject = false;
            _packetLengthBufferPointer = 0;
            _packetLengthBuffer = new byte[PACKET_LENGTH_BUFFER_SIZE];

            _dataObjectBuffer = Array.Empty<byte>();
            _dataObjectBufferPointer = 0;
        }

        /// <summary>
        /// Set data read buffer size in bytes, default value is 8192 Bytes
        /// </summary>
        /// <param name="size">buffer size in bytes</param>
        public void SetBufferSize(int size)
        {
            Array.Resize(ref _readBuffer, size);
        }
        
        /// <summary>
        /// Connect to server by IP, port
        /// </summary>
        /// <param name="ipAddress">IP address of server</param>
        /// <param name="port">port number of server</param>
        /// <returns>returns ConnectionState of server[OK, FULL, NOT_LISTENING, ERROR]</returns>
        public ConnectionState Connect(string ipAddress, int port)
        {
            if (_tcpClient != null)
                Close();
            try
            {
                _tcpClient = new TcpClient(ipAddress, port);
                _stream = _tcpClient.GetStream();
                byte[] buffer = new byte[128];
                _stream.Read(buffer);
                ConnectionState serverState = (ConnectionState)BitConverter.ToInt32(buffer);
                if (serverState != ConnectionState.OK)
                {
                    _logger.LogError($"Cannot connect to server : SERVER IS {serverState.ToString()}");
                    _stream.Close();
                    _tcpClient.Close();
                    return serverState;
                }
            }
            catch (Exception e)
            {
                _logger?.LogError($"Cannot connect to server : {e.Message}");
                return ConnectionState.ERROR;
            }
            try
            {
                _stream.BeginRead(_readBuffer, 0, _readBuffer.Length, ReadBuffer, null);
                _logger?.Log($"Connection success to {ipAddress}:{port}");
                _serverId = ipAddress + ":" + port;
            }
            catch(Exception e)
            {
                _logger?.LogError($"Error! Cannot read network stream : " + e.Message);
                Close();
                return ConnectionState.ERROR;
            }
            return ConnectionState.OK;
        }
        /// <summary>
        /// Connect to server by pre determined IP, port
        /// </summary>
        /// <returns>returns ConnectionState of server[OK, FULL, NOT_LISTENING, ERROR]</returns>
        public ConnectionState Connect()
        {
            return Connect(IPAddress, port);
        }

        /// <summary>
        /// Connect to server asynchronously by IP, port
        /// </summary>
        /// <param name="ipAddress">IP address of server</param>
        /// <param name="port">port number of server</param>
        /// <param name="callback">callback method execute after connection success or fail</param>
        public void BeginConnect(string ipAddress, int port, Action<ConnectionState> callback)
        {
            UniTask.RunOnThreadPool(() =>
            {
                if (_tcpClient != null)
                    Close();
                _serverId = IPAddress + ":" + this.port;
                _tcpClient = new TcpClient(AddressFamily.InterNetwork);
                try
                {
                    _tcpClient.Connect(ipAddress, port);
                    _stream = _tcpClient.GetStream();

                    byte[] buffer = new byte[128];
                    _stream.Read(buffer);
                    ConnectionState serverState = (ConnectionState)BitConverter.ToInt32(buffer);
                    if (serverState != ConnectionState.OK)
                    {
                        _logger?.LogError($"Cannot connect to server : SERVER IS {serverState.ToString()}");
                        _stream.Close();
                        _tcpClient.Close();
                        callback(serverState);
                        return;
                    }
                }
                catch
                {
                    _logger?.LogError("Cannot connect to server");
                    callback(ConnectionState.ERROR);
                    return;
                }
                try
                {
                    _stream.BeginRead(_readBuffer, 0, _readBuffer.Length, ReadBuffer, null);
                    _logger?.Log($"Connection success to {ipAddress}:{port}");
                    _serverId = ipAddress + ":" + port;
                }
                catch(Exception e)
                {
                    _logger?.LogError($"Error! Cannot read network stream : " + e.Message);
                    Close();
                    callback(ConnectionState.ERROR);
                    return;
                }
                callback(ConnectionState.OK);
            });

        }
        /// <summary>
        /// Connect to server asynchronously by IP, port
        /// </summary>
        /// <param name="callback">callback method execute after connection success or fail</param>
        /// <param name="timeout">timeout by seconds</param>
        public void BeginConnect(Action<ConnectionState> callback)
        {
            BeginConnect(IPAddress, port, callback);
        }

        /// <summary>
        /// Connect to server by IP, port
        /// </summary>
        /// <param name="ipAddress">IP address of server</param>
        /// <param name="port">port number of server</param>
        /// <returns>returns ConnectionState of server[OK, FULL, NOT_LISTENING, ERROR]</returns>
        public async UniTask<ConnectionState> ConnectAsync(string ipAddress, int port)
        {
            return await UniTask.RunOnThreadPool(() =>
            {
                if (_tcpClient != null)
                    Close();
                try
                {
                    _tcpClient = new TcpClient(AddressFamily.InterNetwork);

                    _tcpClient.Connect(ipAddress, port);
                    _stream = _tcpClient.GetStream();
                    byte[] buffer = new byte[128];
                    _stream.Read(buffer);
                    ConnectionState serverState = (ConnectionState) BitConverter.ToInt32(buffer);
                    if (serverState != ConnectionState.OK)
                    {
                        _logger?.LogError($"Cannot connect to server : SERVER IS {serverState.ToString()}");
                        _stream.Close();
                        _tcpClient.Close();
                        return serverState;
                    }
                }
                catch (Exception e)
                {
                    _logger?.LogError($"Cannot connect to server : {e.Message}");
                    return ConnectionState.ERROR;
                }

                try
                {
                    _stream.BeginRead(_readBuffer, 0, _readBuffer.Length, ReadBuffer, null);
                    _logger?.Log($"Connection success to {ipAddress}:{port}");
                    _serverId = ipAddress + ":" + port;
                }
                catch(Exception e)
                {
                    _logger?.LogError($"Error! Cannot read network stream : " + e.Message);
                    Close();
                    return ConnectionState.ERROR;
                }
                return ConnectionState.OK;
            });
        }
        /// <summary>
        /// Connect to server by pre determined IP, port
        /// </summary>
        /// <returns>returns ConnectionState of server[OK, FULL, NOT_LISTENING, ERROR]</returns>
        public async UniTask<ConnectionState> ConnectAsync()
        {
            return await ConnectAsync(IPAddress, port);
        }

        #region Request Methods
        /// <summary>
        /// Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">EdenData structured sending data </param>
        /// <param name="timeout">timeout for response from server(seconds)
        /// </param>
        public EdenData Request(string tag, int timeout, EdenData data)
        {
            tag = REQUEST_PREFIX + tag;
            if (!_responseEvents.ContainsKey(tag))
                _responseEvents.Add(tag, null);
            bool result = Send(tag, data);
            if (result == false) 
                return EdenData.Error("ERR:Request send failed");
            double time = 0;
            EdenData responseData;
            do
            {
                Thread.Sleep(100);
                time += 0.1;

                if (_responseEvents.TryGetValue(tag, out responseData) && responseData != null)
                    break;

            } while (timeout > time);
            _responseEvents.Remove(tag);
            if (timeout <= time) 
                return EdenData.Error("ERR:Request timeout");
            return responseData;
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data </param>
        /// <param name="timeout">timeout for response from server(seconds)
        public EdenData Request(string tag, int timeout, params object[] data)
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
        /// <param name="timeout">timeout for response from server
        public EdenData Request(string tag, int timeout, Dictionary<string, object> data)
        {
            if (data == null)
                return Request(tag, timeout, new EdenData(data));
            else
                return Request(tag, timeout, new EdenData());
        }




        /// <summary>
        /// Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">EdenData structured sending data </param>
        /// <param name="timeout">timeout for response from server(seconds)
        /// </param>
        public async UniTask<EdenData> RequestAsync(string tag, int timeout, EdenData data)
        {
            return await UniTask.RunOnThreadPool(async () =>
            {
                tag = REQUEST_PREFIX + tag;
                if (!_responseEvents.ContainsKey(tag))
                    _responseEvents.Add(tag, null);
                bool result = await SendAsync(tag, data);
                if (result == false) 
                    return EdenData.Error("ERR:Request send failed");
                double time = 0;
                EdenData responseData;
                do
                {
                    await UniTask.Delay(100);
                    time += 0.1;

                    if (_responseEvents.TryGetValue(tag, out responseData) && responseData != null)
                        break;

                } while (timeout > time);
                _responseEvents.Remove(tag);
                if (timeout <= time) return EdenData.Error("ERR:Request timeout");
                else return responseData;
            });
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data </param>
        /// <param name="timeout">timeout for response from server(seconds)
        public async UniTask<EdenData> RequestAsync(string tag, int timeout, params object[] data)
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
        /// <param name="timeout">timeout for response from server
        public async UniTask<EdenData> RequestAsync(string tag, int timeout, Dictionary<string, object> data)
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
        /// <param name="timeout">timeout for request(seconds)</param>
        /// <param name="response">ReceiveEvent of same packet tag</param>
        /// <param name="data">EdenData structured sending data </param>
        public void BeginRequest(string tag, int timeout, Action<EdenData> response, EdenData data)
        {
            UniTask.RunOnThreadPool(() =>
            {
                tag = REQUEST_PREFIX + tag;
                SyncResponseEventForm form = new SyncResponseEventForm {responseEvent = response, tag = tag, timeout = timeout, startTime = Time.unscaledTime};
                _responseEventList.Add(form);
                _responseEvents.TryAdd(tag, null);
                Send(tag, data);
            });
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="timeout">timeout for request(seconds)</param>
        /// <param name="data">object array sending data </param>
        /// <param name="response">timeout for response from server</param>
        public void BeginRequest(string tag, int timeout, Action<EdenData> response, params object[] data)
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
        /// <param name="timeout">timeout for request(seconds)</param>
        /// <param name="data">Dictionary sending data </param>
        /// <param name="response">timeout for response from server
        public void BeginRequest(string tag, int timeout, Action<EdenData> response, Dictionary<string, object> data)
        {
            if (data == null)
                BeginRequest(tag, timeout, response, new EdenData());
            else
                BeginRequest(tag, timeout, response, new EdenData(data));

        }

        #endregion

        #region Send Methods
        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">EdenData structured sending data </param>
        public bool Send(string tag, EdenData data)
        {
            if (_stream.CanWrite)
            {
                EdenPacket packet = new EdenPacket {tag = tag, data = data};

                string jsonPacket = JsonConvert.SerializeObject(packet);
                byte[] bytes = Encoding.UTF8.GetBytes(jsonPacket);
                byte[] sendObj = BitConverter.GetBytes(bytes.Length);
                sendObj = sendObj.Concat(bytes).ToArray();

                if (sendObj.Length >= _readBuffer.Length)
                {
                    _logger?.LogError($"Error! Send - Too big data to send once, EdenNetProtocol support size under ({_readBuffer.Length})KB");
                    return false;
                }

                _stream.Write(sendObj, 0, sendObj.Length);
                _logger?.Log($"Send({_serverId}/{bytes.Length,4}B) : [TAG] {tag} [DATA] {JsonConvert.SerializeObject(data.data)}");                
                return true;
            }
            else
            {
                _logger.LogError("Error! Send - networkStream cannot write on server : " + _serverId);
                return false;
            }
        }
        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data </param>
        public bool Send(string tag, params object[] data)
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
        public bool Send(string tag, Dictionary<string, object> data)
        {
            if (data == null)
            { 
                return Send(tag, new EdenData());
            }
            else
                return Send(tag, new EdenData(data));
        }

        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="callback">callback function[bool success] run after data send</param>
        /// <param name="data">EdenData structured sending data</param>
        public void BeginSend(string tag, Action<bool> callback, EdenData data)
        {
            UniTask.RunOnThreadPool(() => 
            {
                SyncSendEventForm form = new SyncSendEventForm();
                form.success = Send(tag, data);
                callback ??= (bool x) => { };
                form.sendEvent = callback;
                _sendEventQueue.Enqueue(form);
            });
        }
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="callback">callback function[bool success] run after data send</param>
        /// <param name="state">parameter for callback function</param>
        /// <param name="data">object array sending data</param>
        public void BeginSend(string tag, Action<bool> callback, params object[] data)
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
        public void BeginSend(string tag, Action<bool> callback, Dictionary<string, object> data)
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
        public async UniTask<bool> SendAsync(string tag, EdenData data)
        {
            return await UniTask.RunOnThreadPool(async () =>
            {
                if (_stream is {CanWrite: true})
                {
                    EdenPacket packet = new EdenPacket {tag = tag, data = data};

                    string jsonPacket = JsonConvert.SerializeObject(packet);
                    byte[] bytes = Encoding.UTF8.GetBytes(jsonPacket);
                    byte[] sendObj = BitConverter.GetBytes(bytes.Length);
                    sendObj = sendObj.Concat(bytes).ToArray();

                    if (sendObj.Length >= _readBuffer.Length)
                    {
                        _logger?.LogError($"Error! SendAsync - Too big data to send once, EdenNetProtocol support data size under ({_readBuffer.Length})");
                        return false;
                    }
                    await _stream.WriteAsync(sendObj, 0, sendObj.Length);
                    _logger?.Log($"Send({_serverId}/{bytes.Length,4}B) : [TAG] {tag} [DATA] {JsonConvert.SerializeObject(data.data)}");
                    return true;
                }
                else
                {
                    _logger?.LogError($"Error! SendAsync - NetworkStream cannot write on server");
                    return false;
                }
            });
        }
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data</param>
        public async UniTask<bool> SendAsync(string tag, params object[] data)
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
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">dictionary sending data</param>
        public async UniTask<bool> SendAsync(string tag, Dictionary<string, object> data)
        {
            if (data == null)
                return await SendAsync(tag, new EdenData());
            else
                return await SendAsync(tag, new EdenData(data));
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
                _logger?.LogError($"Error! AddReceiveEvent - receive event tag({tag}) already exists");
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
                _logger?.LogError($"Error! RemoveReceiveEvent - receive event tag({tag}) does not exist");
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
                numberOfBytes = _stream.EndRead(ar);
            }
            catch (ObjectDisposedException)
            {
                return; // may be connection is closed
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
                var jsonString = Encoding.UTF8.GetString(jsonObject);
                try
                {
                    var packet = JsonConvert.DeserializeObject<EdenPacket>(jsonString, new JsonSerializerSettings{TypeNameHandling = TypeNameHandling.All});
                    _logger?.Log($"Recv({_serverId}/{ jsonObject.Length,4}B) : [TAG] {packet.tag} [DATA] {packet.data.data}");

                    packet.data.CastJsonToType();
                    //Process response
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
                    //Process receive
                    else
                    {
                        if (_receiveEvents.TryGetValue(packet.tag, out var packetListenEvent))
                        {
                            SyncReceiveEventForm form = new SyncReceiveEventForm {tag = packet.tag, receiveEvent = packetListenEvent, data = packet.data};
                            _receiveEventQueue.Enqueue(form);
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

        }

        #endregion
    }
}