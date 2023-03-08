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
	}
}

