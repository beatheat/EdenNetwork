using System.Reflection;
using EdenNetwork.EdenException;
using EdenNetwork.Packet;

namespace EdenNetwork.Dispatcher;

internal class EdenClientDispatcher
{
	private class ResponseData
	{
		public bool Received { get; set; }
		public byte[]? RawData { get; set; }
	}
	
	
	private readonly Dictionary<string, Endpoint> _receiveEndpoints;
	private readonly Dictionary<string, ResponseData> _responseData;
	private readonly List<Endpoint> _disconnectEndpoints;
	private readonly EdenPacketSerializer _serializer;

	public EdenClientDispatcher(EdenPacketSerializer serializer)
	{
		_receiveEndpoints = new Dictionary<string, Endpoint>();
		_responseData = new Dictionary<string, ResponseData>();
		_disconnectEndpoints = new List<Endpoint>();
		
		_serializer = serializer;
	}

	public void AddEndpoints(params object[] endpointObjects)
	{
		foreach (var endpointObject in endpointObjects)
		{
			var endpointTypeInfo = endpointObject.GetType();
			if (!endpointTypeInfo.IsClass)
			{
				throw new EdenDispatcherException($"Endpoint Is Not a Class - Class Name : {endpointTypeInfo.Name}");
			}

			var methodInfos = endpointTypeInfo.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			foreach (var methodInfo in methodInfos)
			{
				var endpoint = new Endpoint {Onwer = endpointObject, Logic = methodInfo};
				if (methodInfo.GetCustomAttribute(typeof(EdenReceiveAttribute)) != null)
				{
					endpoint.ArgumentType = ValidateReceiveMethod(methodInfo);
					if (_receiveEndpoints.TryAdd(methodInfo.Name, endpoint) == false)
					{
						throw new EdenDispatcherException($"Same Name of Endpoint Logic Method Exist - Class Name : {endpointTypeInfo.Name} Method Name : {methodInfo.Name}");
					}
				}
				else if (methodInfo.GetCustomAttribute(typeof(EdenDisconnectAttribute)) != null)
				{
					ValidateDisconnectMethod(methodInfo);
					_disconnectEndpoints.Add(endpoint);
				}
			}
		}
	}
	
	public void RemoveEndpoints(params object[] endpointObjects)
	{
		foreach (var endpointObject in endpointObjects)
		{
			var endpointTypeInfo = endpointObject.GetType();
			if (!endpointTypeInfo.IsClass)
			{
				throw new EdenDispatcherException($"Endpoint Is Not a Class - Class Name : {endpointTypeInfo.Name}");
			}
			
			var methodInfos = endpointTypeInfo.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			foreach (var methodInfo in methodInfos)
			{
				if (methodInfo.GetCustomAttribute(typeof(EdenReceiveAttribute)) != null)
				{
					_receiveEndpoints.Remove(methodInfo.Name);
				}
				else if (methodInfo.GetCustomAttribute(typeof(EdenDisconnectAttribute)) != null)
				{
					var disconnectEndpoint = _disconnectEndpoints.Find(endpoint => endpoint.Onwer == endpointObject);
					if (disconnectEndpoint != null) _disconnectEndpoints.Remove(disconnectEndpoint);
					
				}
			}
		}
	}


	public void DispatchSendPacket(EdenPacket packet)
	{
		// Ignore Unknown API
		if (!_receiveEndpoints.TryGetValue(packet.Tag, out var endpoint))
			return;
		if (endpoint.ArgumentType != null)
		{
			var dataSerializeMethod = _serializer.GetType().GetMethod(nameof(EdenPacketSerializer.DeserializeData))!.MakeGenericMethod(endpoint.ArgumentType);
			var packetData = dataSerializeMethod.Invoke(_serializer, new[] {packet.Data})!;
			endpoint.Logic.Invoke(endpoint.Onwer, new[] {packetData});
		}
		else
		{
			endpoint.Logic.Invoke(endpoint.Onwer, null);
		}
	}

	public void DispatchResponsePacket(EdenPacket packet)
	{
		// Ignore Unknown API
		if (!_responseData.ContainsKey(packet.Tag))
			return;

		_responseData[packet.Tag].Received = true;
		if(packet.Data != null)
			_responseData[packet.Tag].RawData = (byte[]) packet.Data;
	}

	
	public T? WaitResponse<T>(string tag, double timeout)
	{
		if (_responseData.ContainsKey(tag))
			throw new EdenDispatcherException($"Duplicated Request Simultaneously - API : {tag}");

		ResponseData responseData = new ResponseData {Received = false, RawData = null};
		_responseData.Add(tag, responseData);
		var time = DateTime.Now;
		
		while (DateTime.Now - time < TimeSpan.FromSeconds(timeout))
		{
			if (responseData.Received)
				break;
			Thread.Sleep(10);
		}

		if (responseData.Received == false)
		{
			throw new EdenDispatcherException($"Request Timeout - API : {tag}");
		}

		if (responseData.RawData == null)
			return default(T);
		
		return _serializer.DeserializeData<T>(responseData.RawData);
	}


	public void DispatchDisconnectMessage(DisconnectReason reason)
	{
		foreach (var endpoint in _disconnectEndpoints)
		{
			endpoint.Logic.Invoke(endpoint.Onwer, new object?[] {reason});
		}
	}
	
	Type? ValidateReceiveMethod(MethodInfo methodInfo)
	{
		var parameterInfos = methodInfo.GetParameters();
		if (parameterInfos.Length > 1)
		{
			throw new EdenDispatcherException($"Invalid Receive Method - Method Name: {methodInfo.Name}");
		}
		
		if (parameterInfos.Length == 0)
			return null;
		
		var dataType = parameterInfos[0].ParameterType;
		return dataType;
	}

	void ValidateDisconnectMethod(MethodInfo methodInfo)
	{
		var parameterInfos = methodInfo.GetParameters();
		if (parameterInfos.Length != 1)
		{
			throw new EdenDispatcherException($"Invalid Disconnect Method : Wrong Parameter Count - Method Name : {methodInfo.Name}");
		}

		var disconnectReasonParameterType = parameterInfos[0].ParameterType;
		if (disconnectReasonParameterType != typeof(DisconnectReason))
		{
			throw new EdenDispatcherException($"Invalid Disconnect Method : Wrong Parameter Type - Method Name : {methodInfo.Name}");
		}
	}

}