using System.Net;

namespace EdenNetwork;

public class NatPeer
{
	public PeerId LocalEndPoint { get; set; }
	public PeerId RemoteEndPoint { get; set; }
}
