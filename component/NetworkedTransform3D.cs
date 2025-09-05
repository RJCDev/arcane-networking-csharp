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
   
    public float snapShotInterval = 1f / Engine.PhysicsTicksPerSecond;
    float snapshotTimer = 0;
    float Latency => (Current.SnaphotTime - Previous.SnaphotTime) / 1000.0f;

    public TransformSnapshot Previous = new(), Current = new();

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
        if ((AuthorityMode == AuthorityMode.Client && NetworkedNode.AmIOwner) // Client Authority
        || AuthorityMode == AuthorityMode.Server && NetworkManager.AmIServer) // Server Authority
        {
            Changed changes = Changed.None;
            List<float> valuesChanged = [];

            // Pos
            if (SyncPosition)
            {
                if (Current.Pos.X != TransformNode.GlobalPosition.X) { changes |= Changed.PosX; valuesChanged.Add(TransformNode.GlobalPosition.X); }
                if (Current.Pos.Y != TransformNode.GlobalPosition.Y) { changes |= Changed.PosY; valuesChanged.Add(TransformNode.GlobalPosition.Y); }
                if (Current.Pos.Z != TransformNode.GlobalPosition.Z) { changes |= Changed.PosZ; valuesChanged.Add(TransformNode.GlobalPosition.Z); }
            }

            // Rot
            if (SyncRotation)
            {
                Vector3 compressed = CompQuat(TransformNode.Quaternion); // Compress to fit into Vector3

                if (Current.Rot.X != compressed.X) { changes |= Changed.RotX; valuesChanged.Add(compressed.X); }
                if (Current.Rot.Y != compressed.Y) { changes |= Changed.RotY; valuesChanged.Add(compressed.Y); }
                if (Current.Rot.Z != compressed.Z) { changes |= Changed.RotZ; valuesChanged.Add(compressed.Z); }
            }

            // Scale
            if (SyncScale)
            {
                if (Current.Scale != TransformNode.Scale) { changes |= Changed.Scale; valuesChanged.Add(TransformNode.Scale.X); valuesChanged.Add(TransformNode.Scale.Y); valuesChanged.Add(TransformNode.Scale.Z); }
            }


            // Send RPC if changes occured
            if (changes != Changed.None)
            {
                uint[] send = null;
                if (NetworkManager.AmIClientOnly) send = [Client.serverConnection.GetRemoteID()];
                else if (NetworkManager.AmIServer) send = Server.GetConnsExcluding(Client.serverConnection.localID, NetworkedNode.OwnerID);
                // Send
                if (send.Length > 0) Set(send, changes, [.. valuesChanged]);

            }

        }
       
    }
    public override void _Process(double delta)
    {
        if (NetworkedNode.AmIOwner) return;

        snapshotTimer += (float)delta;

        float t = (float)(snapshotTimer / snapShotInterval) * Latency;
        t = Math.Clamp(t, 0f, 1f);

        // Process the samples if we aren't owner
        TransformSnapshot Interp = Previous.InterpWith(Current, t);
        TransformNode.GlobalPosition = Interp.Pos;
        TransformNode.Quaternion = Interp.Rot;
        TransformNode.Scale = Interp.Scale;

        if (t == 1) Previous = Current; // Set previous to current // Done Interpolating

    }

    [MethodRPC(Channels.Unreliable, true)]
    public void Set(uint[] connsToSendTo, Changed changed, float[] valuesChanged)
    {
        if (!NetworkedNode.AmIOwner)
        {
            // Read new current
            ReadSnapshot(changed, valuesChanged);
            Current.SnaphotTime = Time.GetTicksMsec();

            snapshotTimer = 0;
            
            // Relay logic
            if (NetworkManager.AmIServer)
            {
                uint[] relayConnections = Server.GetConnsExcluding(Client.serverConnection.localID, NetworkedNode.OwnerID);

                if (relayConnections.Length > 0)
                    Set(relayConnections, changed, valuesChanged);
            }
        }
    }


    /// <summary>
    /// Read a snapshot from values changed (delta)
    /// </summary>
    void ReadSnapshot(Changed changed, float[] valuesChanged)
    {
        int readIndex = 0;

        Current.Pos = new()
        {
            X = (changed & Changed.PosX) > 0 ? valuesChanged[readIndex++] : TransformNode.GlobalPosition.X,
            Y = (changed & Changed.PosY) > 0 ? valuesChanged[readIndex++] : TransformNode.GlobalPosition.Y,
            Z = (changed & Changed.PosZ) > 0 ? valuesChanged[readIndex++] : TransformNode.GlobalPosition.Z,
        };

        Current.Rot = DecompQuat(new()
        {
            X = (changed & Changed.RotX) > 0 ? valuesChanged[readIndex++] : TransformNode.Quaternion.X,
            Y = (changed & Changed.RotY) > 0 ? valuesChanged[readIndex++] : TransformNode.Quaternion.Y,
            Z = (changed & Changed.RotZ) > 0 ? valuesChanged[readIndex++] : TransformNode.Quaternion.Z,
        });

        bool updateScale = (changed & Changed.Scale) > 0;

        Current.Scale = new()
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

    public static Vector3 CompQuat(Quaternion q)
    {
        // Ensure w is non-negative (canonical form)
        if (q.W < 0f) q = -q;

        return new Vector3(q.X, q.Y, q.Z);
    }


    public static Quaternion DecompQuat(Vector3 v)
    {
        float wSquared = 1f - (v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        float w = wSquared > 0f ? (float)Math.Sqrt(wSquared) : 0f;

        return new Quaternion(v.X, v.Y, v.Z, w);
    }

    // A transform snapshot
    public struct TransformSnapshot
    {
        public Vector3 Pos;
        public Quaternion Rot;
        public Vector3 Scale;

        public ulong SnaphotTime;
        public TransformSnapshot()
        {
            Pos = Vector3.Zero;
            Rot = Quaternion.Identity;
            Scale = Vector3.One;
        }
        /// <summary>
        /// TO Extrapolate, use a value larger than 1 for amount
        /// </summary>
        /// <returns>A TransformSnapshot that has been Transformed from this TransformSnapshot To "After"</returns>
        public TransformSnapshot InterpWith(TransformSnapshot other, float amount)
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

