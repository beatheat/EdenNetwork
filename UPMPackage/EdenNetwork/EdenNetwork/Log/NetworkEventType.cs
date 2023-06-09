namespace EdenNetwork.Log;

public enum NetworkEventType
{
	Connect,
	Disconnect,
	Send,
	RequestTo,
	RequestFrom,
	Receive,
	ResponseTo,
	ResponseFrom,
	NotFormattedPacket
}