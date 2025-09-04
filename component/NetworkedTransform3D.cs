using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ArcaneNetworking;

[GlobalClass]
public partial class NetworkedTransform3D : NetworkedComponent
{
    Node3D TransformNode;
    [Export] public SendTime SendTime = SendTime.Physics;

    [Export] public Channels SendChannel = Channels.Reliable;

    [ExportCategory("What To Sync")]
    [Export] public bool SyncPosition;
    [Export] public bool SyncRotation;
    [Export] public bool SyncScale;

    [ExportCategory("Interpolation And Corrections")]
    [Export] public bool LinearInterpolation = true;
    [Export] public float InterpSpeed = 0.5f;


    public Vector3 ServerPos = Vector3.Zero;
    public Vector3 ServerRot = Vector3.Zero;
    public Vector3 ServerScale = Vector3.Zero;

    public override void _Ready()
    {
        if (NetworkedNode.Node is not Node3D)
        {
            GD.PushError("(Network Transform) Networked Node's Parent is NOT a Node3D!");
        }
        else
        {
            // Set Defaults
            TransformNode = NetworkedNode.Node as Node3D;
        }

    }
    public override void _PhysicsProcess(double delta)
    {
        // Update Position
        if (NetworkedNode.AmIOwner)
        {
            Changed changes = Changed.None;
            List<float> valuesChanged = [];

            // Pos
            if (SyncPosition)
            {
                if (ServerPos.X != TransformNode.GlobalPosition.X) { changes |= Changed.PosX; valuesChanged.Add(TransformNode.GlobalPosition.X); }
                if (ServerPos.Y != TransformNode.GlobalPosition.Y) { changes |= Changed.PosY; valuesChanged.Add(TransformNode.GlobalPosition.Y); }
                if (ServerPos.Z != TransformNode.GlobalPosition.Z) { changes |= Changed.PosZ; valuesChanged.Add(TransformNode.GlobalPosition.Z); }
            }

            // Rot
            if (SyncRotation)
            {
                if (ServerRot.X != TransformNode.GlobalRotation.X) { changes |= Changed.RotX; valuesChanged.Add(TransformNode.GlobalRotation.X); }
                if (ServerRot.Y != TransformNode.GlobalRotation.Y) { changes |= Changed.RotY; valuesChanged.Add(TransformNode.GlobalRotation.Y); }
                if (ServerRot.Z != TransformNode.GlobalRotation.Z) { changes |= Changed.RotZ; valuesChanged.Add(TransformNode.GlobalRotation.Z); }
            }

            // Scale
            if (SyncScale)
            {
                if (ServerScale != TransformNode.Scale) { changes |= Changed.Scale; valuesChanged.Add(TransformNode.Scale.X); valuesChanged.Add(TransformNode.Scale.Y); valuesChanged.Add(TransformNode.Scale.Z); }
            }
            

            // Send RPC if changes occured
            if (changes != Changed.None)
            {
                // Send to others (server if authority mode is client, all clients if authority mode is server)
                Set(AuthorityMode == AuthorityMode.Client ? [Client.serverConnection.GetID()] : [.. Server.Connections.Keys], changes, [.. valuesChanged]);
            }

        }
        else
        {
            // Process the interpolation if we aren't owner
            TransformNode.GlobalPosition = TransformNode.GlobalPosition.Lerp(ServerPos, InterpSpeed);
            TransformNode.GlobalRotation = TransformNode.GlobalRotation.Lerp(ServerRot, InterpSpeed);
            TransformNode.Scale = TransformNode.Scale.Lerp(ServerScale, InterpSpeed);

        }
       
    }

