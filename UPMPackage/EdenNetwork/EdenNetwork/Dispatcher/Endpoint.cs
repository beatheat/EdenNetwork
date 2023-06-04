using System;
using System.Reflection;

namespace EdenNetwork.Dispatcher
{
	internal class Endpoint
	{
		public object Owner { get; set; } = null!;
		public MethodInfo Logic { get; set; }
		public Type ArgumentType { get; set; }
	}
}