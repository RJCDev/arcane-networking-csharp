using ArcaneNetworking;
using Godot;
using System;

public partial class NetworkSimulations : Node
{
    bool handshakeFinished = false;
    RandomNumberGenerator rand;
    [Export(PropertyHint.Range, "0, 1, 0.05")] float PacketDropRate = 0;
    public override void _Ready()
    {
        rand = new();
        MessageLayer.Active.OnClientSend += OnClientSend;
    }
    void OnClientSend(ArraySegment<byte> bytes)
    {
        if (!handshakeFinished)
        {
            handshakeFinished = true;
            return;
        }
        
        
        if (rand.Randf() < PacketDropRate)
        {
            throw new Exception("[Server Simulation] Dropping Packet!");
        }
       
    }

}
