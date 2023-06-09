using System.Collections.Generic;
using UnityEngine;

namespace EdenNetwork
{
	public class EdenNetManager : MonoBehaviour
	{
		private static EdenNetManager _instance = null;

		private readonly Dictionary<string, EdenTcpClient> _tcpNetClients;
		private readonly Dictionary<string, EdenUdpClient> _udpNetClients;

		void Awake()
		{
			DontDestroyOnLoad(this);
			_instance = this;
		}

		public EdenNetManager()
		{
			_tcpNetClients = new Dictionary<string, EdenTcpClient>();
			_udpNetClients = new Dictionary<string, EdenUdpClient>();
		}

		public static EdenTcpClient CreateTcpClient(string tag, string ipAddress, int port)
		{
			EdenTcpClient client = new EdenTcpClient(ipAddress, port);
			_instance._tcpNetClients.Add(tag, client);
			return client;
		}

		public static EdenTcpClient GetTcpClient(string tag)
		{
			if (_instance._tcpNetClients.ContainsKey(tag))
			{
				return _instance._tcpNetClients[tag];
			}
			return null;
		}

		public static void DestroyTcpClient(string tag)
		{
			var client = GetTcpClient(tag);
			if (client != null)
			{
				_instance._tcpNetClients.Remove(tag);
				Destroy(client);
			}
		}
		
		public static EdenUdpClient CreateUdpClient(string tag, string ipAddress, int port)
		{
			EdenUdpClient client = new EdenUdpClient()
			client.Init(ipAddress, port, logging, logPath);
			_instance._udpNetClients.Add(tag, client);
			return client;
		}
		
		public static EdenUdpClient GetUdpClient(string tag)
		{
			if (_instance._udpNetClients.ContainsKey(tag))
			{
				return _instance._udpNetClients[tag];
			}
			return null;
		}

		public static void DestroyUdpClient(string tag)
		{
			var client = GetUdpClient(tag);
			if (client != null)
			{
				_instance._udpNetClients.Remove(tag);
				Destroy(client);
			}
		}
		
	}
}