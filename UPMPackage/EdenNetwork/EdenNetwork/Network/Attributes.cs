using System;

namespace EdenNetwork
{


	[AttributeUsage(AttributeTargets.Method)]
	public class EdenReceiveAttribute : Attribute
	{

	}

	[AttributeUsage(AttributeTargets.Method)]
	public class EdenResponseAttribute : Attribute
	{

	}

	[AttributeUsage(AttributeTargets.Method)]
	public class EdenClientConnectAttribute : Attribute
	{

	}

	[AttributeUsage(AttributeTargets.Method)]
	public class EdenClientDisconnectAttribute : Attribute
	{

	}

	[AttributeUsage(AttributeTargets.Method)]
	public class EdenDisconnectAttribute : Attribute
	{

	}

	[AttributeUsage(AttributeTargets.Method)]
	public class EdenNatRelayAttribute : Attribute
	{

	}
}