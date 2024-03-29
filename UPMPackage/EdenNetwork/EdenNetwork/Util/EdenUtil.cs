﻿using System;
using System.Net.NetworkInformation;
using System.Threading;

namespace EdenNetwork
{
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


		public static void WaitUntilFlagOn(ref bool flag, double timeout)
		{
			var time = DateTime.Now;
			while (!flag)
			{
				Thread.Sleep(10);
				if (DateTime.Now - time > TimeSpan.FromSeconds(timeout))
					break;
			}
		}
	}
}