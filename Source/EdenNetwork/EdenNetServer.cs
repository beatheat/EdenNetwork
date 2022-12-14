using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

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
            public EdenClient(TcpClient _cli, string _id)
            {
                tcpclient = _cli;
                stream = _cli.GetStream();
                id = _id;
                read_buffer = new byte[BUF_SIZE];
            }

            public TcpClient tcpclient;
            public NetworkStream stream;
            public string id;
            public byte[] read_buffer;
        };
        #endregion

        #region Fields

        /// <summary>
        /// Constant : constant for infinite listen number of client
        /// </summary>
        public static readonly int INF_CLIENT_ACCEPT = -1;
        /// <summary>
        /// Constant : size of read buffer for each client
        /// </summary>
        private const int BUF_SIZE = 8 * 1024;

        private TcpListener server;
        private Dictionary<string, EdenClient> clients;
        private Dictionary<string, Action<string, EdenData>> receive_events;
        private bool is_listening;
        private StreamWriter log_stream;
        private Action<string>? accept_event;
        private Action<string>? disconn_event;
        private int max_accept_num;
        private bool print_log;
        private Thread log_thread;

        #endregion

        #region Public Methods
        #region Constructor
        /// <summary>
        /// Constructor for EdenNetSerer
        /// </summary>
        /// <param name="ipv4_address">IPv4 address to open server</param>
        /// <param name="port">Port number to open server</param>
        /// <param name="log_path">Path for log data to wrtie</param>
        /// <exception cref="Exception">
        /// Two case of Exception exists
        /// case 1. Cannot create tcplistener 
        /// case 2. Cannot open stream for log file path
        /// </exception>
        public EdenNetServer(string ipv4_address, int port, string log_path)
        {
            try
            {
                server = new TcpListener(IPAddress.Parse(ipv4_address), port);
            }
            catch (Exception e)
            {
                throw new Exception("Cannot create TcpListener on " + ipv4_address + ":" + port + "\n" + e.Message);
            }
            clients = new Dictionary<string, EdenClient>();
            receive_events = new Dictionary<string, Action<string, EdenData>>();
            is_listening = false;
            accept_event = null;
            disconn_event = null;

            print_log = log_path == "" ? false : true;
            if (print_log)
            {
                try
                {
                    log_stream = new StreamWriter(log_path, append: true);
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
                            Console.WriteLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ff") + "|EdenNetServer]" + "Log stream is closed");
                        }
                    });
                    log_thread.Start();
                }
                catch (Exception e)
                {
                    throw new Exception("EdenNetServer::Cannot create log-file stream on " + log_path + "\n" + e.Message);
                }
            }

        }
        /// <summary>
        /// Constructor for EdenNetSerer
        /// </summary>
        /// <param name="ipv4_address">IPv4 address to open server</param>
        /// <param name="port">Port number to open server</param>
        ///         /// <exception cref="Exception">
        /// Two case of Exception exists
        /// case 1. Cannot create tcplistener 
        /// case 2. Cannot open stream for log file path
        /// </exception>
        public EdenNetServer(string ipv4_address, int port) : this(ipv4_address, port, "") { }
        /// <summary>
        /// Constructor for EdenNetSerer
        /// </summary>
        /// <param name="port">Port number to open server</param>
        /// <param name="log_path">Path for log data to wrtie</param>
        ///         /// <exception cref="Exception">
        /// Two case of Exception exists
        /// case 1. Cannot create tcplistener 
        /// case 2. Cannot open stream for log file path
        /// </exception>
        public EdenNetServer(int port, string log_path) : this("0.0.0.0", port, log_path) { }
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
        /// <param name="max_accept_num">max number of clients to accept</param>
        /// <param name="DoAfterClientAccept">  
        ///     Action for do something after client accepted <br/>
        ///     arg1 = string client_id
        /// </param>
        public void Listen(int max_accept_num, Action<string> DoAfterClientAccept)
        {
            server.Start();
            this.max_accept_num = max_accept_num;
            is_listening = true;
            var ip_info = (IPEndPoint)server.LocalEndpoint;
            accept_event = DoAfterClientAccept;
            server.BeginAcceptTcpClient(Listening, null);

            Log("Eden Server is listening on  " + ip_info.ToString());
        }
        /// <summary>
        /// Listen and wait for client connection
        /// </summary>
        /// <param name="DoAfterClientAccept">  
        ///     Action for do something after client accepted <br/>
        ///     arg1 = string client_id
        /// </param>
        public void Listen(Action<string> DoAfterClientAccept)
        {
            Listen(INF_CLIENT_ACCEPT, DoAfterClientAccept);
        }
        /// <summary>
        /// Listen and wait for client connection
        /// </summary>
        /// <param name="max_accept_num">max number of clients to accept</param>
        public void Listen(int max_accept_num)
        {
            Listen(max_accept_num, (string client_id) => { });
        }
        /// <summary>
        /// Listen and wait for client connection
        /// </summary>
        public void Listen()
        {
            Listen(INF_CLIENT_ACCEPT, (string client_id) => { });
        }

        /// <summary>
        /// Stop Listening and waiting for client connection 
        /// </summary>
        public void StopListen()
        {
            is_listening = false;
        }

        /// <summary>
        /// Append receive event which response for packet named with specific tag 
        /// </summary>
        /// <param name="tag">reactable tag name for packet received</param>
        /// <param name="receive_event">
        ///     event that processed by packet received <br/>
        ///     arg1 = string client_id <br/>
        ///     arg2 = EdenData data
        /// </param>
        public void AddReceiveEvent(string tag, Action<string, EdenData> receive_event)
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
        public void SetClientDisconnectEvent(Action<string> DoAfterClientDisconnect)
        {
            disconn_event = DoAfterClientDisconnect;
        }

        /// <summary>
        /// set null to the event activates when client connection close
        /// </summary>
        public void ResetClientDisconnectEvent()
        {
            disconn_event = null;
        }

        /// <summary>
        /// Close server and release 
        /// </summary>
        public void Close()
        {
            StopListen();
            foreach (var client in clients.Values)
            {
                client.stream.Close();
                client.tcpclient.Close();
            }
            log_thread.Interrupt();
            server.Stop();
            Log("Server is closed");
            log_stream.Close();
        }

        #region Send Methods
        /// <summary>
        /// Send data json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="client_id">client id for send</param>
        /// <param name="data">EdenData structured sending data </param>
        public bool Send(string tag, string client_id, EdenData data)
        {
            NetworkStream stream = clients[client_id].stream;
            if (stream.CanWrite)
            {
                EdenPacket packet = new EdenPacket();
                packet.tag = tag;
                packet.data = data;

                string json_packet = JsonSerializer.Serialize(packet, new JsonSerializerOptions { IncludeFields = true });
                byte[] bytes = Encoding.UTF8.GetBytes(json_packet);
                byte[] send_obj = BitConverter.GetBytes(bytes.Length);
                send_obj = send_obj.Concat(bytes).ToArray();
                if (send_obj.Length >= BUF_SIZE) // Exception for too big data size
                {
                    Log("Too big data to send, EdenNetProtocol support data size below " + BUF_SIZE);
                    return false;
                }
                // Begin sending packet
                stream.BeginWrite(send_obj, 0, send_obj.Length, (IAsyncResult ar) =>
                {
#pragma warning disable CS8600, CS8604
                    //ar.AsyncState cannot be null
                    LogAsync((string)ar.AsyncState);
#pragma warning restore CS8600, CS8604
                }, client_id + " <==  Packet Len : " + bytes.Length.ToString() + " | Json Obj : " + json_packet);
                return true;
            }
            else // Exception for network stream write is not ready
            {
                Log("NetworkStream cannot write on client_id : " + clients[client_id].id);
                return false;
            }
        }
        /// <summary>
        /// Send data json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="client_id">client id for send</param>
        /// <param name="data">object array sending data </param>
        public bool Send(string tag, string client_id, params object[] data)
        {
            if (data == null || data.Length == 0)
            {
                return Send(tag, client_id, new EdenData());
            }
            else if (data.Length == 1)
            {
                return Send(tag, client_id, new EdenData(data[0]));
            }
            else
                return Send(tag, client_id, new EdenData(data));
        }
        /// <summary>
        /// Send data json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="client_id">client id for send</param>
        /// <param name="data">dictionary sending data </param>
        public bool Send(string tag, string client_id, Dictionary<string, object> data)
        {
            if (data == null)
                return Send(tag, client_id, new EdenData());
            else
                return Send(tag, client_id, new EdenData(data));
        }

        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">Edendata structured sending data</param>
        public bool Broadcast(string tag, EdenData data)
        {
            foreach (var client in clients.Values)
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
        public bool Broadcast(string tag, params object[] data)
        {
            if (data == null || data.Length == 0)
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
        public bool Broadcast(string tag, Dictionary<string, object> data)
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
        public bool BroadcastExcept(string tag, string client_id, EdenData data)
        {
            foreach (var client in clients.Values)
            {
                if (client_id == client.id)
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
        public bool BroadcastExcept(string tag, string client_id, params object[] data)
        {
            if (data == null || data.Length == 0)
            {
                return BroadcastExcept(tag, client_id, new EdenData());
            }
            else if (data.Length == 1)
            {
                return BroadcastExcept(tag, client_id, new EdenData(data[0]));
            }
            else
                return BroadcastExcept(tag, client_id, new EdenData(data));
        }
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">dictionary sending data</param>
        public bool BroadcastExcept(string tag, string client_id, Dictionary<string, object> data)
        {
            if (data == null)
                return BroadcastExcept(tag, client_id, new EdenData());
            else
                return BroadcastExcept(tag, client_id, new EdenData(data));
        }
        #endregion

        #region AsyncSend Methods
        /// <summary>
        /// Send data asynchronously json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="client_id">client id for send</param>
        /// <param name="callback">callback function[bool success, object state] run after data send</param>
        /// <param name="state">parameter for callback function</param>
        /// <param name="data">EdenData structured sending data</param>
        public void SendAsync(string tag, string client_id, Action<bool, object> callback, object state, EdenData data)
        {
            Task.Run(() => callback(Send(tag, client_id, data), state));
        }
        /// <summary>
        /// Send data asynchronously json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="client_id">client id for send</param>
        /// <param name="callback">callback function[bool success, object state] run after data send</param>
        /// <param name="state">parameter for callback function</param>
        /// <param name="data">object array sending data</param>
        public void SendAsync(string tag, string client_id, Action<bool, object> callback, object state, params object[] data)
        {
            if (data == null || data.Length == 0)
            {
                SendAsync(tag, client_id, callback, state, new EdenData());
            }
            else if (data.Length == 1)
            {
                SendAsync(tag, client_id, callback, state, new EdenData(data[0]));
            }
            else
                SendAsync(tag, client_id, callback, state, new EdenData(data));
        }
        /// <summary>
        /// Send data asynchronously json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="client_id">client id for send</param>
        /// <param name="callback">callback function[bool success, object state] run after data send</param>
        /// <param name="state">parameter for callback function</param>
        /// <param name="data">dictionary array sending data</param>
        public void SendAsync(string tag, string client_id, Action<bool, object> callback, object state, Dictionary<string, object> data)
        {
            if (data == null)
                SendAsync(tag, client_id, callback, state, new EdenData());
            else
                SendAsync(tag, client_id, callback, state, new EdenData(data));
        }
        /// <summary>
        /// Send data asynchronously json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="client_id">client id for send</param>
        /// <param name="data">sending data</param>
        public void SendAsync(string tag, string client_id, EdenData data)
        {
            Task.Run(() => Send(tag, client_id, data));
        }
        /// <summary>
        /// Send data asynchronously json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="client_id">client id for send</param>
        /// <param name="data">object array sending data</param>
        public void SendAsync(string tag, string client_id, params object[] data)
        {
            if (data == null || data.Length == 0)
            {
                SendAsync(tag, client_id, new EdenData());
            }
            else if (data.Length == 1)
            {
                SendAsync(tag, client_id, new EdenData(data[0]));
            }
            else
                SendAsync(tag, client_id, new EdenData(data));
        }
        /// <summary>
        /// Send data asynchronously json format to specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="client_id">client id for send</param>
        /// <param name="data">dictionary sending data</param>
        public void SendAsync(string tag, string client_id, Dictionary<string, object> data)
        {
            if (data == null)
                SendAsync(tag, client_id, new EdenData());
            else
                SendAsync(tag, client_id, new EdenData(data));
        }

        /// <summary>
        /// Broadcast data asynchronously to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="callback">callback function[bool success, object state] run after data send</param>
        /// <param name="state">parameter for callback function</param>
        /// <param name="data">sending data</param>
        public void BroadcastAsync(string tag, Action<bool, object> callback, object state, EdenData data)
        {
            Task.Run(() => callback(Broadcast(tag, data), state));
        }
        /// <summary>
        /// Broadcast data asynchronously to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="callback">callback function[bool success, object state] run after data send</param>
        /// <param name="state">parameter for callback function</param>
        /// <param name="data">object array sending data</param>
        public void BroadcastAsync(string tag, Action<bool, object> callback, object state, params object[] data)
        {
            if (data == null || data.Length == 0)
            {
                BroadcastAsync(tag, callback, state, new EdenData());
            }
            else if (data.Length == 1)
            {
                BroadcastAsync(tag, callback, state, new EdenData(data[0]));
            }
            else
                BroadcastAsync(tag, callback, state, new EdenData(data));
        }
        /// <summary>
        /// Broadcast data asynchronously to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="callback">callback function[bool success, object state] run after data send</param>
        /// <param name="state">parameter for callback function</param>
        /// <param name="data">dictionary sending data</param>
        public void BroadcastAsync(string tag, Action<bool, object> callback, object state, Dictionary<string, object> data)
        {
            if (data == null)
                BroadcastAsync(tag, callback, state, new EdenData());
            else
                BroadcastAsync(tag, callback, state, new EdenData(data));
        }
        /// <summary>
        /// Broadcast data asynchronously to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">sending data</param>
        public void BroadcastAsync(string tag, EdenData data)
        {
            Task.Run(() => Broadcast(tag, data));
        }
        /// <summary>
        /// Broadcast data asynchronously to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data</param>
        public void BroadcastAsync(string tag, params object[] data)
        {
            if (data == null || data.Length == 0)
            {
                BroadcastAsync(tag, new EdenData());
            }
            else if (data.Length == 1)
            {
                BroadcastAsync(tag, new EdenData(data[0]));
            }
            else
                BroadcastAsync(tag, new EdenData(data));
        }
        /// <summary>
        /// Broadcast data asynchronously to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">dictionary sending data</param>
        public void BroadcastAsync(string tag, Dictionary<string, object> data)
        {
            if (data == null)
                BroadcastAsync(tag, new EdenData());
            else
                BroadcastAsync(tag, new EdenData(data));
        }

        /// <summary>
        /// Broadcast data to all connected client except specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="client_id">client id for except  when broadcast</param>
        /// <param name="callback">callback function[bool success, object state] run after data send</param>
        /// <param name="state">parameter for callback function</param>
        /// <param name="data">EdenData structured sending data</param>
        public void BroadcastExceptAsync(string tag, string client_id, Action<bool, object> callback, object state, EdenData data)
        {
            Task.Run(() => callback(BroadcastExcept(tag, client_id, data), state));
        }
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="client_id">client id for except  when broadcast</param>
        /// <param name="callback">callback function[bool success, object state] run after data send</param>
        /// <param name="state">parameter for callback function</param>
        /// <param name="data">object array sending data</param>
        public void BroadcastExceptAsync(string tag, string client_id, Action<bool, object> callback, object state, params object[] data)
        {
            if (data == null || data.Length == 0)
            {
                BroadcastExceptAsync(tag, client_id, callback, state, new EdenData());
            }
            else if (data.Length == 1)
            {
                BroadcastExceptAsync(tag, client_id, callback, state, new EdenData(data[0]));
            }
            else
                BroadcastExceptAsync(tag, client_id, callback, state, new EdenData(data));
        }
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="client_id">client id for except  when broadcast</param>
        /// <param name="callback">callback function[bool success, object state] run after data send</param>
        /// <param name="state">parameter for callback function</param>
        /// <param name="data">dictionary sending data</param>
        public void BroadcastExceptAsync(string tag, string client_id, Action<bool, object> callback, object state, Dictionary<string, object> data)
        {
            if (data == null)
                BroadcastExceptAsync(tag, client_id, callback, state, new EdenData());
            else
                BroadcastExceptAsync(tag, client_id, callback, state, new EdenData(data));
        }
        /// <summary>
        /// Broadcast data to all connected client except specific client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="client_id">client id for except  when broadcast</param>
        /// <param name="data">EdenData structured sending data</param>
        public void BroadcastExceptAsync(string tag, string client_id, EdenData data)
        {
            Task.Run(() => BroadcastExcept(tag, client_id, data));
        }
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="client_id">client id for except  when broadcast</param>
        /// <param name="data">object array sending data</param>
        public void BroadcastExceptAsync(string tag, string client_id, params object[] data)
        {
            if (data == null || data.Length == 0)
            {
                BroadcastExceptAsync(tag, client_id, new EdenData());
            }
            else if (data.Length == 1)
            {
                BroadcastExceptAsync(tag, client_id, new EdenData(data[0]));
            }
            else
                BroadcastExceptAsync(tag, client_id, new EdenData(data));
        }
        /// <summary>
        /// Broadcast data to all connected client
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="client_id">client id for except  when broadcast</param>
        /// <param name="data">dictionary sending data</param>
        public void BroadcastExceptAsync(string tag, string client_id, Dictionary<string, object> data)
        {
            if (data == null)
                BroadcastExceptAsync(tag, client_id, new EdenData());
            else
                BroadcastExceptAsync(tag, client_id, new EdenData(data));
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
                client = server.EndAcceptTcpClient(ar);
            }
            catch
            {
                //maybe server is closed
                return;
            }
            string log = "";
#pragma warning disable CS8600
            //client cannot be null
            IPEndPoint remoteEndPoint = (IPEndPoint)client.Client.RemoteEndPoint;
#pragma warning restore CS8600
#pragma warning disable CS8602
            //remoteEndPoint cannot be null
            if (!is_listening || clients.Count >= max_accept_num)
            {
                if (!is_listening)
                {
                    log = remoteEndPoint.ToString() + " is rejected by SERVER NOT LISTENING STATE";
                    client.GetStream().Write(Encoding.UTF8.GetBytes("NOT LISTENING"));
                }
                else if (clients.Count > max_accept_num)
                {
                    log = remoteEndPoint.ToString() + " is rejected by SERVER FULL STATE";
                    client.GetStream().Write(Encoding.UTF8.GetBytes("FULL"));
                }
                Log(log);
                client.GetStream().Close();
                client.Close();
                return;
            }

            Log("Remote client connected " + remoteEndPoint.ToString());
#pragma warning restore CS8602


            EdenClient eclient = new EdenClient(client, remoteEndPoint.ToString());
            lock (clients)
            {
                clients.Add(eclient.id, eclient);
            }
#pragma warning disable CS8602
            accept_event(eclient.id);
#pragma warning restore CS8602

            NetworkStream stream = client.GetStream();

            //OK sign to client
            stream.Write(Encoding.UTF8.GetBytes("OK"));

            server.BeginAcceptTcpClient(Listening, null);

            if (stream.CanRead)
                stream.BeginRead(eclient.read_buffer, 0, eclient.read_buffer.Length, ReadBuffer, eclient);
            else // Exception for network stream  read is not ready
            {
                Log("NetworkStream cannot read on client_id : " + eclient.id);
            }
        }

        /// <summary>
        /// Async method for read packet and tcp buffer
        /// </summary>
        /// <param name="ar">ar.AsyncState is EdenClient which sent data</param>
        private void ReadBuffer(IAsyncResult ar)
        {
#pragma warning disable CS8605
            //ar.AsyncState cannot be null
            EdenClient eclient = (EdenClient)ar.AsyncState;
#pragma warning restore CS8605
            NetworkStream stream = eclient.stream;
            int numberofbytes;
            try
            {
                numberofbytes = stream.EndRead(ar);
            }
            catch (Exception e) // Exception for forced disconnection to client
            {
                eclient.stream.Close();
                eclient.tcpclient.Close();

                if (disconn_event != null)
                    disconn_event(eclient.id);

                lock (clients)
                {
                    clients.Remove(eclient.id);
                }

                Log("Forced disconnection to client. " + eclient.id + "\n" + e.Message);
                return;
            }

            if (numberofbytes == 0)// Process for client disconnection
            {
                eclient.stream.Close();
                eclient.tcpclient.Close();

                if (disconn_event != null)
                    disconn_event(eclient.id);

                lock (clients)
                {
                    clients.Remove(eclient.id);
                }

                Log("Client disconnected." + eclient.id);
                return;
            }

            int length = BitConverter.ToInt32(new ArraySegment<byte>(eclient.read_buffer, 0, 4));
            byte[] json_object = (new ArraySegment<byte>(eclient.read_buffer, 4, length).ToArray());

            LogAsync(eclient.id + "  ==> Packet Len : " + length.ToString() + " | Json Obj : " + Encoding.UTF8.GetString(json_object));

            EdenPacket packet;
            try
            {
                packet = JsonSerializer.Deserialize<EdenPacket>(json_object, new JsonSerializerOptions { IncludeFields = true });

                Action<string, EdenData>? PacketListenEvent;
                if (receive_events.TryGetValue(packet.tag, out PacketListenEvent))
                {
                    packet.data.CastJsonToType();
                    try { PacketListenEvent(eclient.id, packet.data); }
                    catch (Exception e) // Exception for every problem in PacketListenEvent
                    {
                        Log("Some Error occurs in PacketListenEvent : " + packet.tag + " | " + eclient.id + "\n" + e.Message);
                    }
                }
                else // Exception for packet tag not registered
                {
                    Log("EdenNet-Error::There is no packet tag <" + packet.tag + "> from " + eclient.id);
                    return;
                }
            }
            catch (Exception e) // Exception for not formed packet data
            {
                Log("Packet data is not JSON-formed on " + eclient.id + "\n" + e.Message);
                return;
            }

            if (stream.CanRead)
                stream.BeginRead(eclient.read_buffer, 0, eclient.read_buffer.Length, ReadBuffer, eclient);
            else // Exception for network stream read is not ready
            {
                Log("NetworkStream cannot read on client_id : " + eclient.id);
            }
        }

        private void Log(string log)
        {
            if (print_log)
            {
                log = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ff") + "|EdenNetServer]" + log;
                Console.WriteLine(log);
                log_stream.WriteLine(log);
            }
        }

        private void LogAsync(string log)
        {
            if (print_log)
            {
                log = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ff") + "|EdenNetServer]" + log;
                Console.WriteLine(log);
                log_stream.WriteLineAsync(log);
            }
        }

        #endregion
    }
}
