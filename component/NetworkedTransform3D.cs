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
    [Export] public bool SyncPosition;
    [Export] public bool SyncRotation;

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
    public override void _PhysicsProcess(double delta)
    {
        if (SendTiming == SendTime.Physics)
            HandleWrite();

        if (interpMode == InterpolationMode.Physics)
        {
            HandleLerp((float)delta);
        }
    }
    public override void _Process(double delta)
    {
        if (SendTiming == SendTime.Process)
            HandleWrite();

        if (interpMode == InterpolationMode.Process)
        {
            HandleLerp((float)delta);
        }

        // Debug
        if (ServerDebugMesh != null)
        {
            ServerDebugMesh.TopLevel = true;
            ServerDebugMesh.Visible = DebugEnabled;

            if (Snapshots.Count > 0)
            {
                ServerDebugMesh.GlobalPosition = Snapshots.Max.Pos;
                ServerDebugMesh.Quaternion = Snapshots.Max.Rot;
            }
        }
        
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
            if (Local.Pos.X != TransformNode.GlobalPosition.X) { changes |= Changed.PosX; valuesChanged.Add(TransformNode.GlobalPosition.X); }
            if (Local.Pos.Y != TransformNode.GlobalPosition.Y) { changes |= Changed.PosY; valuesChanged.Add(TransformNode.GlobalPosition.Y); }
            if (Local.Pos.Z != TransformNode.GlobalPosition.Z) { changes |= Changed.PosZ; valuesChanged.Add(TransformNode.GlobalPosition.Z); }
        }

        // Rot
        if (SyncRotation)
        {
            if (Local.Rot.X != TransformNode.Quaternion.X) { changes |= Changed.RotX; valuesChanged.Add(TransformNode.Quaternion.X); }
            if (Local.Rot.Y != TransformNode.Quaternion.Y) { changes |= Changed.RotY; valuesChanged.Add(TransformNode.Quaternion.Y); }
            if (Local.Rot.Z != TransformNode.Quaternion.Z) { changes |= Changed.RotZ; valuesChanged.Add(TransformNode.Quaternion.Z); }
            if (Local.Rot.W != TransformNode.Quaternion.W) { changes |= Changed.RotW; valuesChanged.Add(TransformNode.Quaternion.W); }

        }

        // Set our local to be this so we can backtest it again above
        Local.Pos = TransformNode.GlobalPosition;
        Local.Rot = TransformNode.Quaternion;
        

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
                    SendChanged(changed, changedValues, NetworkTime.TickMS);

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

    void HandleLerp(float delta)
    {
        if (NetworkedNode.AmIOwner || NetworkManager.AmIServer)
            return;

        if (Snapshots.Count < 2)
            return; // Need at least two for interpolation

        // Slide Time back based on the max buffer size and our latency

        long snapshotIntervalMs = (long)(1000.0f / NetworkManager.manager.NetworkRate);
        long targetBuffer = minBufferMs + snapshotIntervalMs + (long)NetworkTime.GetSmoothedRTT();
        long renderTime = NetworkTime.TickMS - targetBuffer;

        var (prev, curr) = GetSurroundingSnaps(renderTime); // Grab 2 snaps surrounding this buffer time

        if (!prev.HasValue || !curr.HasValue)
            return;
        
        Current = curr;
        Previous = prev;

        //GD.Print((NetworkTime.TickMS - Current.Value.SnaphotTime) + " " + (NetworkTime.TickMS - bufferSliderMs) + " " +  (NetworkTime.TickMS - Previous.Value.SnaphotTime));
        // Step 1: Interpolate at buffer time
        // Make sure we normalize using the current ms, this is required to make sure the numbers don't get too large when we do float math
        
        float t = (float)(renderTime - Previous.Value.SnaphotTime) /
          (float)(Current.Value.SnaphotTime - Previous.Value.SnaphotTime);

        interpT = Mathf.SmoothStep(0f, 1f, t);

        // We need to lerp
        TransformSnapshot interpolated = Previous.Value.InterpWith(Current.Value, interpT);

        // Extrapolate forward from buffer time to "now"
        // Get Time difference between previous and current
        long dt = Current.Value.SnaphotTime - Previous.Value.SnaphotTime;
        if (dt <= 0) dt = 1; // prevent divide by zero

        // Fraction from current snapshot
        float extrapT = (float)(NetworkTime.TickMS - Current.Value.SnaphotTime) / dt;

        extrapT = Mathf.Clamp(extrapT, 0f, 0.2f); // e.g. 0.1..0.2

        Local = interpolated.InterpWith(Current.Value, extrapT);

        // Apply transform
        TransformNode.GlobalPosition = Local.Pos;
        TransformNode.Quaternion = Local.Rot;

        // Clean Buffer
        while (Snapshots.Count > 0 && Snapshots.Min.SnaphotTime < Previous.Value.SnaphotTime)
        {
            Snapshots.Remove(Snapshots.Min);
        }

    }

    [Command(Channels.Reliable)]
    public void SendChanged(Changed changed, float[] valuesChanged, long tickMS)
    {
        // Only set on server if we as the server don't own this
        if (!NetworkedNode.AmIOwner && NetworkManager.AmIHeadless)
        {
            SetNoLerp(changed, valuesChanged, tickMS);
        }

        // Tell the clients their new info
        RelayChanged(changed, valuesChanged, tickMS);

    }

    [Relay(Channels.Reliable)]
    public void RelayChanged(Changed changed, float[] valuesChanged, long tickMS)
    {
        if (NetworkedNode.AmIOwner) return;

        if (LinearInterpolation != InterpolationMode.None)
        {
            ReadSnapshot(changed, valuesChanged, tickMS);
        }
        else
        {
            SetNoLerp(changed, valuesChanged, tickMS);
        }

    }

    void SetNoLerp(Changed changed, float[] valuesChanged, long tickMS)
    {
        Local = ReadSnapshot(changed, valuesChanged, tickMS);

        // Apply transform
        TransformNode.GlobalPosition = Local.Pos;
        TransformNode.Quaternion = Local.Rot;
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

