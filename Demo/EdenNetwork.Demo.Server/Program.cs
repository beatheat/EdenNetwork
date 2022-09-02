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
            server.Listen(1,(string client_id) => {
                Program.client_id = client_id;
                Console.WriteLine("Client <" + client_id + "> is connected");
            });
            while(client_id == "")
            {
                Thread.Sleep(100);
            }

            server.AddReceiveEvent("client_msg", (string client_id, EdenData data) => {
                Console.WriteLine("Client: " + data.Get<string>());
            });

            bool quit = false;
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