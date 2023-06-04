using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text.Json;
using EdenNetwork;



namespace EdenNetwork.Demo.Server
{
    class Program
    {

        public class Messenger
        {
            private IEdenNetServer _server;

            private PeerId? _clientId = null;
            
            public void Start(IEdenNetServer server)
            {
                this._server = server;
                server.AddEndpoints(this);
                Console.WriteLine("Server is listening now...");
                server.Listen(1);
                
                MainLoop();
            }

            [EdenClientConnect]
            public void ClientConnect(PeerId clientId)
            {
                this._clientId = clientId;
                Console.WriteLine("Client <" + clientId + "> is connected");
            }
            
            [EdenClientDisconnect]
            public void ClientDisconnect(PeerId clientId, DisconnectReason reason)
            {
                this._clientId = clientId;
                Console.WriteLine("Client <" + clientId + "> is disconnected");
            }

            [EdenReceive]
            public void ClientMessage(PeerId clientId, string message)
            {
                Console.WriteLine("Client : " + message);
            }

            [EdenResponse]
            public DateTime ServerTime(PeerId clientId)
            {
                return DateTime.Now;
            }

            public void MainLoop()
            {
                bool quit = false;
                //main loop
                while (!quit)
                {
                    string line = Console.ReadLine()!;
                    if (line.Equals("exit")) 
                        quit = true;
                    else
                    {
                        if (_clientId != null)
                        {
                            _server.Send("ServerMessage", _clientId.Value, line);
                            Console.WriteLine("Server: " + line);
                        }
                    }
                
                }
                _server.Close();;
            }
        }
        
        static void Main(string[] args)
        {
            Log.EdenLogManager.SettingLogger("DemoServerNet", ".log");
            var messenger = new Messenger();
            messenger.Start(new EdenTcpServer(7777));
        }
    }
}