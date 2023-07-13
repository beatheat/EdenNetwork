using System.Text;
using EdenNetwork.EdenException;
using ProtoBuf;

namespace EdenNetwork.Packet;

internal class EdenDataSerializer : IEdenDataSerializer
{
	public byte[] Serialize<T>(T data)
	{
		try
		{
			var memoryStream = new MemoryStream();
			Serializer.Serialize(memoryStream, data);
			return memoryStream.ToArray();
		}
		catch (Exception e)
		{
			throw new EdenSerializerException(e, data!, $"Data Serialization Fail\n" + e.Message);
		}
	}

	public T Deserialize<T>(byte[] packetSerialized)
	{
		try
		{
			var memoryStream = new MemoryStream(packetSerialized);
			return Serializer.Deserialize<T>(memoryStream);
		}
		catch (Exception e)
		{
			throw new EdenSerializerException(e, $"Data Deserialization Fail({Encoding.Default.GetString(packetSerialized)})\n" + e.Message);
		}
	}
}