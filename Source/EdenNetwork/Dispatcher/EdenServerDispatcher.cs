﻿using System.Reflection;
using System.Text;
using EdenNetwork.EdenException;
using EdenNetwork.Packet;

namespace EdenNetwork.Dispatcher;

internal class EdenServerDispatcher
{
	private readonly Dictionary<string, Endpoint> _receiveEndpoints;
	private readonly Dictionary<string, Endpoint> _responseEndpoints;
	private readonly List<Endpoint> _disconnectEndpoints;
	private readonly List<Endpoint> _connectEndpoints;
	private Endpoint? _natRelayEndpoint;

	private readonly EdenPacketSerializer _serializer;
	
	public EdenServerDispatcher(EdenPacketSerializer serializer)
	{
		_receiveEndpoints = new Dictionary<string, Endpoint>();
		_responseEndpoints = new Dictionary<string, Endpoint>();
		_disconnectEndpoints = new List<Endpoint>();
		_connectEndpoints = new List<Endpoint>();
		_natRelayEndpoint = null;
		
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
				var endpoint = new Endpoint {Owner = endpointObject, Logic = methodInfo};
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
					endpoint.ArgumentType = ValidateReceiveResponseMethod(methodInfo);
					var receiveAttribute = (EdenReceiveAttribute)attribute;
					receiveAttribute.apiName ??= methodInfo.Name;
					
					if(endpoint.ArgumentType != null)	
						endpoint.DataDeserializer = _serializer.GetType().GetMethod(nameof(EdenPacketSerializer.DeserializeData))!.MakeGenericMethod(endpoint.ArgumentType);
					
					if (_receiveEndpoints.TryAdd(receiveAttribute.apiName, endpoint) == false)
					{
						throw new EdenDispatcherException($"Same Name of Endpoint Logic Method Exist - Class Name : {endpointTypeInfo.Name} Method Name : {methodInfo.Name}");
					}
				}
				else if (attributeType == typeof(EdenResponseAttribute))
				{
					endpoint.ArgumentType = ValidateReceiveResponseMethod(methodInfo);
					var responseAttribute = (EdenResponseAttribute)attribute;
					responseAttribute.apiName ??= methodInfo.Name;
					
					if(endpoint.ArgumentType != null)
						endpoint.DataDeserializer = _serializer.GetType().GetMethod(nameof(EdenPacketSerializer.DeserializeData))!.MakeGenericMethod(endpoint.ArgumentType);

					if (_responseEndpoints.TryAdd(responseAttribute.apiName, endpoint) == false)
					{
						throw new EdenDispatcherException($"Same Name of Endpoint Logic Method Exist - Class Name : {endpointTypeInfo.Name} Method Name : {methodInfo.Name}");
					}
				}
				else if (attributeType == typeof(EdenClientConnectAttribute))
				{

					ValidateClientConnectMethod(methodInfo);
					_connectEndpoints.Add(endpoint);
				}
				else if (attributeType == typeof(EdenClientDisconnectAttribute))
				{
					ValidateClientDisconnectMethod(methodInfo);
					_disconnectEndpoints.Add(endpoint);
				}
				else if (attributeType == typeof(EdenNatRelayAttribute))
				{
					if (_natRelayEndpoint != null)
						throw new EdenDispatcherException($"NAT Relay Method Could Exist Only One - Class Name : {endpointTypeInfo.Name} Method Name : {methodInfo.Name}");
					ValidateNatRelayMethod(methodInfo);
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
					if (disconnectEndpoint != null) _connectEndpoints.Remove(disconnectEndpoint);
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
			if (endpoint.ArgumentType != null)
			{
				var packetData = endpoint.DataDeserializer.Invoke(_serializer, new[] {packet.Data})!;
				endpoint.Logic.Invoke(endpoint.Owner, new[] {peerId, packetData});
			}
			else
			{
				endpoint.Logic.Invoke(endpoint.Owner, new object?[] {peerId});
			}
		}
		catch (Exception e)
		{
			throw new EdenDispatcherException("DispatchSend Error at " + endpoint.Logic.Name + "\n");
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
			if (endpoint.ArgumentType != null)
			{
				var packetData = endpoint.DataDeserializer.Invoke(_serializer, new[] {packet.Data})!;
				responseData = endpoint.Logic.Invoke(endpoint.Owner, new[] {peerId, packetData});
			}
			else
			{
				responseData = endpoint.Logic.Invoke(endpoint.Owner, new object?[] {peerId});
			}
		}
		catch (Exception e)
		{
			throw new EdenDispatcherException("DispatchRequest Error at " + endpoint.Logic.Name + "\n" + e.InnerException?.Message + "\n" + Encoding.Default.GetString((byte[])packet.Data));
		}

		return responseData;
	}

	public void DispatchConnectMessage(PeerId peerId)
	{
		foreach (var endpoint in _connectEndpoints)
		{
			endpoint.Logic.Invoke(endpoint.Owner, new object?[] {peerId});
		}
	}
	
	public void DispatchDisconnectMessage(PeerId peerId, DisconnectReason reason)
	{
		foreach (var endpoint in _disconnectEndpoints)
		{
			endpoint.Logic.Invoke(endpoint.Owner, new object?[] {peerId, reason});
		}
	}

	public NatPeer? DispatchNatRelayMessage(NatPeer natPeer, string additionalData)
	{
		var opponentNatPeer = _natRelayEndpoint?.Logic.Invoke(_natRelayEndpoint.Owner, new object?[] {natPeer, additionalData});
		return (NatPeer)opponentNatPeer!;
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