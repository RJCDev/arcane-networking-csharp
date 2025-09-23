using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace ArcaneNetworking;

[GlobalClass]
public partial class NetworkedTransform3D : NetworkedComponent
{
    [Export] protected Node3D TransformNode = null;
    [Export] protected SendTime SendTiming = SendTime.Process;

    [ExportCategory("What To Sync")]
    [Export] public bool SyncPosition = true;
    [Export] public bool SyncRotation = true;

    [Export]
    public bool UseLocal;

    [ExportCategory("Interpolation And Corrections")]
    InterpolationMode interpMode = InterpolationMode.Process;
    [Export]
    InterpolationMode LinearInterpolation
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
    [Export(PropertyHint.Range, "2, 50, 0.05")] int minBufferMs;

    [ExportCategory("Debug")]
    [Export] bool DebugEnabled;
    [Export] MeshInstance3D ServerDebugMesh;
    float interpT = 0;
    long currentBufferMs = 0;

    TransformSnapshot Local;

    TransformSnapshot? Previous, Current;

    SortedSet<TransformSnapshot> Snapshots = new();
    public override void _Ready()
    {
        if (NetworkedNode == null || NetworkedNode.Node is not Node3D)
        {
            GD.PushError("(Network Transform) Networked Node's Parent is NOT a Node3D!");
        }
        else
        {
            TransformNode ??= NetworkedNode.Node as Node3D; // Set Defaults
            Reset();
        }

    }
    public override void _AuthoritySet()
    {
        if (TransformNode is RigidBody3D rb)
        {
            if (NetworkManager.AmIServer)
            {
                if (AuthorityMode == AuthorityMode.Server)
                    rb.Freeze = false;
                else
                    rb.Freeze = true;
            }
            if (NetworkManager.AmIClient)
            {
                if (AuthorityMode == AuthorityMode.Client)
                    rb.Freeze = false;
                else
                    rb.Freeze = true;
            }

        }
       
    }
    public override void _NetworkReady()
    {
        _AuthoritySet();
    }
    public override void _NetworkUpdate()
    {
        if (SendTiming == SendTime.Update)
            HandleWrite();
        
    }
    public override void _Process(double delta)
    {
        if (SendTiming == SendTime.Process)
            HandleWrite();

        if (interpMode == InterpolationMode.Process)
            HandleLerp();
        
        // Debug
        if (ServerDebugMesh != null)
        {
            ServerDebugMesh.TopLevel = !UseLocal;
            ServerDebugMesh.Visible = DebugEnabled;

            if (Snapshots.Count > 0)
            {
                if (SyncPosition)
                {
                    if (UseLocal)
                        ServerDebugMesh.Position = Snapshots.Max.Pos;
                    else
                    {
                        ServerDebugMesh.Position = Vector3.Zero;
                        ServerDebugMesh.GlobalPosition = Snapshots.Max.Pos;
                    }
                        
                }
                if (SyncRotation)
                {
                    if (UseLocal)
                        ServerDebugMesh.Basis = new Basis(Snapshots.Max.Rot);
                    else
                    {
                        ServerDebugMesh.Basis = Basis.Identity;
                        ServerDebugMesh.GlobalBasis = new Basis(Snapshots.Max.Rot);
                    }
                        
                }
            }
        }
        
    }
    public override void _PhysicsProcess(double delta)
    {
        if (SendTiming == SendTime.Physics)
            HandleWrite();

        if (interpMode == InterpolationMode.Physics)
            HandleLerp();
    
    }
    void Reset()
    {
        Local = new() { Pos = TransformNode.GlobalPosition, Rot = TransformNode.Quaternion, SnaphotTime = NetworkTime.TickMS };

        Snapshots.Clear();
    }

