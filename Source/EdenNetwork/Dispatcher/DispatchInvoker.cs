using System.Reflection;
using System.Reflection.Emit;
using EdenNetwork.Packet;

namespace EdenNetwork.Dispatcher;

public delegate object DeserializeDataInvoker(byte[]? serializedData);
public delegate object ServerEndpointLogicInvoker(PeerId peerId, object? data);
public delegate object ClientEndpointLogicInvoker(object? data);
public delegate void ClientConnectLogicInvoker(PeerId peerId);
public delegate void ClientDisconnectLogicInvoker(PeerId peerId, DisconnectReason reason);
public delegate void ServerDisconnectLogicInvoker(DisconnectReason reason); 
public delegate NatPeer NatEndpointLogicInvoker(NatPeer natPeer, string additionalData);

internal static class DispatchInvoker
{
	//TODO: DynamicMethod 생성 캐싱가능
	public static DeserializeDataInvoker GetDeserializeDataInvoker(EdenPacketSerializer serializer, Type dataType)
	{
		var deserializeMethodInfo = serializer.GetType().GetMethod(nameof(EdenPacketSerializer.DeserializeData))!.MakeGenericMethod(dataType);
		
		DynamicMethod dm = new DynamicMethod($"Deserialize_{Guid.NewGuid():N}", typeof(object), new[] {typeof(EdenPacketSerializer), typeof(byte[])}, typeof(EdenPacketSerializer), true);
		ILGenerator il = dm.GetILGenerator();
		il.Emit(OpCodes.Ldarg_0);
		il.Emit(OpCodes.Ldarg_1);
		il.Emit(OpCodes.Call, deserializeMethodInfo);
		if(dataType.IsPrimitive)
			il.Emit(OpCodes.Box, dataType);
		il.Emit(OpCodes.Ret);
		return dm.CreateDelegate<DeserializeDataInvoker>(serializer);
	}


	public static ClientEndpointLogicInvoker GetClientEndpointLogicInvoker(object owner, MethodInfo endpointMethodInfo)
	{
		ParameterInfo? dataInfo = null;
		var endpointParameters = endpointMethodInfo.GetParameters();
		if (endpointParameters.Length > 0)
		{
			dataInfo = endpointParameters[0];
		}
		
		DynamicMethod dm = new DynamicMethod($"{endpointMethodInfo.Name}_{Guid.NewGuid():N}", typeof(object), new[] {owner.GetType(), typeof(object)}, owner.GetType(), true);
		ILGenerator il = dm.GetILGenerator();
		
		il.Emit(OpCodes.Ldarg_0);
		if (dataInfo != null)
		{
			il.Emit(OpCodes.Ldarg_1);
			if (dataInfo.ParameterType.IsPrimitive)
				il.Emit(OpCodes.Unbox_Any, dataInfo.ParameterType);
		}

		il.Emit(OpCodes.Call, endpointMethodInfo);

		if (endpointMethodInfo.ReturnType.IsPrimitive)
			il.Emit(OpCodes.Box, endpointMethodInfo.ReturnType);
		
		if(endpointMethodInfo.ReturnType == typeof(void))
			il.Emit(OpCodes.Ldnull);
		
		il.Emit(OpCodes.Ret);

		return dm.CreateDelegate<ClientEndpointLogicInvoker>(owner);
	}

	public static ServerEndpointLogicInvoker GetServerEndpointLogicInvoker(object owner, MethodInfo endpointMethodInfo)
	{
		ParameterInfo? dataInfo = null;
		var endpointParameters = endpointMethodInfo.GetParameters();
		if (endpointParameters.Length > 1)
		{
			dataInfo = endpointParameters[1];
		}
		
		DynamicMethod dm = new DynamicMethod($"{endpointMethodInfo.Name}_{Guid.NewGuid():N}", typeof(object), new[] {owner.GetType(), typeof(PeerId), typeof(object)}, owner.GetType(), true);
		ILGenerator il = dm.GetILGenerator();
		
		il.Emit(OpCodes.Ldarg_0);
		il.Emit(OpCodes.Ldarg_1);
		if (dataInfo != null)
		{
			il.Emit(OpCodes.Ldarg_2);
			if (dataInfo.ParameterType.IsPrimitive)
				il.Emit(OpCodes.Unbox_Any, dataInfo.ParameterType);
		}

		il.Emit(OpCodes.Call, endpointMethodInfo);

		if (endpointMethodInfo.ReturnType.IsPrimitive)
			il.Emit(OpCodes.Box, endpointMethodInfo.ReturnType);
		
		if(endpointMethodInfo.ReturnType == typeof(void))
			il.Emit(OpCodes.Ldnull);
		
		il.Emit(OpCodes.Ret);

		return dm.CreateDelegate<ServerEndpointLogicInvoker>(owner);
	}
}