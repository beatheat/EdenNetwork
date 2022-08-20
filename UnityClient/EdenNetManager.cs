using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

//EdenNetwork for unity & c# 9.0 
namespace EdenNetwork
{
    /// <summary>
    /// EdenPacketData
    /// </summary>
    public class EdenData
    {
        /// <summary>
        /// Enum Type of EdenData
        /// </summary>
        public enum Type { SINGLE, ARRAY, DICTIONARY }
        public Type type;
        public object data;
        [JsonIgnore]
        private object[] array_data = null;
        [JsonIgnore]
        private Dictionary<string, object> dict_data = null;

        /// <summary>
        /// Initialize structure by single null data
        /// </summary>
        public EdenData()
        {
            data = null;
            type = Type.SINGLE;
        }
        /// <summary>
        /// Initialize structure by single data
        /// </summary>
        public EdenData(object data)
        {
            this.data = data;
            type = Type.SINGLE;
        }
        /// <summary>
        /// Initialize structure by object array
        /// </summary>
        public EdenData(object[] data)
        {
            this.data = data;
            type = Type.ARRAY;
        }
        /// <summary>
        /// Initialize structure by dictionary
        /// </summary>
        public EdenData(Dictionary<string, object> data)
        {
            this.data = data;
            type = Type.DICTIONARY;
        }
        /// <summary>
        /// object json string to each EdenData.Type
        /// </summary>
        public void CastJsonToType()
        {
            if (data == null) return;
            if (type == Type.ARRAY)
            {
                array_data = ParseData<object[]>(data);
            }
            else if (type == Type.DICTIONARY)
            {
                dict_data = ParseData<Dictionary<string, object>>(data);
            }
        }
        /// <summary>
        /// Get single data
        /// </summary>
        /// <typeparam name="T">type to parse</typeparam>
        /// <returns>parsed data for type desired</returns>
        public T Get<T>()
        {
            if (type == Type.SINGLE)
            {
                if (data == null)
                    return default(T);
                return ParseData<T>(data);
            }
            throw new Exception("EdenData::Get() - data is not single data");
        }
        /// <summary>
        /// Get data by index from array object data
        /// </summary>
        /// <typeparam name="T">type to parse</typeparam>
        /// <param name="idx"> ndex desire</param>
        /// <returns>parsed data for type desired</returns>
        public T Get<T>(int idx)
        {
            if (array_data == null) throw new Exception("EdenData::Get(int idx) - data is null");
            if (type == Type.ARRAY)
            {
                if (idx < 0 || idx >= array_data.Length)
                {
                    throw new Exception("EdenData::Get(int idx) - out of index ");
                }
#pragma warning disable CS8603
                return ParseData<T>(array_data[idx]);
#pragma warning restore
            }
            throw new Exception("EdenData::Get(int idx) - data is not array");
        }
        /// <summary>
        /// Get data by key from dictionary data
        /// </summary>
        /// <typeparam name="T">type to parse</typeparam>
        /// <param name="key">key desire</param>
        /// <returns>parsed data for type desired</returns>
        public T Get<T>(string key)
        {
            if (dict_data == null) throw new Exception("EdenData::Get(string key) - data is null");
            if (type == Type.DICTIONARY)
            {
                object value;
                if (dict_data.TryGetValue(key, out value) == false)
                    throw new Exception("EdenData::Get(string tag) - there is no tag in data dictionary");
#pragma warning disable CS8603
                return ParseData<T>(value);
#pragma warning restore
            }
            throw new Exception("EdenData::Get(int idx) - data is not dictionary");
        }
        /// <summary>
        /// parse json data object to type desired
        /// </summary>
        /// <typeparam name="T">type to parse</typeparam>
        /// <param name="data">data object</param>
        /// <returns>parsed data for type desired</returns>
        public static T ParseData<T>(object data)
        {
            return JsonConvert.DeserializeObject<T>(data.ToString());
        }

    }

    /// <summary>
    /// Struct : packet sturcture for EdenNetwork
    /// </summary>
    public struct EdenPacket
    {
        public string tag;
        public EdenData data;
    }
    /// <summary>
    /// Enum for server connection statte
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
        private const int BUF_SIZE = 8 * 1024;

