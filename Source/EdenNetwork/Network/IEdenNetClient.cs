using EdenNetwork.Packet;

namespace EdenNetwork;

public interface IEdenNetClient
{
	public void Close();
	
	public void SetDefaultTimeout(TimeSpan timeout);
	
	public void SetSerializer(IEdenDataSerializer serializer);

	public void AddEndpoints(params object[] endpoints);

	public void RemoveEndpoints(params object[] endpoints);
	
	public ConnectionState Connect(TimeSpan? timeout = null);
	
	public Task<ConnectionState> ConnectAsync(TimeSpan? timeout = null);
	
	public void Send(string tag, object? data = null);

	public T? Request<T>(string tag, object? data = null, TimeSpan? timeout = null);

	public Task SendAsync(string tag, object? data = null);
	
	public Task<T?> RequestAsync<T>(string tag, object? data = null, TimeSpan? timeout = null);
}