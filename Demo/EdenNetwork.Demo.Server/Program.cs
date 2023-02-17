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
        static string client_id = "";

        static void Main(string[] args)
        {
            EdenNetServer server = new EdenNetServer(7777);

            Console.WriteLine("Server is listening now...");
            //Listening clients with restriction allowing only 1 client to connect and register method which runs after client connected
            server.Listen(1,(string client_id) => {
                Program.client_id = client_id;
                Console.WriteLine("Client <" + client_id + "> is connected");
            });

            //Block server until client connects
            while(client_id == "")
            {
                Thread.Sleep(100);
            }

            //Register callback method which run after client message received
            server.AddReceiveEvent("client_msg", (string client_id, EdenData data) => {
                if (data.TryGet<string>(out var testData))
                    Console.WriteLine("Client: " + testData);
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
                    server.Send("server_msg", client_id, line);
                    Console.WriteLine("Server: " + line);
                }
                
            }

            server.Close();
        }
    }
}