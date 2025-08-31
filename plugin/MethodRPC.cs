using Godot;
using MessagePack;
using MethodDecorator.Fody.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;


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
    bool ShouldSend;
    MethodBase Method;
    NetworkedComponent NetComponent;
    List<byte[]> MethodArgs;


    public MethodRPCAttribute(Channels channel = Channels.Reliable, SendTime sendTime = SendTime.Physics, bool runLocally = true, bool shouldRelay = true)
    {
        SendTime = sendTime;
        SendChannel = channel;
        ShouldRelay = shouldRelay;
        RunLocally = runLocally;
    }

    public void Init(object instance, MethodBase method, object[] args)
    {
        NetComponent = instance as NetworkedComponent;
        Method = method;
        MethodArgs = new(args.Length);

        // Serialize args
        foreach (var arg in args)
            MethodArgs.Add(MessagePackSerializer.Serialize(arg));
       
    }

    public void OnEntry()
    {
        NetworkedNode NetNode = NetComponent.NetworkedNode;

        ShouldSend = NetNode.AmIOwner; // Add mor logic later

        if (ShouldSend) // Should we be sending this RPC? (Do we Own This Object?)
        {
            // Create the packet
            RPCPacket packet = new()
            {
                CallerNetID = NetNode.NetID,
                CallerCompIndex = NetComponent.GetIndex(),
                CallerMethodID = NetworkStorage.Singleton.MethodToID((MethodInfo)Method),
                ShouldRelay = ShouldRelay,
                Args = MethodArgs
            };

            // You are a client, send to server host
            if (NetworkManager.AmIClient)
            {
                Client.Send(packet, SendChannel);
                GD.Print("[Client] Sending Method RPC!");

                if (!RunLocally) throw new RPCDontRunAsClientException(); // Don't run as a client locally            
            }
            else if (ShouldRelay) // We just send to all active connections if we are the server
            {
                Server.SendAll(packet, SendChannel);
                GD.Print("[Server] Sending Method RPC!");
            }
        }
        // If shouldn't send then we most likely are receiving this RPC, just run the method normally!
        
    }

    public void OnExit() { }

    public void OnException(Exception exception)
    {
        if (exception is RPCDontRunAsClientException) return; // Simply just return and don't print anything
        if (exception is RPCServerAuthorityException) { GD.PrintErr("[MethodRPC] Attempted to run From Client when Server Authority For Component is ON!"); return; } // Simply just return and don't print anything
        else GD.PrintErr(exception.Message);
    }


}