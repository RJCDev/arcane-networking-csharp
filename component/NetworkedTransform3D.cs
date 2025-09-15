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

    [Export] SendTime SendTiming = SendTime.Process;

    [ExportCategory("What To Sync")]
    [Export] public bool SyncPosition;
    [Export] public bool SyncRotation;
    [Export] public bool SyncScale;

    [ExportCategory("Interpolation And Corrections")]
    InterpolationMode interpMode = InterpolationMode.Process;
    [Export] InterpolationMode LinearInterpolation
    {
        get
        {
            return interpMode;
        }
        set
        {
            if (TransformNode != null) Reset();
            interpMode = value;
        }
    }

    [Export] int maxSnapshots = 3;

    public float snapShotInterval = 1f / NetworkManager.manager.NetworkRate;

    float interpT = 1;

    TransformSnapshot Previous, Local, Current;
    SortedSet<TransformSnapshot> Snapshots = new();
    
    
    public override void _EnterTree()
    {
        if (TransformNode == null && NetworkedNode.Node is not Node3D)
        {
            GD.PushError("(Network Transform) Networked Node's Parent is NOT a Node3D!");
        }
        else
        {
            TransformNode ??= NetworkedNode.Node as Node3D; // Set Defaults
            Reset();
        }

    }

    public override void _NetworkReady()
    {
        if (NetworkedNode.Node is RigidBody3D body && !NetworkedNode.AmIOwner) body.Freeze = true;
       
    }
    public override void _PhysicsProcess(double delta)
    {
        if (SendTiming == SendTime.Physics)
            HandleWrite();

        if (LinearInterpolation == InterpolationMode.Physics)
            HandleRead(delta);
        
           
    }
    public override void _Process(double delta)
    {
        if (SendTiming == SendTime.Process)
            HandleWrite();

        if (LinearInterpolation == InterpolationMode.Process)
            HandleRead(delta);
    }

    void Reset()
    {
        Local = new() { Pos = TransformNode.GlobalPosition, Rot = TransformNode.Quaternion, Scale = TransformNode.Scale, SnaphotTime = Client.TickMS };
        Previous = Local;
        Current = Local;

        Snapshots.Clear();
    }

    void HandleWrite()
    {
        // Update Position
        if (NetworkedNode.AmIOwner)
        {
            Changed changes = Changed.None;
            List<float> valuesChanged = [];

            // Pos
            if (SyncPosition)
            {
                if (Local.Pos.X != TransformNode.GlobalPosition.X) { changes |= Changed.PosX; valuesChanged.Add(TransformNode.GlobalPosition.X); }
                if (Local.Pos.Y != TransformNode.GlobalPosition.Y) { changes |= Changed.PosY; valuesChanged.Add(TransformNode.GlobalPosition.Y); }
                if (Local.Pos.Z != TransformNode.GlobalPosition.Z) { changes |= Changed.PosZ; valuesChanged.Add(TransformNode.GlobalPosition.Z); }
            }

            // Rot
            if (SyncRotation)
            {
                if (Local.Rot.X != TransformNode.GlobalRotation.X) { changes |= Changed.RotX; valuesChanged.Add(TransformNode.GlobalRotation.X); }
                if (Local.Rot.Y != TransformNode.GlobalRotation.Y) { changes |= Changed.RotY; valuesChanged.Add(TransformNode.GlobalRotation.Y); }
                if (Local.Rot.Z != TransformNode.GlobalRotation.Z) { changes |= Changed.RotZ; valuesChanged.Add(TransformNode.GlobalRotation.Z); }
                
            }

            // Scale
            if (SyncScale)
            {
                if (Local.Scale != TransformNode.Scale)
                {
                    changes |= Changed.Scale;
                    valuesChanged.Add(TransformNode.Scale.X);
                    valuesChanged.Add(TransformNode.Scale.Y);
                    valuesChanged.Add(TransformNode.Scale.Z);
                }
            }


            // Send RPC if changes occured
            if (changes != Changed.None)
            {
                
                if (NetworkManager.AmIServer)
                    RelayChanged(changes, [.. valuesChanged], Server.TickMS);
                else
                    SendChanged(changes, [.. valuesChanged], Client.TickMS);

                // Set our local to be this so we can backtest it again above
                Local.Pos = TransformNode.GlobalPosition;
                Local.Rot = TransformNode.Quaternion;
                Local.Scale = TransformNode.Scale;
            }

        }
    }

    void Pop()
    {
        Previous = Snapshots.Min;
        Snapshots.Remove(Snapshots.Min);

        Current = Snapshots.Min;
        Snapshots.Remove(Snapshots.Min);
    }

    void HandleRead(double delta)
    {
        if (NetworkedNode.AmIOwner || NetworkManager.AmIServer) return;

        // Pop the 2 newest from the snapshots list
        if (Snapshots.Count >= maxSnapshots + 2 && interpT >= 1.0) // Check if we are finished with most recent interp
        {
            Pop(); // Pop 2 oldest that are now valid
        }

        long snapshotOffsetMS = (long)(snapShotInterval * 1000f) * maxSnapshots + 2;
        long bufferMS = Client.TickMS - snapshotOffsetMS;// Get data from the networkrate times max snapshots ago to get the buffer ms
        
        // Get the time between the normalized and the end
        interpT = Mathf.InverseLerp(
        Previous.SnaphotTime,
        Current.SnaphotTime, 
        bufferMS);

        //GD.Print(Snapshots.Count);
        GD.Print(Previous.SnaphotTime + " " + Current.SnaphotTime + " " + bufferMS + " " + interpT);
        
        // Lerp
        Local = Previous.InterpWith(Current, interpT);

        TransformNode.GlobalPosition = Local.Pos;
        TransformNode.Quaternion = Local.Rot;
        TransformNode.Scale = Local.Scale;
        

    }

    [Command(Channels.Unreliable)]
    public void SendChanged(Changed changed, float[] valuesChanged, long tickMS)
    {
        // Only set on server if we as the server don't own this
        if (!NetworkedNode.AmIOwner)
            SetServer(changed, valuesChanged, tickMS);

        // Tell the clients their new info
        RelayChanged(changed, valuesChanged, tickMS);
    }

    [Relay(Channels.Unreliable)]
    public void RelayChanged(Changed changed, float[] valuesChanged, long tickMS) => SetClient(changed, valuesChanged, tickMS);

    void SetServer(Changed changed, float[] valuesChanged, long tickMS)
    {
        Local = ReadSnapshot(changed, valuesChanged, tickMS);
        TransformNode.GlobalPosition = Local.Pos;
        TransformNode.Quaternion = Local.Rot;
        TransformNode.Scale = Local.Scale;
    }

    void SetClient(Changed changed, float[] valuesChanged, long tickMS)
    {
        if (NetworkedNode.AmIOwner) return;

        if (LinearInterpolation != InterpolationMode.None)
        {
            var newSnap = ReadSnapshot(changed, valuesChanged, tickMS);
            Snapshots.Add(newSnap);
        }
        else
        {
            Local = ReadSnapshot(changed, valuesChanged, tickMS);
            TransformNode.GlobalPosition = Local.Pos;
            TransformNode.Quaternion = Local.Rot;
            TransformNode.Scale = Local.Scale;
        }
    }

    /// <summary>
    /// Read a snapshot from values changed
    /// </summary>
    TransformSnapshot ReadSnapshot(Changed changed, float[] valuesChanged, long tickMS)
    {        
        TransformSnapshot snap = new();

        int readIndex = 0;

        snap.Pos = new()
        {
            X = (changed & Changed.PosX) > 0 ? valuesChanged[readIndex++] : Local.Pos.X,
            Y = (changed & Changed.PosY) > 0 ? valuesChanged[readIndex++] : Local.Pos.Y,
            Z = (changed & Changed.PosZ) > 0 ? valuesChanged[readIndex++] : Local.Pos.Z,
        };

        snap.Rot = Quaternion.FromEuler(new()
        {
            X = (changed & Changed.RotX) > 0 ? valuesChanged[readIndex++] : Local.Rot.X,
            Y = (changed & Changed.RotY) > 0 ? valuesChanged[readIndex++] : Local.Rot.Y,
            Z = (changed & Changed.RotZ) > 0 ? valuesChanged[readIndex++] : Local.Rot.Z,
        }).Normalized();

        bool updateScale = (changed & Changed.Scale) > 0;

        snap.Scale = new()
        {
            X = updateScale ? valuesChanged[readIndex++] : Local.Scale.X,
            Y = updateScale ? valuesChanged[readIndex++] : Local.Scale.Y,
            Z = updateScale ? valuesChanged[readIndex++] : Local.Scale.Z,
        };
    
        snap.SnaphotTime = tickMS;
        return snap;
    }

    public enum SendTime
    {
        Process,
        Physics
    }
    public enum InterpolationMode
    {
        None,
        Process,
        Physics,

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
    public struct TransformSnapshot: IComparable<TransformSnapshot>
    {
        public Vector3 Pos;
        public Quaternion Rot;
        public Vector3 Scale;

        public long SnaphotTime;
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

        public int CompareTo(TransformSnapshot other)
        {
            return SnaphotTime == other.SnaphotTime ? 0 : (SnaphotTime < other.SnaphotTime ? -1 : 1);
        }
    }

}

