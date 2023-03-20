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
        static string clientId = "";

        static void Main(string[] args)
        {
            EdenNetServer server = new EdenNetServer(7777);

            Console.WriteLine("Server is listening now...");
            //Listening clients with restriction allowing only 1 client to connect and register method which runs after client connected
            server.Listen(1,(string clientId) => {
                Program.clientId = clientId;
                Console.WriteLine("Client <" + clientId + "> is connected");
            });

            //Block server until client connects
            while(clientId == "")
            {
                Thread.Sleep(100);
            }

            //Register callback method which run after client message received
            server.AddReceiveEvent("clientMessage", (string clientId, EdenData data) => {
                if (data.TryGet<string>(out var testData))
                    Console.WriteLine("Client: " + testData);
            });
            
            //Register callback method which response current server time
            server.AddResponse("serverTime", (string clientId, EdenData data) =>
            {
                return new EdenData(DateTime.Now.ToString());
            });

            bool quit = false;
            //main loop
            while (!quit)
            {
                string line = Console.ReadLine();
                if (line.Equals("exit")) 
                    quit = true;
                else
                {
                    server.Send("serverMessage", clientId, line);
                    Console.WriteLine("Server: " + line);
                }
                
            }

            server.Close();
        }
    }
}