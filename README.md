# EdenNetwork ![apm](https://img.shields.io/badge/license-MIT-green)
Simple C# asynchronous TCP&UDP socket server&amp;client library for .NET &amp; Unity

### Required Version
.net 6.0 & c# 10.0   
Unity >= 2021.3.19f1   

### Dependencies

litenetlib 1.0.1.1 (https://github.com/RevenantX/LiteNetLib)   
protobuf-net 3.2.16 (https://github.com/protobuf-net/protobuf-net)   
ZLogger 1.7.0 (https://github.com/Cysharp/ZLogger)   
 

### Nuget
[nuget package](https://www.nuget.org/packages/EdenNetwork)   

### UPM
You can add `https://github.com/beatheat/EdenNetwork.git?path=UPMPackage/EdenNetwork` to Package Manager   
This package has dependency on https://github.com/Cysharp/UniTask

### Documentation (Korean)
[documentation](https://github.com/beatheat/EdenNetwork/blob/main/Docs/EdenNetwork_Documentation.pdf)


### Demo

[Simple Chatting](https://github.com/beatheat/EdenNetwork/tree/main/Demo)

### SimpleUsage

Server
```c#
class Endpoint
{
    EdenTcpServer server;
    public Endpoint(EdenTcpServer server)
    {
        this.server = server;
    }
    [EdenResponse]
    public int ServerResponse(PeerId clientId, int data)
    {
        return data;
    }
    [EdenReceive]
    public void ServerReceive(PeerId clientId, int data)
    {
        server.Send(clientId, data);
    }
}

EdenTcpServer server = new EdenTcpServer(12121);
server.AddEndpoint(new Endpoint(server));
// Listen on other thread 
server.Listen(1);

...
```

Client
```c#
class Endpoint
{
    [EdenReceive]
    public void ClientReceive(PeerId clientId, int data)
    {
        Console.WriteLine("receive: " + data); // receive : 5
    }
}

EdenTcpClient client = new EdenTcpClient("127.0.0.1", 12121);
client.AddEndpoint(new Endpoint());

if(client.Connect() == ConnectionState.OK)
{
    
    //blocked until response return or timeout
    int response = client.Request<int>("ServerResponse", data: 1);
    Console.WriteLine("response: " + response); // response : 3
    client.Send("ServerReceive", data: 5); 
}

...
```
