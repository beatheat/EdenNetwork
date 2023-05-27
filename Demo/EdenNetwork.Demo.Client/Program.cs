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
            private IEdenNetClient client;

            public void Start()
            {
                client = new EdenTcpClient("127.0.0.1", 7777);
                
                client.AddEndpoints(this);
                
                if (client.Connect() == ConnectionState.OK)
                {
                    Console.WriteLine("Connection success");
                    MainLoop();
                }
                else
                {
                    Console.WriteLine("Connection fail");
                }
            }
            
            [EdenReceive]
            private void ServerMessage(string message)
            {
                Console.WriteLine("Server: " + message);
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
                    //Request current server time
                    else if (line.Equals("ServerTime"))
                    {
                        var serverTime = client.Request<DateTime>("ServerTime");
                        Console.WriteLine("Server time is " + serverTime);
                    }
                    else
                    {
                        client.Send("ClientMessage", line);
                        Console.WriteLine("Client: " + line);
                    }

                }
                client.Close();
            }

        }
        

        static void Main(string[] args)
        {
            var messenger = new Messenger();
            messenger.Start();
        }
    }
}