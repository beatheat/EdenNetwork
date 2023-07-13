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

	private readonly Dictionary<string, ClientReceiveEndpoint> _receiveEndpoints;
	private readonly Dictionary<string, ResponseData> _responseData;
	private readonly List<ServerDisconnectEndpoint> _disconnectEndpoints;
	private readonly EdenPacketSerializer _serializer;

	public EdenClientDispatcher(EdenPacketSerializer serializer)
	{
		_receiveEndpoints = new Dictionary<string, ClientReceiveEndpoint>();
		_responseData = new Dictionary<string, ResponseData>();
		_disconnectEndpoints = new List<ServerDisconnectEndpoint>();
		
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
				
				Attribute? attribute;
				try
				{
					attribute = methodInfo.GetCustomAttribute(typeof(EdenAttribute));
				}
				catch (Exception e)
				{
					throw new EdenDispatcherException($"Cannot Get Eden Attribute - Class Name :{endpointTypeInfo.Name} Method Name : {methodInfo.Name}\n{e.Message}");
				}
				
				if(attribute == null) continue;

				var attributeType = attribute.GetType();
				
				if (attributeType == typeof(EdenReceiveAttribute))
				{
					var argumentType = ValidateReceiveMethod(methodInfo);
					var endpoint = new ClientReceiveEndpoint {Owner = endpointObject, Logic = DispatchInvoker.GetClientEndpointLogicInvoker(endpointObject, methodInfo)};
					if (argumentType != null)
						endpoint.DataDeserializer = DispatchInvoker.GetDeserializeDataInvoker(_serializer, argumentType);
					
					var receiveAttribute = (EdenReceiveAttribute) attribute;
					receiveAttribute.apiName ??= methodInfo.Name;
					
			
					if (_receiveEndpoints.TryAdd(receiveAttribute.apiName, endpoint) == false)
					{
						throw new EdenDispatcherException($"Same Name of Endpoint Logic Method Exist - Class Name : {endpointTypeInfo.Name} Method Name : {methodInfo.Name}");
					}

				}
				else if (attributeType == typeof(EdenDisconnectAttribute))
				{
					ValidateDisconnectMethod(methodInfo);
					var endpoint = new ServerDisconnectEndpoint {Owner = endpointObject, Logic = methodInfo.CreateDelegate<ServerDisconnectLogicInvoker>(endpointObject)};
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
				Attribute? attribute;
				try
				{
					attribute = methodInfo.GetCustomAttribute(typeof(EdenAttribute));
				}
				catch (Exception e)
				{
					throw new EdenDispatcherException($"Cannot Get Eden Attribute - Class Name :{endpointTypeInfo.Name} Method Name : {methodInfo.Name}\n{e.Message}");
				}

				if (attribute == null) continue;
				
				var attributeType = attribute.GetType();
				if (attributeType == typeof(EdenReceiveAttribute))
				{
					var receiveAttribute = (EdenReceiveAttribute) attribute;
					receiveAttribute.apiName ??= methodInfo.Name;
					_receiveEndpoints.Remove(receiveAttribute.apiName);
				}
				else if (attributeType == typeof(EdenDisconnectAttribute))
				{
					var disconnectEndpoint = _disconnectEndpoints.Find(endpoint => endpoint.Owner == endpointObject);
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
		try
		{
			var packetData = endpoint.DataDeserializer?.Invoke((byte[]) packet.Data!);
			endpoint.Logic(packetData);
		}
		catch (Exception e)
		{
			throw new EdenDispatcherException("Dispatch Error at " + endpoint.Name + "\n" + e.InnerException?.Message);
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

	
	public T? WaitResponse<T>(string tag, TimeSpan timeout)
	{
		if (_responseData.ContainsKey(tag))
			throw new EdenDispatcherException($"Duplicated Request Simultaneously - API : {tag}");

		ResponseData responseData = new ResponseData {Received = false, RawData = null};
		_responseData.Add(tag, responseData);

		SpinWait.SpinUntil(() => responseData.Received, timeout);
		
		_responseData.Remove(tag);

		if (responseData.Received == false)
		{
			throw new EdenTimeoutException($"Request Timeout - API : {tag}");
		}

		if (responseData.RawData == null)
			return default(T);
		
		return _serializer.DeserializeData<T>(responseData.RawData);
	}


	public void DispatchDisconnectMessage(DisconnectReason reason)
	{
		foreach (var endpoint in _disconnectEndpoints)
		{
			endpoint.Logic(reason);
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