using Godot;
using MessagePack;
using MethodDecorator.Fody.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ArcaneNetworking;

/// <summary>
/// If you wish to block the method from running locally, simply block the method with a return statement if we are not the server
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class MethodRPCAttribute : Attribute, IMethodDecorator
{
    public SendTime SendTime;
    public Channels SendChannel;
    bool RunLocally = true;
    bool ShouldRelay = true;
    MethodBase Method;
    NetworkedComponent Instance;
    object[] MethodArgs;

    //if relay is changed, then we will NOT have the host send the data to all clients as well
    public MethodRPCAttribute(Channels channel = Channels.Reliable, SendTime sendTime = SendTime.Physics, bool runLocally = true, bool shouldRelay = true)
    {
        SendTime = sendTime;
        SendChannel = channel;
        ShouldRelay = shouldRelay;
        RunLocally = runLocally;
    }

    public void Init(object instance, MethodBase method, object[] args)
    {
        Instance = instance as NetworkedComponent;
        Method = method;
        MethodArgs = args;
    }

    public void OnEntry()
    {
        NetworkedNode NetNode = Instance.NetworkedNode;
        
        if (!NetNode.AmIOwner) // Don't send if object isn't owned by us
        {
            throw new NotNetworkOwnerException("Instance is not owner of NetworkObject!");
        }

        ushort StorageMethodID = NetworkStorage.Singleton.MethodToID((MethodInfo)Method);

        // Create the packet
        RPCPacket packet = new()
        {
            CallerNetID = NetNode.NetID,
            CallerCompIndex = Instance.GetIndex(),
            CallerMethodID = StorageMethodID,
            ShouldRelay = ShouldRelay,
            Args = MethodArgs
        };

        // You are JUST a client, send to server host
        if (NetworkManager.AmIClient)
        {
            Client.Send(packet, SendChannel);

            if (!RunLocally) throw new RPCNotServerException(); // Don't run as a client locally            
        }
        else // We just send to all active connections if we are the server
        {
            Server.SendAll(packet, SendChannel);
        }
    }

    public void OnExit() { }

    public void OnException(Exception exception)
    {
        if (exception is RPCNotServerException) return; // Simply just return and don't print anything
        else GD.PrintErr(exception.Message);
    }


}