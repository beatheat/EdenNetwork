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
        static string client_id = "";

        static void Main(string[] args)
        {
            EdenNetClient client = new EdenNetClient("127.0.0.1", 7777);

            ConnectionState cstate = client.Connect();

            if (cstate == ConnectionState.OK)
            {
                Console.WriteLine("Connection success");
                client.AddReceiveEvent("server_msg", (EdenData data) => {
                    Console.WriteLine("Server: " + data.Get<string>());
                });

                bool quit = false;
                while (!quit)
                {
                    string line = Console.ReadLine();
                    if (line.Equals("exit"))
                        quit = true;
                    else
                    {
                        client.Send("client_msg", line);
                        Console.WriteLine("Client: " + line);
                    }

                }
            }
            else
                Console.WriteLine("Connection failed");

            client.Close();
        }
    }
}