        private TcpClient tcpclient;
        private NetworkStream stream;
        private byte[] read_buffer;
        private string server_id;
        private Dictionary<string, Action<EdenData>> receive_events;
        private Action<string> disconn_event;
        private StreamWriter log_stream;
        private Thread log_thread;
        #endregion

        #region Unity Dependents

        #region UnityFields
        [SerializeField]
        private string IPAddress = "127.0.0.1";
        [SerializeField]
        private int port = 17676;
        [SerializeField]
        private bool Logging = false;
        [SerializeField]
        private string logPath = "EdenNetLog.txt";



        /// <summary>
        /// Struct to enqeue receive event cause of Unity Single Thread
        /// </summary>
        private struct SyncReceiveEventForm
        {
            public string tag;
            public Action<EdenData> receive_event;
            public EdenData data;
        }
        private Queue<SyncReceiveEventForm> receive_event_queue = new Queue<SyncReceiveEventForm>();

        public static EdenNetManager Instance { get; private set; }
        #endregion

        /// <summary>
        /// UnityAwake 
        /// </summary>
        private void Awake()
        {
            DontDestroyOnLoad(this);
            if(Logging)
                LoadLogStream();
            Instance = this;
        }

        /// <summary>
        /// UnityUpdate execute receive event if it is in queue
        /// </summary>
        public void Update()
        {
            while (receive_event_queue.Count > 0)
            {
                SyncReceiveEventForm form = receive_event_queue.Dequeue();
                try
                {
                    form.receive_event(form.data);
                }
                catch (Exception e)
                {
                    LogError("Some Error occurs in PacketListenEvent : " + form.tag + " | " + server_id + "\n" + e.Message);
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

        /// <summary>
        /// Load stream of log file
        /// </summary>
        private void LoadLogStream()
        {
            try
            {
                log_stream = new StreamWriter(logPath, append: true);
                log_thread = new Thread(() =>
                {
                    try
                    {
                        while (log_stream.BaseStream != null)
                        {
                            Thread.Sleep(3 * 60 * 1000);
                            log_stream.Flush();
                        }
                    }
                    catch //(ThreadInterruptedException e)
                    {
                        UnityDebugLogError("Log stream is closed");
                    }
                });
                log_thread.Start();
            }
            catch //(Exception e)
            {
                UnityDebugLogError("Cannot create log-file stream on " + logPath);
            }
        }

        #region Log Methods
        private void UnityDebugLog(string log)
        {
#if UNITY_EDITOR
            Debug.Log(log);
#endif
        }

        private void UnityDebugLogError(string log)
        {
#if UNITY_EDITOR
            Debug.LogError(log);
#endif
        }

        private void Log(string log)
        {
            UnityDebugLog(log);
            if(Logging)
                log_stream.WriteLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ff") + "] " + log);
        }

        private void LogError(string log)
        {
            UnityDebugLogError(log);
            if(Logging)
                log_stream.WriteLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ff") + "] " + log);
        }

        private void LogAsync(string log)
        {
            UnityDebugLog(log);
            if(Logging)
                log_stream.WriteLineAsync("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ff") + "] " + log);
        }

        #endregion
        #endregion

        #region Public Methods

        /// <summary>
        /// EdenNetwork manager for UNITY and C#9.0 
        /// </summary>
        public EdenNetManager()
        {
            receive_events = new Dictionary<string, Action<EdenData>>();
            read_buffer = new byte[BUF_SIZE];
            log_thread = null;
            log_stream = null;
            disconn_event = null;
            tcpclient = null;
            stream = null;
            server_id = null;
        }


