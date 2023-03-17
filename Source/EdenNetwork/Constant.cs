namespace EdenNetwork
{
	public static class Constant
	{
		/// <summary>
		/// Constant : size of read buffer for each client 8KB
		/// </summary>
		public const int DEFAULT_BUFFER_SIZE = 8 * 1024;
		/// <summary>
		/// Constant : constant for receive request packet
		/// </summary>
		public const string REQUEST_PREFIX = "*r*";
		/// <summary>
		/// Constant : size of packet length field
		/// </summary>
		public const int PACKET_LENGTH_BUFFER_SIZE = 4;
		
	}
}