    (Changed changed, float[] changedValues) GetChanged()
    {
        Changed changes = Changed.None;
        List<float> valuesChanged = [];

        // Pos
        if (SyncPosition)
        {
            if (UseLocal)
            {
                if (Local.Pos.X != TransformNode.Position.X) { changes |= Changed.PosX; valuesChanged.Add(TransformNode.Position.X); }
                if (Local.Pos.Y != TransformNode.Position.Y) { changes |= Changed.PosY; valuesChanged.Add(TransformNode.Position.Y); }
                if (Local.Pos.Z != TransformNode.Position.Z) { changes |= Changed.PosZ; valuesChanged.Add(TransformNode.Position.Z); }

                Local.Pos = TransformNode.Position;
            }
            else
            {
                if (Local.Pos.X != TransformNode.GlobalPosition.X) { changes |= Changed.PosX; valuesChanged.Add(TransformNode.GlobalPosition.X); }
                if (Local.Pos.Y != TransformNode.GlobalPosition.Y) { changes |= Changed.PosY; valuesChanged.Add(TransformNode.GlobalPosition.Y); }
                if (Local.Pos.Z != TransformNode.GlobalPosition.Z) { changes |= Changed.PosZ; valuesChanged.Add(TransformNode.GlobalPosition.Z); }

                Local.Pos = TransformNode.GlobalPosition;
            }

            
        }

        // Rot
        if (SyncRotation)
        {
            if (UseLocal)
            {
                Quaternion LocalRot = TransformNode.Basis.GetRotationQuaternion();
                if (Local.Rot.X != LocalRot.X) { changes |= Changed.RotX; valuesChanged.Add(LocalRot.X); }
                if (Local.Rot.Y != LocalRot.Y) { changes |= Changed.RotY; valuesChanged.Add(LocalRot.Y); }
                if (Local.Rot.Z != LocalRot.Z) { changes |= Changed.RotZ; valuesChanged.Add(LocalRot.Z); }
                if (Local.Rot.W != LocalRot.W) { changes |= Changed.RotW; valuesChanged.Add(LocalRot.W); }

                Local.Rot = LocalRot;
            }
            else
            {
                Quaternion GlobalRot = TransformNode.GlobalBasis.GetRotationQuaternion();
                if (Local.Rot.X != GlobalRot.X) { changes |= Changed.RotX; valuesChanged.Add(GlobalRot.X); }
                if (Local.Rot.Y != GlobalRot.Y) { changes |= Changed.RotY; valuesChanged.Add(GlobalRot.Y); }
                if (Local.Rot.Z != GlobalRot.Z) { changes |= Changed.RotZ; valuesChanged.Add(GlobalRot.Z); }
                if (Local.Rot.W != GlobalRot.W) { changes |= Changed.RotW; valuesChanged.Add(GlobalRot.W); }

                Local.Rot = GlobalRot;
            }

            

        }        

        return (changes, [.. valuesChanged]);
    }

    void HandleWrite()
    {
        // Update Position
        if (NetworkedNode.AmIOwner)
        {
            var (changed, changedValues) = GetChanged();

            // Send RPC if changes occured
            if (changed != Changed.None)
            {
                if (NetworkManager.AmIServer && AuthorityMode == AuthorityMode.Server)
                    RelayChanged(changed, changedValues, NetworkTime.TickMS);

                else if (NetworkManager.AmIClient && AuthorityMode == AuthorityMode.Client)
                    SendChanged(changed, changedValues);

            }
        }

        
    }

    (TransformSnapshot? prev, TransformSnapshot? curr) GetSurroundingSnaps(long bufferTime)
    {
        TransformSnapshot? prev = null;
        TransformSnapshot? curr = null;

        // Find bracketing snapshots
        foreach (var snap in Snapshots)
        {
            if (snap.SnaphotTime <= bufferTime)
            {
                prev = snap;
            }
            else
            {
                curr = snap;
                break;
            }
        }
        return (prev, curr);
    }

    void HandleLerp()
    {
        if (NetworkedNode.AmIOwner || NetworkManager.AmIServer)
            return;

        // Slide Time back based on the max buffer size and our latency
        long snapshotIntervalMs = (long)(1000.0f / NetworkManager.manager.NetworkRate);
        long targetBuffer = minBufferMs + snapshotIntervalMs + (long)NetworkTime.GetSmoothedRTT();
        long renderTime = NetworkTime.TickMS - targetBuffer;

        var (prev, curr) = GetSurroundingSnaps(renderTime); // Grab 2 snaps surrounding this buffer time

        if (!prev.HasValue || !curr.HasValue)
            return;
        
        Current = curr;
        Previous = prev;

        // GD.Print((NetworkTime.TickMS - Current.Value.SnaphotTime) + " " + (NetworkTime.TickMS - bufferSliderMs) + " " +  (NetworkTime.TickMS - Previous.Value.SnaphotTime));
        // Get the normalized value
        float t = (float)(renderTime - Previous.Value.SnaphotTime) /
            (Current.Value.SnaphotTime - Previous.Value.SnaphotTime);

        interpT = Mathf.SmoothStep(0f, 1f, t);

        // Extrapolate forward from buffer time to "now"
        // Get Time difference between previous and current
        long dt = Current.Value.SnaphotTime - Previous.Value.SnaphotTime;
        if (dt == 0) dt = 1; // prevent divide by zero

        // Fraction from current snapshot
        float extrapT = (float)(((float)NetworkTime.TickMS - Current.Value.SnaphotTime) / dt) / 1000.0f;

        // We need to lerp
        Local = Previous.Value.InterpWith(Current.Value, interpT + extrapT);

        // Apply transforms
        ApplyFromLocal();

        // Clean Buffer
        while (Snapshots.Count > 0 && Snapshots.Min.SnaphotTime < Previous.Value.SnaphotTime)
        {
            Snapshots.Remove(Snapshots.Min);
        }

    }