    [MethodRPC(Channels.Unreliable)]
    public void Set(uint[] connsToSendTo, Changed changed, float[] valuesChanged)
    {
        // Set our current state
        if (NetworkedNode.AmIOwner) // OnSend
        {
            GD.Print("[Client] Sending From: " + NetworkedNode.NetID);

            // Update Server Pos because are owner
            ServerPos = TransformNode.GlobalPosition;
            ServerRot = TransformNode.GlobalRotation;
            ServerScale = TransformNode.Scale;
            
        }
        else // OnReceive
        {
            GD.Print("[Client] Reading For: "+ NetworkedNode.NetID);

            // Read the "Current" snapshot we just got into our server transform data
            // We will interpolate back in process loop
            ReadSnapshot(changed, valuesChanged);

            // If we recieve and we are the server we need to relay
            if (NetworkManager.AmIServer)
            {
                GD.Print("[Server] Relaying For: "+ NetworkedNode.NetID); // If im headless, send to all, if not, then send to all but our local connection, and the owner of this object
                Set(NetworkManager.AmIHeadless ? [.. Server.Connections.Keys] : Server.GetConnsExcluding(Client.connectionIDToServer, NetworkedNode.OwnerID), changed, valuesChanged);
            }
        }
              
    }

    /// <summary>
    /// Read a snapshot from values changed (delta)
    /// </summary>
    void ReadSnapshot(Changed changed, float[] valuesChanged)
    {
        int readIndex = 0;

        ServerPos = new()
        {
            X = (changed & Changed.PosX) > 0 ? valuesChanged[readIndex++] : TransformNode.GlobalPosition.X,
            Y = (changed & Changed.PosY) > 0 ? valuesChanged[readIndex++] : TransformNode.GlobalPosition.Y,
            Z = (changed & Changed.PosZ) > 0 ? valuesChanged[readIndex++] : TransformNode.GlobalPosition.Z,
        };

        ServerRot = new()
        {
            X = (changed & Changed.RotX) > 0 ? valuesChanged[readIndex++] : TransformNode.GlobalRotation.X,
            Y = (changed & Changed.RotY) > 0 ? valuesChanged[readIndex++] : TransformNode.GlobalRotation.Y,
            Z = (changed & Changed.RotZ) > 0 ? valuesChanged[readIndex++] : TransformNode.GlobalRotation.Z,
        };

        bool updateScale = (changed & Changed.Scale) > 0;

        ServerScale = new()
        {
            X = updateScale ? valuesChanged[readIndex++] : TransformNode.Scale.X,
            Y = updateScale ? valuesChanged[readIndex++] : TransformNode.Scale.Y,
            Z = updateScale ? valuesChanged[readIndex++] : TransformNode.Scale.Z,
        };
    }

    // A byte describing what part of the transform was changed

    public enum Changed : byte
    {
        None = 0,
        PosX = 1 << 0,
        PosY = 1 << 1,
        PosZ = 1 << 2,
        RotX = 1 << 4,
        RotY = 1 << 5,
        RotZ = 1 << 6,
        Scale = 1 << 7,
    }


    // A transform snapshot
    public struct TransformSnapshot
    {
        public Vector3 Pos;
        public Vector3 Rot;
        public Vector3 Scale;

        /// <summary>
        /// TO Extrapolate, use a value larger than 1 for amount
        /// </summary>
        /// <returns>A TransformSnapshot that has been Transformed from this TransformSnapshot To "After"</returns>
        public TransformSnapshot TransformWith(TransformSnapshot other, float amount)
        {
            return new()
            {
                Pos = Pos.Lerp(other.Pos, amount),
                Rot = Rot.Slerp(other.Rot, amount),
                Scale = Scale.Lerp(other.Scale, amount)
            };
        }

        public override bool Equals([NotNullWhen(true)] object obj)
        {
            if (obj is TransformSnapshot s)
            {
                return Pos == s.Pos && Rot == s.Rot && Scale == s.Scale;
            }
            else return false;
            
        }
        public static bool operator ==(TransformSnapshot left, TransformSnapshot right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(TransformSnapshot left, TransformSnapshot right)
        {
            return !(left == right);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Pos.GetHashCode(), Rot.GetHashCode(), Scale.GetHashCode());
        }

    }

}

