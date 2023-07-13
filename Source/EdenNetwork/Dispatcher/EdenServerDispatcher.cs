using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using EdenNetwork.EdenException;
using EdenNetwork.Packet;

namespace EdenNetwork.Dispatcher;

internal class EdenServerDispatcher
{
	private readonly Dictionary<string, ServerReceiveResponseEndpoint> _receiveEndpoints;
	private readonly Dictionary<string, ServerReceiveResponseEndpoint> _responseEndpoints;
	private readonly List<ClientConnectEndpoint> _connectEndpoints;
	private readonly List<ClientDisconnectEndpoint> _disconnectEndpoints;
	private NatEndpoint? _natRelayEndpoint;
	
	private readonly EdenPacketSerializer _serializer;

	
	public EdenServerDispatcher(EdenPacketSerializer serializer)
	{
		_receiveEndpoints = new Dictionary<string, ServerReceiveResponseEndpoint>();
		_responseEndpoints = new Dictionary<string, ServerReceiveResponseEndpoint>();
		_connectEndpoints = new List<ClientConnectEndpoint>();
		_disconnectEndpoints = new List<ClientDisconnectEndpoint>();
		_natRelayEndpoint = null;
		
		_serializer = serializer;
	}

	//Reflection 메소드 캐싱 가능
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
				if(attribute == null)
					continue;
				var attributeType = attribute.GetType();
				