    [Command(Channels.Unreliable)]
    public void SendChanged(Changed changed, float[] valuesChanged)
    {
        // Only set on server if we as the server don't own this
        if (!NetworkedNode.AmIOwner && NetworkManager.AmIHeadless)
        {
            Local = ReadSnapshot(changed, valuesChanged, NetworkTime.TickMS);
            ApplyFromLocal();
        }

        // Tell the clients their new info
        RelayChanged(changed, valuesChanged, NetworkTime.TickMS);

    }

    [Relay(Channels.Unreliable)]
    public void RelayChanged(Changed changed, float[] valuesChanged, long tickMS)
    {
        if (NetworkedNode.AmIOwner) return;

        if (LinearInterpolation != InterpolationMode.None)
        {
            ReadSnapshot(changed, valuesChanged, tickMS);
        }
        else
        {
            Local = ReadSnapshot(changed, valuesChanged, tickMS);
            ApplyFromLocal();
        }

    }

    void ApplyFromLocal()
    {
        if (SyncPosition)
        {
            // Apply transform
            if (UseLocal)
                TransformNode.Position = Local.Pos;
            else
            {
                TransformNode.Position = Vector3.Zero;
                TransformNode.GlobalPosition = Local.Pos;
            }
                
        }
        if (SyncRotation)
        {
            if (UseLocal)
                TransformNode.Basis = new Basis(Local.Rot);
            else
            {
                TransformNode.Basis = Basis.Identity;
                TransformNode.GlobalBasis = new Basis(Local.Rot);
            }
                
        }
        
    }

    /// <summary>
    /// Read a snapshot from values changed
    /// </summary>
    TransformSnapshot ReadSnapshot(Changed changed, float[] valuesChanged, long tickMS)
    {
        TransformSnapshot? last = Snapshots.Count > 0
        ? Snapshots.Max // most recent one
        : null;

        TransformSnapshot snap = last ?? new(); // Latest snap

        int readIndex = 0;

        snap.Pos = new()
        {
            X = (changed & Changed.PosX) > 0 ? valuesChanged[readIndex++] : snap.Pos.X,
            Y = (changed & Changed.PosY) > 0 ? valuesChanged[readIndex++] : snap.Pos.Y,
            Z = (changed & Changed.PosZ) > 0 ? valuesChanged[readIndex++] : snap.Pos.Z,
        };

        snap.Rot = new()
        {
            X = (changed & Changed.RotX) > 0 ? valuesChanged[readIndex++] : snap.Rot.X,
            Y = (changed & Changed.RotY) > 0 ? valuesChanged[readIndex++] : snap.Rot.Y,
            Z = (changed & Changed.RotZ) > 0 ? valuesChanged[readIndex++] : snap.Rot.Z,
            W = (changed & Changed.RotW) > 0 ? valuesChanged[readIndex++] : snap.Rot.W,
        };

        snap.SnaphotTime = tickMS;
        Snapshots.Add(snap);
        return snap;
    }

    public enum SendTime
    {
        Process,
        Physics,
        Update
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
        RotW = 1 << 7,
    }

    // A transform snapshot
    public struct TransformSnapshot : IComparable<TransformSnapshot>
    {
        public Vector3 Pos;
        public Quaternion Rot;

        public long SnaphotTime;
        public TransformSnapshot()
        {
            Pos = Vector3.Zero;
            Rot = Quaternion.Identity;
        }
        /// <summary>
        /// TO Extrapolate, use a value larger than 1 for amount
        /// </summary>
        /// <returns>A TransformSnapshot that has been Transformed from this TransformSnapshot To "After"</returns>
        public TransformSnapshot InterpWith(TransformSnapshot other, float amount)
        {

            return new()
            {
                SnaphotTime = SnaphotTime,
                Pos = Pos.Lerp(other.Pos, amount),
                Rot = Rot.Normalized().Slerp(other.Rot.Normalized(), amount).Normalized(),
            };
        }

        public override bool Equals([NotNullWhen(true)] object obj)
        {
            if (obj is TransformSnapshot s)
            {
                return Pos == s.Pos && Rot == s.Rot ;
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
            return HashCode.Combine(Pos.GetHashCode(), Rot.GetHashCode());
        }

        public int CompareTo(TransformSnapshot other)
        {
            return SnaphotTime == other.SnaphotTime ? 0 : (SnaphotTime < other.SnaphotTime ? -1 : 1);
        }
    }
}

