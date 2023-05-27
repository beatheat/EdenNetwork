using System.Net;

namespace EdenNetwork;


public struct PeerId
{
	public string Ip { get; set; }
	public int Port { get; set; }

	public PeerId(string ip, int port)
	{
		Ip = ip;
		Port = port;
	}

	public PeerId(IPEndPoint ipEndPoint)
	{
		Ip = ipEndPoint.Address.ToString();
		Port = ipEndPoint.Port;
	}

	public static bool operator == (PeerId left, PeerId right)
	{
		return string.CompareOrdinal(left.Ip, right.Ip) == 0 && left.Port == right.Port;
	}
		
	public static bool operator != (PeerId left, PeerId right)
	{
		return string.CompareOrdinal(left.Ip, right.Ip) != 0 || left.Port != right.Port;
	}
		
	public bool Equals(PeerId other)
	{
		return Ip == other.Ip && Port == other.Port;
	}

	public override bool Equals(object? obj)
	{
		return obj is PeerId other && Equals(other);
	}
	
	public override int GetHashCode()
	{
		return HashCode.Combine(Ip, Port);
	}

	public override string ToString()
	{
		return Ip + ":" + Port;
	}

	public IPEndPoint ToIPEndPoint()
	{
		return IPEndPoint.Parse(ToString());
	}
}