				if (attributeType == typeof(EdenReceiveAttribute))
				{
					var argumentType = ValidateReceiveResponseMethod(methodInfo);
					
					var endpoint = new ServerReceiveResponseEndpoint {Owner = endpointObject, Name = methodInfo.Name, Logic = DispatchInvoker.GetServerEndpointLogicInvoker(endpointObject, methodInfo)};
					if (argumentType != null)
						endpoint.DataDeserializer = DispatchInvoker.GetDeserializeDataInvoker(_serializer, argumentType);
					
					var receiveAttribute = (EdenReceiveAttribute)attribute;
					receiveAttribute.apiName ??= methodInfo.Name;

					if (_receiveEndpoints.TryAdd(receiveAttribute.apiName, endpoint) == false)
					{
						throw new EdenDispatcherException($"Same Name of Endpoint Logic Method Exist - Class Name : {endpointTypeInfo.Name} Method Name : {methodInfo.Name}");
					}
				}
				else if (attributeType == typeof(EdenResponseAttribute))
				{
					var argumentType = ValidateReceiveResponseMethod(methodInfo);
					var endpoint = new ServerReceiveResponseEndpoint {Owner = endpointObject, Name = methodInfo.Name, Logic = DispatchInvoker.GetServerEndpointLogicInvoker(endpointObject, methodInfo)};
					if (argumentType != null)
						endpoint.DataDeserializer = DispatchInvoker.GetDeserializeDataInvoker(_serializer, argumentType);
					
					var responseAttribute = (EdenResponseAttribute)attribute;
					responseAttribute.apiName ??= methodInfo.Name;
					
					if (_responseEndpoints.TryAdd(responseAttribute.apiName, endpoint) == false)
					{
						throw new EdenDispatcherException($"Same Name of Endpoint Logic Method Exist - Class Name : {endpointTypeInfo.Name} Method Name : {methodInfo.Name}");
					}
				}
				else if (attributeType == typeof(EdenClientConnectAttribute))
				{

					ValidateClientConnectMethod(methodInfo);
					
					var endpoint = new ClientConnectEndpoint {Owner = endpointObject, Name = methodInfo.Name, Logic = methodInfo.CreateDelegate<ClientConnectLogicInvoker>(endpointObject)};
					_connectEndpoints.Add(endpoint);
				}
				else if (attributeType == typeof(EdenClientDisconnectAttribute))
				{
					ValidateClientDisconnectMethod(methodInfo);
					var endpoint = new ClientDisconnectEndpoint {Owner = endpointObject, Name = methodInfo.Name, Logic = methodInfo.CreateDelegate<ClientDisconnectLogicInvoker>(endpointObject)};
					_disconnectEndpoints.Add(endpoint);
				}
				else if (attributeType == typeof(EdenNatRelayAttribute))
				{
					if (_natRelayEndpoint != null)
						throw new EdenDispatcherException($"NAT Relay Method Could Exist Only One - Class Name : {endpointTypeInfo.Name} Method Name : {methodInfo.Name}");
					ValidateNatRelayMethod(methodInfo);
					var endpoint = new NatEndpoint {Owner = endpointObject, Name = methodInfo.Name, Logic = methodInfo.CreateDelegate<NatEndpointLogicInvoker>(endpointObject)};
					_natRelayEndpoint = endpoint;
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

				if (attribute == null)
					continue;

				var attributeType = attribute.GetType();
				if (attributeType == typeof(EdenReceiveAttribute))
				{
					var receiveAttribute = (EdenReceiveAttribute) attribute;
					receiveAttribute.apiName ??= methodInfo.Name;
					_receiveEndpoints.Remove(receiveAttribute.apiName);
				}
				else if (attributeType == typeof(EdenResponseAttribute))
				{
					var receiveAttribute = (EdenResponseAttribute) attribute;
					receiveAttribute.apiName ??= methodInfo.Name;
					_responseEndpoints.Remove(receiveAttribute.apiName);
				}
				else if (attributeType == typeof(EdenClientConnectAttribute))
				{
					var connectEndpoint = _connectEndpoints.Find(endpoint => endpoint.Owner == endpointObject);
					if (connectEndpoint != null) _connectEndpoints.Remove(connectEndpoint);
				}
				else if (attributeType == typeof(EdenClientDisconnectAttribute))
				{
					var disconnectEndpoint = _disconnectEndpoints.Find(endpoint => endpoint.Owner == endpointObject);
					if (disconnectEndpoint != null) _disconnectEndpoints.Remove(disconnectEndpoint);
				}
				else if (attributeType == typeof(EdenNatRelayAttribute))
				{
					_natRelayEndpoint = null;
				}
			}
		}
	}

	
	public void DispatchSendPacket(PeerId peerId, EdenPacket packet)
	{
		// Ignore Unknown Packet
		if (!_receiveEndpoints.TryGetValue(packet.Tag, out var endpoint))
			return;
		try
		{
			var packetData = endpoint.DataDeserializer?.Invoke((byte[]) packet.Data!);
			endpoint.Logic(peerId, packetData);
		}
		catch (Exception e)
		{
			throw new EdenDispatcherException("DispatchSend Error at " + endpoint.Name +"\n" + e.Message);
		}
	}

	public object? DispatchRequestPacket(PeerId peerId, EdenPacket packet)
	{
		// Response null for unknown api
		if (!_responseEndpoints.TryGetValue(packet.Tag, out var endpoint))
			return null;

		object? responseData;
		try
		{
			var packetData = endpoint.DataDeserializer?.Invoke((byte[]) packet.Data!);
			responseData = endpoint.Logic(peerId, packetData);
		}
		catch (Exception e)
		{
			throw new EdenDispatcherException("DispatchRequest Error at " + endpoint.Name + "\n" + e.InnerException?.Message + "\n" + Encoding.Default.GetString((byte[])packet.Data!));
		}

		return responseData;
	}

	public void DispatchConnectMessage(PeerId peerId)
	{
		foreach (var endpoint in _connectEndpoints)
		{
			endpoint.Logic(peerId);		
		}
	}
	
	public void DispatchDisconnectMessage(PeerId peerId, DisconnectReason reason)
	{
		foreach (var endpoint in _disconnectEndpoints)
		{
			endpoint.Logic(peerId, reason);
		}
	}

	public NatPeer? DispatchNatRelayMessage(NatPeer natPeer, string additionalData)
	{
		return _natRelayEndpoint?.Logic(natPeer, additionalData);
	}
	
	private Type? ValidateReceiveResponseMethod(MethodInfo methodInfo)
	{
		var parameterInfos = methodInfo.GetParameters();
		if (parameterInfos.Length > 2)
		{
			throw new EdenDispatcherException($"Invalid Receive/Response Method : Wrong Parameter Count - Method Name: {methodInfo.Name}");
		}
		var clientIdParameter = parameterInfos[0];
		if (clientIdParameter.ParameterType != typeof(PeerId))
		{
			throw new EdenDispatcherException($"Invalid Receive/Response Method : Wrong Parameter Type - Method Name: {methodInfo.Name}");
		}

		if (parameterInfos.Length == 1)
			return null;
		
		var dataType = parameterInfos[1].ParameterType;
		return dataType;
	}
	

	private void ValidateClientConnectMethod(MethodInfo methodInfo)
	{
		var parameterInfos = methodInfo.GetParameters();
		if (parameterInfos.Length != 1)
		{
			throw new EdenDispatcherException($"Invalid Connect Method : Wrong Parameter Count - Method Name : {methodInfo.Name}");
		}

		if (parameterInfos[0].ParameterType != typeof(PeerId))
		{
			throw new EdenDispatcherException($"Invalid Connect Method : Wrong Parameter Type - Method Name : {methodInfo.Name}");
		}
		
	}
	
	private void ValidateClientDisconnectMethod(MethodInfo methodInfo)
	{
		var parameterInfos = methodInfo.GetParameters();
		if (parameterInfos.Length != 2)
		{
			throw new EdenDispatcherException($"Invalid Disconnect Method : Wrong Parameter Count - Method Name : {methodInfo.Name}");
		}

		if (parameterInfos[0].ParameterType != typeof(PeerId))
		{
			throw new EdenDispatcherException($"Invalid Disconnect Method : Wrong Parameter Type - Method Name : {methodInfo.Name}");
		}

		if (parameterInfos[1].ParameterType != typeof(DisconnectReason))
		{
			throw new EdenDispatcherException($"Invalid Disconnect Method : Wrong Parameter Type - Method Name : {methodInfo.Name}");
		}
	}
	
	private void ValidateNatRelayMethod(MethodInfo methodInfo)
	{
		var parameterInfos = methodInfo.GetParameters();
		if (parameterInfos.Length != 2)
		{
			throw new EdenDispatcherException($"Invalid NAT Relay Method : Wrong Parameter Count - Method Name : {methodInfo.Name}");
		}

		if (parameterInfos[0].ParameterType != typeof(NatPeer))
		{
			throw new EdenDispatcherException($"Invalid NAT Relay Method : Wrong Parameter Type - Method Name : {methodInfo.Name}");
		}
		
		
		if (parameterInfos[1].ParameterType != typeof(string))
		{
			throw new EdenDispatcherException($"Invalid NAT Relay Method : Wrong Parameter Type - Method Name : {methodInfo.Name}");
		}


		if (methodInfo.ReturnType != typeof(NatPeer))
		{
			throw new EdenDispatcherException($"Invalid NAT Relay Method : Wrong Parameter Type - Method Name : {methodInfo.Name}");
		}
	}
	
}