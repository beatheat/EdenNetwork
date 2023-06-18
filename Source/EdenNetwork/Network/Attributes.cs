namespace EdenNetwork;


public class EdenAttribute : Attribute
{
	
}

[AttributeUsage(AttributeTargets.Method)]
public class EdenReceiveAttribute : EdenAttribute
{
	public string? apiName;

	public EdenReceiveAttribute()
	{
		this.apiName = null;
	}
	
	public EdenReceiveAttribute(string apiName)
	{
		this.apiName = apiName;
	}
}

[AttributeUsage(AttributeTargets.Method)]
public class EdenResponseAttribute : EdenAttribute
{
	public string? apiName;
	public EdenResponseAttribute()
	{
		this.apiName = null;
	}

	public EdenResponseAttribute(string apiName)
	{
		this.apiName = apiName;
	}
}

[AttributeUsage(AttributeTargets.Method)]
public class EdenClientConnectAttribute : EdenAttribute
{
	
}

[AttributeUsage(AttributeTargets.Method)]
public class EdenClientDisconnectAttribute : EdenAttribute
{
	
}

[AttributeUsage(AttributeTargets.Method)]
public class EdenDisconnectAttribute : EdenAttribute
{
	
}

[AttributeUsage(AttributeTargets.Method)]
public class EdenNatRelayAttribute : EdenAttribute
{
	
}