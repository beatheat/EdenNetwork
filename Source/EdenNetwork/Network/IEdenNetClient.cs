using EdenNetwork.Packet;

namespace EdenNetwork;

public interface IEdenNetClient
{
	public void Close();
	
	public void SetDefaultTimeout(double timeout);
	
	public void SetSerializer(IEdenDataSerializer serializer);

	public void AddEndpoints(params object[] endpoints);

	public void RemoveEndpoints(params object[] endpoints);
	
	public ConnectionState Connect(double timeout = -1);
	
	public Task<ConnectionState> ConnectAsync(double timeout = -1);
	
	public void Send(string tag, object? data = null);

	public T? Request<T>(string tag, object? data = null, double timeout = -1);

	public Task SendAsync(string tag, object? data = null);
	
	public Task<T?> RequestAsync<T>(string tag, object? data = null, double timeout = -1);
}