﻿using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;


namespace EdenNetwork
{
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
    public class EdenNetClient
    {
        #region Fields
        private const int BUF_SIZE = 8 * 1024;
        private const string REQUEST_PREFIX = "*r*";

        private TcpClient tcpclient;
        private NetworkStream stream;
        private byte[] read_buffer;
        private string server_id;
        private Dictionary<string, Action<EdenData>> receive_events;
        private Dictionary<string, EdenData?> response_events;
        private Action<string> disconn_event;
        private StreamWriter log_stream;
        private Thread log_thread;

        private string ipv4_address;
        private int port;
        private bool print_log;
        #endregion

        #region Public Methods

        /// <summary>
        /// EdenNetwork manager for UNITY and C#9.0 
        /// </summary>
        public EdenNetClient(string ipv4_address, int port, string log_path = "")
        {
            receive_events = new Dictionary<string, Action<EdenData>>();
            response_events = new Dictionary<string, EdenData?>();
            read_buffer = new byte[BUF_SIZE];
            log_thread = null;
            log_stream = null;
            disconn_event = null;
            tcpclient = null;
            stream = null;
            server_id = null;

            this.ipv4_address = ipv4_address;
            this.port = port;

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
                            Console.WriteLine("Log stream is closed");
                        }
                    });
                    log_thread.Start();
                }
                catch //(Exception e)
                {
                    Console.WriteLine("Cannot create log-file stream on " + log_path);
                }
            }
        }

        /// <summary>
        /// Connect to server by IP, port
        /// </summary>
        /// <returns>returns ConnectionState of server[OK, FULL, NOT_LISTENING, ERROR]</returns>
        public ConnectionState Connect()
        {
            if (tcpclient != null)
                Close();
            try
            {
                tcpclient = new TcpClient(ipv4_address, port);
                stream = tcpclient.GetStream();
                byte[] buffer = new byte[128];
                stream.Read(buffer);
                string server_state = Encoding.UTF8.GetString(buffer);
                if (String.Compare(server_state, "OK") == 0)
                {
                    Log("Connection success to " + ipv4_address + ":" + port);
                }
                else if (String.Compare(server_state, "NOT LISTENING") == 0)
                {
                    Log("Cannot connect to server : SERVER IS NOT LISTENING");
                    stream.Close();
                    tcpclient.Close();
                    return ConnectionState.NOT_LISTENING;
                }
                else //if(server_state == "FULL")
                {
                    Log("Cannot connect to server : SERVER IS FULL");
                    stream.Close();
                    tcpclient.Close();
                    return ConnectionState.FULL;
                }
            }
#pragma warning disable CS0168
            catch (Exception e)
            {
                Log("Cannot connect to server : " + e.Message);
                return ConnectionState.ERROR;
            }
#pragma warning restore CS0168

            server_id = ipv4_address + ":" + port;
            stream.BeginRead(read_buffer, 0, read_buffer.Length, ReadBuffer, null);
            return ConnectionState.OK;
        }

        /// <summary>
        /// Connect to server asyncronously by IP, port
        /// </summary>
        /// <param name="IPAddress">IP adress of server</param>
        /// <param name="port">port number of server</param>
        /// <param name="DoAfterConnect">callback method execute after connection success or fail</param>
        public async Task<ConnectionState> ConnectAsync()
        {
            return await Task.Run(async () =>
            {
                if (tcpclient != null)
                    Close();
                try
                {
                    tcpclient = new TcpClient(AddressFamily.InterNetwork);

                    tcpclient.Connect(ipv4_address, port);
                    await tcpclient.ConnectAsync(ipv4_address, port);
                    stream = tcpclient.GetStream();
                    byte[] buffer = new byte[128];
                    stream.Read(buffer);
                    string server_state = Encoding.UTF8.GetString(buffer);
                    if (String.Compare(server_state, "OK") == 0)
                    {
                        Log("Connection success to " + ipv4_address + ":" + port);
                    }
                    else if (String.Compare(server_state, "NOT LISTENING") == 0)
                    {
                        Log("Cannot connect to server : SERVER IS NOT LISTENING");
                        stream.Close();
                        tcpclient.Close();
                        return ConnectionState.NOT_LISTENING;
                    }
                    else //if(server_state == "FULL")
                    {
                        Log("Cannot connect to server : SERVER IS FULL");
                        stream.Close();
                        tcpclient.Close();
                        return ConnectionState.FULL;
                    }
                }
                catch (Exception e)
                {
                    Log("Cannot connect to server : " + e.Message);
                    return ConnectionState.ERROR;
                }

                server_id = ipv4_address + ":" + port;
                stream.BeginRead(read_buffer, 0, read_buffer.Length, ReadBuffer, null);
                return ConnectionState.OK;
            });
        }

        /// <summary>
        /// Connect to server asyncronously by IP, port
        /// </summary>
        /// <param name="IPAddress">IP adress of server</param>
        /// <param name="port">port number of server</param>
        /// <param name="DoAfterConnect">callback method execute after connection success or fail</param>
        public void BeginConnect(Action<ConnectionState> callback)
        {
            Task.Run(() =>
            {
                if (tcpclient != null)
                    Close();
                server_id = ipv4_address + ":" + port;
                tcpclient = new TcpClient(AddressFamily.InterNetwork);
                try
                {
                    tcpclient.Connect(ipv4_address, port);
                    stream = tcpclient.GetStream();

                    byte[] buffer = new byte[128];
                    stream.Read(buffer);
                    string server_state = Encoding.UTF8.GetString(buffer);

                    ConnectionState state;

                    if (String.Compare(server_state, "OK") == 0)
                    {
                        Log("Connection success");
                        state = ConnectionState.OK;
                        stream.BeginRead(read_buffer, 0, read_buffer.Length, ReadBuffer, null);
                    }
                    else if (String.Compare(server_state, "NOT LISTENING") == 0)
                    {
                        Log("Cannot connect to server : SERVER IS NOT LISTENING");
                        state = ConnectionState.NOT_LISTENING;
                        stream.Close();
                        tcpclient.Close();
                    }
                    else //if(server_state == "FULL")
                    {
                        Log("Cannot connect to server : SERVER IS FULL");
                        state = ConnectionState.FULL;
                        stream.Close();
                        tcpclient.Close();
                    }
                    callback(state);
                }
                catch
                {
                    Log("Cannot connect to server");
                    callback(ConnectionState.ERROR);
                }
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
            if (stream.CanWrite)
            {
                EdenPacket packet = new EdenPacket();
                packet.tag = tag;
                packet.data = data;

                string json_packet = JsonSerializer.Serialize(packet, new JsonSerializerOptions { IncludeFields = true });
                byte[] bytes = Encoding.UTF8.GetBytes(json_packet);
                byte[] send_obj = BitConverter.GetBytes(bytes.Length);
                send_obj = send_obj.Concat(bytes).ToArray();

                if (send_obj.Length >= BUF_SIZE)
                {
                    Log("Too big data to send, EdenNetProtocol support data size below " + BUF_SIZE);
                    return false;
                }
                stream.Write(send_obj, 0, send_obj.Length);
                Log(server_id + " <==  Packet Len : " + bytes.Length.ToString() + " | Json Obj : " + json_packet);

                return true;
            }
            else
            {
                Log("NetworkStream cannot write on server : " + server_id);
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
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">EdenData structured sending data </param>
        public async Task<bool> SendAsync(string tag, EdenData data)
        {
            return await Task.Run(async () =>
            {
                if (stream.CanWrite)
                {
                    EdenPacket packet = new EdenPacket();
                    packet.tag = tag;
                    packet.data = data;

                    string json_packet = JsonSerializer.Serialize(packet, new JsonSerializerOptions { IncludeFields = true });
                    byte[] bytes = Encoding.UTF8.GetBytes(json_packet);
                    byte[] send_obj = BitConverter.GetBytes(bytes.Length);
                    send_obj = send_obj.Concat(bytes).ToArray();

                    if (send_obj.Length >= BUF_SIZE)
                    {
                        Log("Too big data to send, EdenNetProtocol support data size below " + BUF_SIZE);
                        return false;
                    }
                    await stream.WriteAsync(send_obj, 0, send_obj.Length);
                    Log(server_id + " <==  Packet Len : " + bytes.Length.ToString() + " | Json Obj : " + json_packet);

                    return true;
                }
                else
                {
                    Log("NetworkStream cannot write to server");
                    return false;
                }
            });
        }
        /// <summary>
        /// Send data json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data </param>
        public async Task<bool> SendAsync(string tag, params object[] data)
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
        public async Task<bool> SendAsync(string tag, Dictionary<string, object> data)
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
        /// <param name="DoAfterSend">callback function[bool success] run after data send</param>
        /// <param name="state">parameter for callback function</param>
        /// <param name="data">EdenData structured sending data</param>
        public void BeginSend(string tag, Action<bool> callback, EdenData data)
        {
            Task.Run(() => callback(Send(tag, data)));
        }
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="DoAfterSend">callback function[bool success ] run after data send</param>
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
        /// <param name="DoAfterSend">callback function[bool success] run after data send</param>
        /// <param name="state">parameter for callback function</param>
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
        public void BeginSend(string tag, EdenData data)
        {
            Task.Run(() => Send(tag, data));
        }
        /// <summary>
        /// Send data asynchronously json format to server
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data</param>
        public void BeginSend(string tag, params object[] data)
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
        public void BeginSend(string tag, Dictionary<string, object> data)
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
        /// <param name="timeout">timeout for response from server
        /// </param>
        public EdenData Request(string tag, int timeout, EdenData data)
        {
            tag = REQUEST_PREFIX + tag;
            if (!response_events.ContainsKey(tag))
                response_events.Add(tag, null);
            bool result = Send(tag, data);
            if (result == false) return new EdenData(new EdenError("ERR:Send Failed"));
            double time = 0;
            EdenData? rdata;
            do
            {
                Thread.Sleep(100);
                time += 0.1;

                if (response_events.TryGetValue(tag, out rdata) && rdata != null)
                    break;

            } while (timeout > time);
            response_events.Remove(tag);
            if (timeout <= time) return new EdenData(new EdenError("ERR:Timeout"));
            else return rdata.Value;
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data </param>
        /// <param name="timeout">timeout for response from server
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
        /// Request any data to server asynchronously and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="response">ReceiveEvent of same packet tag</param>
        /// <param name="data">EdenData structured sending data </param>
        public async Task<EdenData> RequestAsync(string tag, int timeout, EdenData data)
        {

            return await Task.Run(async () =>
            {
                tag = REQUEST_PREFIX + tag;
                if (!response_events.ContainsKey(tag))
                    response_events.Add(tag, null);
                await SendAsync(tag, data);

                double time = 0;
                EdenData? rdata;
                do
                {
                    await Task.Delay(100);
                    time += 0.1;

                    if (response_events.TryGetValue(tag, out rdata) && rdata != null)
                        break;

                } while (timeout > time);
                response_events.Remove(tag);
                if (timeout <= time) return new EdenData(new EdenError("ERR:Timeout"));
                else return rdata.Value;
            });
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data </param>
        /// <param name="timeout">timeout for response from server
        public async Task<EdenData> RequestAsync(string tag, int timeout, params object[] data)
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
        public async Task<EdenData> RequestAsync(string tag, int timeout, Dictionary<string, object> data)
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
        public void BeginRequest(string tag, int timeout, Action<EdenData> response, EdenData data)
        {
            Task.Run(() =>
            {
                tag = REQUEST_PREFIX + tag;
                if (!response_events.ContainsKey(tag))
                    response_events.Add(tag, null);
                Send(tag, data);
                double time = 0;
                EdenData? rdata;
                do
                {
                    Thread.Sleep(100);
                    time += 0.1;

                    if (response_events.TryGetValue(tag, out rdata) && rdata != null)
                        break;
                } while (timeout > time);

                response_events.Remove(tag);
                if (timeout <= time) response(new EdenData(new EdenError("ERR:Timeout")));
                else response(rdata.Value);
            });
        }

        /// <summary>
        ///  Request any data to server and server response 
        /// </summary>
        /// <param name="tag">packet tag name for client to react</param>
        /// <param name="data">object array sending data </param>
        /// <param name="timeout">timeout for response from server
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
        /// <param name="data">Dictionary sending data </param>
        /// <param name="timeout">timeout for response from server
        public void BeginRequest(string tag, int timeout, Action<EdenData> response, Dictionary<string, object> data)
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
        /// <param name="tag">reactable tag name for packet received</param>
        /// <param name="receive_event">
        ///     event that processed by packet received <br/>
        ///     arg1 = EdenData data
        /// </param>
        public void AddReceiveEvent(string tag, Action<EdenData> receive_event)
        {
            if (receive_events.ContainsKey(tag))
            {
                Log("EdenNetClient::AddReceiveEvent - receive event tag already exists");
                return;
            }
            receive_events.Add(tag, receive_event);
        }

        /// <summary>
        /// Remove receive event which response for packet name with specific tag
        /// </summary>
        /// <param name="tag">reactable tag name for packet received</param>
        public void RemoveReceiveEvent(string tag)
        {
            if (!receive_events.ContainsKey(tag))
            {
                Log("EdenNetClient::RemoveReceiveEvent - receive event tag does not exist");
                return;
            }
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
                if(stream != null)
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
                Log("Forced disconnection to server. " + server_id + "\n" + e.Message);
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

            int byte_pointer = 0;
            int packet_length = 0;
            while (byte_pointer < numberofbytes)
            {
                packet_length = BitConverter.ToInt32(new ArraySegment<byte>(read_buffer, byte_pointer, 4));
                byte_pointer += 4;
                byte[] json_object = (new ArraySegment<byte>(read_buffer, byte_pointer, packet_length)).ToArray();
                byte_pointer += packet_length;

                LogAsync(server_id + " ==> " + "Json Obj : " + "Packet Len : " + packet_length.ToString() + " | " + Encoding.UTF8.GetString(json_object));

                EdenPacket packet;
                try
                {
                    packet = JsonSerializer.Deserialize<EdenPacket>(json_object, new JsonSerializerOptions { IncludeFields = true });

                    packet.data.CastJsonToType();

                    if (packet.tag.StartsWith(REQUEST_PREFIX))
                    {
                        if (response_events.ContainsKey(packet.tag))
                        {
                            response_events[packet.tag] = packet.data;
                        }
                        else
                        {
                            Log("EdenNet-Error::There is no packet tag <" + packet.tag + "> from " + server_id);
                        }
                    }
                    else
                    {
                        Action<EdenData> PacketListenEvent;
                        if (receive_events.TryGetValue(packet.tag, out PacketListenEvent))
                        {
                            packet.data.CastJsonToType();
                            try { PacketListenEvent(packet.data); }
                            catch (Exception e) // Exception for every problem in PacketListenEvent
                            {
                                Log("Some Error occurs in PacketListenEvent : " + packet.tag + " | " + server_id + "\n" + e.Message);
                            }
                        }
                        else
                        {
                            Log("EdenNet-Error::There is no packet tag <" + packet.tag + "> from " + server_id);
                        }
                    }


                }
                catch (Exception e)
                {
                    Log("Packet data is not JSON-formed on " + server_id + "\n" + e.Message);
                }
            }
            if (stream.CanRead)
                stream.BeginRead(read_buffer, 0, read_buffer.Length, ReadBuffer, null);
            else
            {
                Log("NetworkStream cannot read on server : " + server_id);
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