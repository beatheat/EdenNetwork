namespace EdenNetwork.Packet
{

    public enum EdenPacketType : byte
    {
        Send,
        Request,
        Response
    }

    /// <summary>
    /// Struct : packet structure for EdenNetwork
    /// </summary>
    public class EdenPacket
    {
        public EdenPacketType Type { get; set; }
        public string Tag { get; set; }
        public object Data { get; set; }
    }

}