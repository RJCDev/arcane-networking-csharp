using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ArcaneNetworking;

[GlobalClass]
public partial class NetworkedTransform3D : NetworkedComponent
{
    [Export] Node3D TransformNode = null;

    [ExportCategory("What To Sync")]
    [Export] public bool SyncPosition;
    [Export] public bool SyncRotation;
    [Export] public bool SyncScale;

    [ExportCategory("Interpolation And Corrections")]
    [Export] public bool LinearInterpolation = true;
   
    public float snapShotInterval = 1f / Engine.PhysicsTicksPerSecond; 
    float snapshotTimer = 0;
    float Latency => (Current.SnaphotTime - Previous.SnaphotTime) / 1000.0f;

    public TransformSnapshot Previous, Current;

    public override void _Ready()
    {
        if (NetworkedNode.Node is not Node3D)
        {
            GD.PushError("(Network Transform) Networked Node's Parent is NOT a Node3D!");
        }
        else
        {
            TransformNode ??= NetworkedNode.Node as Node3D; // Set Defaults            
        }

    }
    public override void _NetworkReady()
    {
        if (NetworkedNode.Node is RigidBody3D body && !NetworkedNode.AmIOwner) body.Freeze = true;
        
        Current = new() { Pos = TransformNode.GlobalPosition, Rot = TransformNode.Quaternion, Scale = TransformNode.Scale, SnaphotTime = Time.GetTicksMsec() };
        Previous = Current;
    }
    public override void _PhysicsProcess(double delta)
    {

        bool clientAuth = AuthorityMode == AuthorityMode.Client && NetworkManager.AmIClient && NetworkedNode.AmIOwner;
        bool serverAuth = AuthorityMode == AuthorityMode.Server && NetworkManager.AmIServer;
        bool authorized = clientAuth || serverAuth;

        // Update Position
        if (authorized)
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
                if (Current.Rot.X != TransformNode.GlobalRotation.X) { changes |= Changed.RotX; valuesChanged.Add(TransformNode.GlobalRotation.X); }
                if (Current.Rot.Y != TransformNode.GlobalRotation.Y) { changes |= Changed.RotY; valuesChanged.Add(TransformNode.GlobalRotation.Y); }
                if (Current.Rot.Z != TransformNode.GlobalRotation.Z) { changes |= Changed.RotZ; valuesChanged.Add(TransformNode.GlobalRotation.Z); }
            }

            // Scale
            if (SyncScale)
            {
                if (Current.Scale != TransformNode.Scale) { changes |= Changed.Scale; valuesChanged.Add(TransformNode.Scale.X); valuesChanged.Add(TransformNode.Scale.Y); valuesChanged.Add(TransformNode.Scale.Z); }
            }


            // Send RPC if changes occured
            if (changes != Changed.None)
            {
                if (serverAuth)
                    RelayChanged(changes, [.. valuesChanged]);
                else if (clientAuth)
                    SendChanged(changes, [.. valuesChanged]);

                // Set our current to be this so we can backtest it again above
                Current.Pos = TransformNode.GlobalPosition;
                Current.Rot = TransformNode.Quaternion;
                Current.Scale = TransformNode.Scale;
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

        if (LinearInterpolation)
        {
            TransformNode.GlobalPosition = Interp.Pos;
            TransformNode.Quaternion = Interp.Rot;
            TransformNode.Scale = Interp.Scale;
        }
        else
        {
             TransformNode.GlobalPosition = Current.Pos;
            TransformNode.Quaternion = Current.Rot;
            TransformNode.Scale = Current.Scale;
        }
    }

    [Command(Channels.Unreliable)]
    public void SendChanged(Changed changed, float[] valuesChanged)
    {
        // Tell the clients their new info
        RelayChanged(changed, valuesChanged);

    }

    [Relay(Channels.Unreliable)]
    public void RelayChanged(Changed changed, float[] valuesChanged)
    {
        if (NetworkedNode.AmIOwner) return;
        
        Previous = Current; // Set previous to current

        // Read new current
        ReadSnapshot(changed, valuesChanged);
        Current.SnaphotTime = Time.GetTicksMsec();
        snapshotTimer = 0;
    }

    /// <summary>
    /// Read a snapshot from values changed
    /// </summary>
    void ReadSnapshot(Changed changed, float[] valuesChanged)
    {
        int readIndex = 0;

        Current.Pos = new()
        {
            X = (changed & Changed.PosX) > 0 ? valuesChanged[readIndex++] : Previous.Pos.X,
            Y = (changed & Changed.PosY) > 0 ? valuesChanged[readIndex++] : Previous.Pos.Y,
            Z = (changed & Changed.PosZ) > 0 ? valuesChanged[readIndex++] : Previous.Pos.Z,
        };

        Current.Rot = Quaternion.FromEuler(new()
        {
            X = (changed & Changed.RotX) > 0 ? valuesChanged[readIndex++] : Previous.Rot.X,
            Y = (changed & Changed.RotY) > 0 ? valuesChanged[readIndex++] : Previous.Rot.Y,
            Z = (changed & Changed.RotZ) > 0 ? valuesChanged[readIndex++] : Previous.Rot.Z,
        });

        bool updateScale = (changed & Changed.Scale) > 0;

        Current.Scale = new()
        {
            X = updateScale ? valuesChanged[readIndex++] : Previous.Scale.X,
            Y = updateScale ? valuesChanged[readIndex++] : Previous.Scale.Y,
            Z = updateScale ? valuesChanged[readIndex++] : Previous.Scale.Z,
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
        float w = wSquared > 0f ? (float)Mathf.Sqrt(wSquared) : 0f;

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
                Rot = Rot.Slerpni(other.Rot, amount),
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

