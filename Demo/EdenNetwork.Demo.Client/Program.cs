using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text.Json;
using EdenNetwork;



namespace EdenNetwork.Demo.Client
{
    class Program
    {
        public class Messenger
        {
            private IEdenNetClient _client;

            public void Start()
            {
                _client = new EdenTcpClient("127.0.0.1", 7777);
                
                _client.AddEndpoints(this);

                var state = _client.Connect();
                if (state == ConnectionState.Ok)
                {
                    Console.WriteLine("Connection success");
                    MainLoop();
                }
                else
                {
                    Console.WriteLine("Connection fail, Reason : " + state);
                }
            }
            
            [EdenReceive]
            private void ServerMessage(string message)
            {
                Console.WriteLine("Server: " + message);
            }

            [EdenDisconnect]
            private void Disconnect(DisconnectReason reason)
            {
                Console.WriteLine("Reason : " + reason.ToString());
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
                    //Request current server time
                    else if (line.Equals("ServerTime"))
                    {
                        var serverTime = _client.Request<DateTime>("ServerTime");
                        Console.WriteLine("Server time is " + serverTime);
                    }
                    else
                    {
                        _client.Send("ClientMessage", line);
                        Console.WriteLine("Client: " + line);
                    }

                }
                _client.Close();
            }

        }
        

        static void Main(string[] args)
        {
            Log.EdenLogManager.SettingLogger("DemoClientNet", ".log");
            var messenger = new Messenger();
            messenger.Start();
        }
    }
}