namespace EdenNetwork;


[AttributeUsage(AttributeTargets.Method)]
public class EdenReceiveAttribute : Attribute
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
public class EdenResponseAttribute : Attribute
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