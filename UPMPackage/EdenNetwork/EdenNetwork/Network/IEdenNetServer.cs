using System.Threading.Tasks;
using EdenNetwork.Packet;

namespace EdenNetwork
{

	public interface IEdenNetServer
	{
		public void Close();

		public void SetSerializer(IEdenDataSerializer serializer);

		public void Listen(int maxAcceptNum);

		public void AddEndpoints(params object[] endpoints);

		public void RemoveEndpoints(params object[] endpoints);

		public void DisconnectClient(PeerId clientId);

		public void Send(string tag, PeerId clientId, object data);

		public void Broadcast(string tag, object data = null);

		public void BroadcastExcept(string tag, PeerId clientId, object data = null);

		public Task SendAsync(string tag, PeerId clientId, object data = null);

		public Task BroadcastAsync(string tag, object data = null);

		public Task BroadcastExceptAsync(string tag, PeerId clientId, object data = null);
	}
}