namespace EdenNetwork.EdenException;

public class EdenTimeoutException: Exception
{
	public EdenTimeoutException()
	{
	}
	
	public EdenTimeoutException(string message) : base(message)
    {
    }
}