using System.Text;
using EdenNetwork.EdenException;

namespace EdenNetwork.Packet;

internal class EdenPacketSerializer
{
	/*
	 * Packet Structure
	 * 2B PacketLength
	 * 1B TagLength
	 * 1B TYPE
	 * VAR TAG
	 * VAR DATA
	 */
	
	private const int PACKET_BYTE_LENGTH = 2;
	private const int TAG_BYTE_LENGTH = 1;
	private const int TYPE_BYTE_LENGTH = 1;
	
	private IEdenDataSerializer _dataSerializer;

	public EdenPacketSerializer(IEdenDataSerializer dataSerializer)
	{
		_dataSerializer = dataSerializer;
	}

	public void SetSerializer(IEdenDataSerializer serializer)
	{
		_dataSerializer = serializer;
	}
	
	public byte[] Serialize(EdenPacket packet)
	{
		try
		{
			var memoryStream = new MemoryStream();

			var serializedType = (byte)packet.Type;
		
			var serializedTag = Encoding.UTF8.GetBytes(packet.Tag);
			var serializedTagLength = (byte)serializedTag.Length;

			byte[]? serializedData = null;
			if (packet.Data != null)
			{
				var dataSerializeMethod = _dataSerializer.GetType().GetMethod(nameof(IEdenDataSerializer.Serialize))!.MakeGenericMethod(packet.Data.GetType());
				serializedData = (byte[]) dataSerializeMethod.Invoke(_dataSerializer, new[] {packet.Data})!;
			}

			var packetLength = (ushort) (PACKET_BYTE_LENGTH + TAG_BYTE_LENGTH + TYPE_BYTE_LENGTH + serializedTag.Length + (serializedData?.Length ?? 0));

			var serializedPacketLength = BitConverter.GetBytes(packetLength);
			memoryStream.Write(serializedPacketLength);
			memoryStream.WriteByte(serializedTagLength);
			memoryStream.WriteByte(serializedType);
			memoryStream.Write(serializedTag);
			if(serializedData != null)
				memoryStream.Write(serializedData);

			var serializedPacket = memoryStream.ToArray();

			return serializedPacket;
		}
		catch (Exception e)
		{
			throw new EdenSerializerException(e, packet, "Packet Serialize Fail\n" + e.Message + "\n" + e.InnerException?.Message);
		}
	}
	
	public EdenPacket Deserialize(byte[] serializedPacket, int packetLength)
	{
		try
		{
			var memoryStream = new MemoryStream(serializedPacket);
			EdenPacket packet = new EdenPacket();
			
			var tagLength = memoryStream.ReadByte();
			var dataLength = packetLength - tagLength - PACKET_BYTE_LENGTH - TAG_BYTE_LENGTH - TYPE_BYTE_LENGTH;
		
			packet.Type = (EdenPacketType) memoryStream.ReadByte();

			var tagBuffer = new byte[tagLength];
			memoryStream.Read(tagBuffer);
			packet.Tag = Encoding.UTF8.GetString(tagBuffer);

			if (dataLength > 0)
			{
				var dataBuffer = new byte[dataLength];
				memoryStream.Read(dataBuffer);
				packet.Data = dataBuffer;
			}
			else
			{
				packet.Data = Array.Empty<byte>();
			}

			return packet;
		}
		catch (Exception e)
		{
			throw new EdenSerializerException(e, "Packet Deserialize Fail\n" + e.Message);
		}
	}
	
	public EdenPacket Deserialize(byte[] serializedPacket)
	{
		var memoryStream = new MemoryStream(serializedPacket);
		
		var packetLengthBuffer = new byte[PACKET_BYTE_LENGTH];
		memoryStream.Read(packetLengthBuffer);
		var packetLength = BitConverter.ToInt16(packetLengthBuffer);

		var serializedPacketWithoutLength = new byte[packetLength];
		memoryStream.Read(serializedPacketWithoutLength);
		return Deserialize(serializedPacketWithoutLength, packetLength);
	}
	
	
	public T DeserializeData<T>(byte[] serializedData)
	{
		return _dataSerializer.Deserialize<T>(serializedData);
	}

	public int GetPacketLength(byte[] serializedPacketLength)
	{
		var packetLength = BitConverter.ToInt16(serializedPacketLength);
		return packetLength;
	}
	
}

