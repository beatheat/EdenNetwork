using System.Reflection;

#pragma warning disable CS8618


namespace EdenNetwork.Dispatcher;

internal class Endpoint
{
	public bool Hide { get; set; } = false;
	public object Owner { get; set; }
	public MethodInfo Logic { get; set; }
	public MethodInfo DataDeserializer { get; set; }
	public Type? ArgumentType { get; set; }
}