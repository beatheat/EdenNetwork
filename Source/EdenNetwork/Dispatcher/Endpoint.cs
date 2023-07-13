using System.Reflection;

#pragma warning disable CS8618


namespace EdenNetwork.Dispatcher;

internal class Endpoint
{
	public object Owner { get; set; }
	public string Name { get; set; }
	public bool Hide { get; set; } = false;
}

internal class ServerReceiveResponseEndpoint : Endpoint
{
	public ServerEndpointLogicInvoker Logic { get; set; }
	public DeserializeDataInvoker? DataDeserializer { get; set; } = null;
}

internal class ClientReceiveEndpoint : Endpoint
{
	public ClientEndpointLogicInvoker Logic { get; set; }
	public DeserializeDataInvoker? DataDeserializer { get; set; } = null;
}


internal class ClientConnectEndpoint : Endpoint
{
	public ClientConnectLogicInvoker Logic { get; set; }
}

internal class ClientDisconnectEndpoint : Endpoint
{
	public ClientDisconnectLogicInvoker Logic { get; set; }
}

internal class ServerDisconnectEndpoint : Endpoint
{
	public ServerDisconnectLogicInvoker Logic { get; set; }
}

internal class NatEndpoint : Endpoint
{
	public NatEndpointLogicInvoker Logic { get; set; }
}