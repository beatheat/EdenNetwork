using System.Net.NetworkInformation;

namespace EdenNetwork;

public static class EdenUtil
{
	public static bool IsPortAvailable(int port)
	{
		bool isAvailable = true;

		var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
		var activeUdpListeners = ipGlobalProperties.GetActiveUdpListeners();

		foreach (var udpInfo in activeUdpListeners)
		{
			if (udpInfo.Port == port)
			{
				isAvailable = false;
				break;
			}
		}
            
		return isAvailable;
	}

}