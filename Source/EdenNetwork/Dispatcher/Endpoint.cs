using System.Reflection;

namespace EdenNetwork.Dispatcher;

internal class Endpoint
{
	public object Onwer { get; set; }
	public MethodInfo Logic { get; set; }
	public Type? ArgumentType { get; set; }
}