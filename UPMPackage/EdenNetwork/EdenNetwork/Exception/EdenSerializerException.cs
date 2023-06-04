using System;
using EdenNetwork.Packet;

namespace EdenNetwork.EdenException
{
	public class EdenSerializerException : Exception
	{
		public object SerializeTargetData { get; set; } = null!;
		public EdenPacket SerializeTargetPacket { get; set; } = null!;

		public EdenSerializerException()
		{
		}

		public EdenSerializerException(System.Exception inner, EdenPacket serializeTargetPacket, string message) : base(message, inner)
		{
			SerializeTargetPacket = serializeTargetPacket;
		}

		public EdenSerializerException(System.Exception inner, object serializeTargetData, string message) : base(message, inner)
		{
			SerializeTargetData = serializeTargetData;
		}

		public EdenSerializerException(string message) : base(message)
		{
		}

		public EdenSerializerException(System.Exception inner, string message) : base(message, inner)
		{
		}
	}
}