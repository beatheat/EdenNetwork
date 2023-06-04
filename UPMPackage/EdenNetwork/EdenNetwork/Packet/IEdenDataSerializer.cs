namespace EdenNetwork.Packet
{
	public interface IEdenDataSerializer
	{
		byte[] Serialize<T>(T data);
		T Deserialize<T>(byte[] packetSerialized);
	}
}