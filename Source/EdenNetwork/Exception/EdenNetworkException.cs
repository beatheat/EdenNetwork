namespace EdenNetwork.EdenException;

public class EdenNetworkException : Exception 
{
	public EdenNetworkException()
	{
	}

	public EdenNetworkException(string message)
		: base(message)
	{
	}

	public EdenNetworkException(string message, System.Exception inner)
		: base(message, inner)
	{
	}
}