        /// <summary>
        /// Connect to server by IP, port
        /// </summary>
        /// <param name="IPAddress">IP adress of server</param>
        /// <param name="port">port number of server</param>
        /// <param name="logPath">path for write network log</param>
        /// <returns>returns ConnectionState of server[OK, FULL, NOT_LISTENING, ERROR]</returns>
        public ConnectionState Connect(string IPAddress, int port, string logPath)
        {
            if (tcpclient != null)
                Close();
            try
            {
                tcpclient = new TcpClient(IPAddress, port);
                stream = tcpclient.GetStream();
                byte[] buffer = new byte[128];
                stream.Read(buffer);
                string server_state = Encoding.UTF8.GetString(buffer);
                if (String.Compare(server_state, "OK") == 0)
                {
                    UnityDebugLog("Connection success to " + IPAddress + ":" + port);
                }
                else if (String.Compare(server_state, "NOT LISTENING") == 0)
                {
                    UnityDebugLogError("Cannot connect to server : SERVER IS NOT LISTENING");
                    return ConnectionState.NOT_LISTENING;
                }
                else //if(server_state == "FULL")
                {
                    UnityDebugLogError("Cannot connect to server : SERVER IS FULL");
                    return ConnectionState.FULL;
                }
            }
#pragma warning disable CS0168
            catch (Exception e)
            {
                UnityDebugLogError("Cannot connect to server : " + e.Message);
                return ConnectionState.ERROR;
            }
#pragma warning restore CS0168

            server_id = IPAddress + ":" + port;
            stream.BeginRead(read_buffer, 0, read_buffer.Length, ReadBuffer, null);
            return ConnectionState.OK;
        }
        /// <summary>
        /// Connect to server by pre determined IP, port
        /// </summary>
        /// <returns>returns ConnectionState of server[OK, FULL, NOT_LISTENING, ERROR]</returns>
        public ConnectionState Connect()
        {
            return Connect(IPAddress, port, logPath);
        }

