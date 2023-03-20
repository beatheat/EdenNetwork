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
        static void Main(string[] args)
        {
            EdenNetClient client = new EdenNetClient("127.0.0.1", 7777);

            ConnectionState cstate = client.Connect();
            //Run program if connection state is ok
            if (cstate == ConnectionState.OK)
            {
                Console.WriteLine("Connection success");
                //Register callback method which run after server message received
                client.AddReceiveEvent("serverMessage", (EdenData data) => {
                    if(data.TryGet<string>(out var testData))
                        Console.WriteLine("Server: " + testData);
                });

                bool quit = false;
                //main loop
                while (!quit)
                {
                    string line = Console.ReadLine();
                    if (line.Equals("exit"))
                        quit = true;
                    //Request current server time
                    else if (line.Equals("serverTime"))
                    {
                        EdenData data = client.Request("serverTime", 10);
                        if(data.TryGet<string>(out var serverTime))
                            Console.WriteLine("Server time is " + serverTime);
                    }
                    else
                    {
                        client.Send("clientMessage", line);
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