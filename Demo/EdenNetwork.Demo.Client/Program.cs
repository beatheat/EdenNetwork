﻿using System;
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
            //Run program if connection state is ok
            if (cstate == ConnectionState.OK)
            {
                Console.WriteLine("Connection success");
                //Register callback method which run after server message received
                client.AddReceiveEvent("server_msg", (EdenData data) => {
                    Console.WriteLine("Server: " + data.Get<string>());
                });
                client.RequestAsync("response_test", 10,(EdenData data) => {
                    Console.WriteLine(data.Get<string>());
                    
                }, "123");
                bool quit = false;
                //main loop
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