        #region Send Methods
        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">EdenData structured sending data </param>
        public bool Send(string tag, EdenData data)
        {
            if (stream.CanWrite)
            {
                EdenPacket packet = new EdenPacket();
                packet.tag = tag;
                packet.data = data;

                string json_packet = JsonConvert.SerializeObject(packet);
                byte[] bytes = Encoding.UTF8.GetBytes(json_packet);
                byte[] send_obj = BitConverter.GetBytes(bytes.Length);
                send_obj = send_obj.Concat(bytes).ToArray();

                if (send_obj.Length >= BUF_SIZE)
                {
                    LogError("Too big data to send, EdenNetProtocol support data size below " + BUF_SIZE);
                    return false;
                }
                stream.BeginWrite(send_obj, 0, send_obj.Length, (IAsyncResult ar) =>
                {
                    LogAsync((string)ar.AsyncState);
                }, server_id + " <==  Packet Len : " + bytes.Length.ToString() + " | Json Obj : " + json_packet);
                return true;
            }
            else
            {
                LogError("NetworkStream cannot write on server : " + server_id);
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
        #endregion

        #region SendAsync Methods
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="callback">callback function[bool success, object state] run after data send</param>
        /// <param name="state">parameter for callback function</param>
        /// <param name="data">EdenData structured sending data</param>
        public void SendAsync(string tag, Action<bool, object> callback, object state, EdenData data)
        {
            Task.Run(() => callback(Send(tag, data), state));
        }
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="callback">callback function[bool success, object state] run after data send</param>
        /// <param name="state">parameter for callback function</param>
        /// <param name="data">object array sending data</param>
        public void SendAsync(string tag, Action<bool, object> callback, object state, params object[] data)
        {
            if (data == null || data.Length == 0)
            {
                SendAsync(tag, callback, state, new EdenData());
            }
            else if (data.Length == 1)
            {
                SendAsync(tag, callback, state, new EdenData(data[0]));
            }
            else
                SendAsync(tag, callback, state, new EdenData(data));
        }
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="callback">callback function[bool success, object state] run after data send</param>
        /// <param name="state">parameter for callback function</param>
        /// <param name="data">dictionary array sending data</param>
        public void SendAsync(string tag, Action<bool, object> callback, object state, Dictionary<string, object> data)
        {
            if (data == null)
                SendAsync(tag, callback, state, new EdenData());
            else
                SendAsync(tag, callback, state, new EdenData(data));
        }
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">sending data</param>
        public void SendAsync(string tag, EdenData data)
        {
            Task.Run(() => Send(tag, data));
        }
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data</param>
        public void SendAsync(string tag, params object[] data)
        {
            if (data == null || data.Length == 0)
            {
                SendAsync(tag, new EdenData());
            }
            else if (data.Length == 1)
            {
                SendAsync(tag, new EdenData(data[0]));
            }
            else
                SendAsync(tag, new EdenData(data));
        }
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">dictionary sending data</param>
        public void SendAsync(string tag, Dictionary<string, object> data)
        {
            if (data == null)
                SendAsync(tag, new EdenData());
            else
                SendAsync(tag, new EdenData(data));
        }
        #endregion

        /// <summary>
        /// Append receive event which response for packet named with specific tag 
        /// </summary>
        /// <param name="tag">reactable tag name for packet received</param>
        /// <param name="receive_event">
        ///     event that processed by packet received <br/>
        ///     arg1 = EdenData data
        /// </param>
        public void AddReceiveEvent(string tag, Action<EdenData> receive_event)
        {
            receive_events.Add(tag, receive_event);
        }

        /// <summary>
        /// Remove receive event which response for packet name with specific tag
        /// </summary>
        /// <param name="tag">reactable tag name for packet received</param>
        public void RemoveReceiveEvent(string tag)
        {
            receive_events.Remove(tag);
        }

        /// <summary>
        /// Append event activates when client connection close
        /// </summary>
        /// <param name="DoAfterCloentDisconnect">
        ///     Action for do something after client disconnected <br/>
        ///     arg1 = string client_id
        /// </param>
        public void SetServerDisconnectEvent(Action<string> DoAfterServerDisconnect)
        {
            disconn_event = DoAfterServerDisconnect;
        }

        /// <summary>
        /// set null to the event activates when client connection close
        /// </summary>
        public void ReSetServerDisconnectEvent()
        {
            disconn_event = null;
        }

        /// <summary>
        /// Close server and release 
        /// </summary>
        public void Close()
        {
            if (tcpclient != null)
            {
                stream.Close();
                tcpclient.Close();
            }
            if (log_thread != null)
                log_thread.Interrupt();
            Log("Connection is closed");
            if (log_stream != null)
                log_stream.Close();

        }
        #endregion

        #region Private Methods

        /// <summary>
        /// Async method for read packet and tcp buffer
        /// </summary>
        /// <param name="ar">ar.AsyncState is EdenClient which sent data</param>
        private void ReadBuffer(IAsyncResult ar)
        {
            int numberofbytes = 0;
            try
            {
                numberofbytes = stream.EndRead(ar);
            }
#pragma warning disable CS0168
            catch (ObjectDisposedException e)
            {
                return; // may be connection is closed
            }
#pragma warning restore CS0168
            catch (Exception e)
            {
                LogError("Forced disconnection to server. " + server_id + "\n" + e.Message);
                stream.Close();
                tcpclient.Close();
                if (disconn_event != null)
                    disconn_event(server_id);
                return;
            }
            if (numberofbytes == 0)// this happens when the client is disconnected
            {
                Log("Server disconnected.");
                stream.Close();
                tcpclient.Close();
                if (disconn_event != null)
                    disconn_event(server_id);
                return;
            }

            int length = BitConverter.ToInt32(new ArraySegment<byte>(read_buffer, 0, 4));
            byte[] json_object = (new ArraySegment<byte>(read_buffer, 4, length)).ToArray();
            string json_string = Encoding.UTF8.GetString(json_object);

            LogAsync(server_id + " ==> " + "Json Obj : " + "Packet Len : " + length.ToString() + " | " + json_string);

            EdenPacket packet;
            try
            {
                packet = JsonConvert.DeserializeObject<EdenPacket>(json_string);

                Action<EdenData> PacketListenEvent;
                packet.data.CastJsonToType();

                if (receive_events.TryGetValue(packet.tag, out PacketListenEvent))
                {
                    SyncReceiveEventForm form = new SyncReceiveEventForm();
                    form.tag = packet.tag;
                    form.receive_event = PacketListenEvent;
                    form.data = packet.data;
                    receive_event_queue.Enqueue(form);
                }
                else
                {
                    LogError("EdenNet-Error::There is no packet tag <" + packet.tag + "> from " + server_id);
                    return;
                }
            }
            catch (Exception e)
            {
                LogError("Packet data is not JSON-formed on " + server_id + "\n" + e.Message);
            }

            if (stream.CanRead)
                stream.BeginRead(read_buffer, 0, read_buffer.Length, ReadBuffer, null);
            else
            {
                LogError("NetworkStream cannot read on server : " + server_id);
            }
        }

        #endregion
    }
}