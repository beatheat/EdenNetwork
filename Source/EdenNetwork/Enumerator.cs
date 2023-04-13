namespace EdenNetwork
{

	/// <summary>
	/// Enum for server connection state
	/// </summary>
	public enum ConnectionState
	{
		DISCONNECT,
		OK,
		FULL,
		FAIL,
		ERROR
	}
	
	public enum DisconnectReason
	{
		ConnectionFailed,
		Timeout,
		HostUnreachable,
		NetworkUnreachable,
		RemoteConnectionClose,
		DisconnectPeerCalled,
		ConnectionRejected,
		InvalidProtocol,
		UnknownHost,
		Reconnect,
		PeerToPeerConnection,
		PeerNotFound,
	}
}