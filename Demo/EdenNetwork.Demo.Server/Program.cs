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
            private IEdenNetServer server;

            private PeerId? clientId = null;
            
            public void Start(IEdenNetServer server)
            {
                this.server = server;
                server.AddEndpoints(this);
                Console.WriteLine("Server is listening now...");
                server.Listen(1);
                
                MainLoop();
            }

            [EdenClientConnect]
            public void ClientConnect(PeerId clientId)
            {
                this.clientId = clientId;
                Console.WriteLine("Client <" + clientId + "> is connected");
            }
            
            [EdenClientDisconnect]
            public void ClientDisconnect(PeerId clientId)
            {
                this.clientId = clientId;
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
                    string line = Console.ReadLine();
                    if (line.Equals("exit")) 
                        quit = true;
                    else
                    {
                        if (clientId != null)
                        {
                            server.Send("ServerMessage", clientId.Value, line);
                            Console.WriteLine("Server: " + line);
                        }
                    }
                
                }
                server.Close();;
            }
        }
        
        static void Main(string[] args)
        {
            var messenger = new Messenger();
            messenger.Start(new EdenUdpServer(7777));
        }
    }
}