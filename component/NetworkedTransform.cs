using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ArcaneNetworking;

[GlobalClass]
public abstract partial class NetworkedTransform : NetworkedComponent
{
    [Export] protected Node3D TransformNode = null;

	[ExportCategory("Send Rate")]
    
    long _sendRate = 60;
    [Export] protected long SendRate
    {
        get => _sendRate;
        set
        {
            _sendRate = value;
            SendRateMs = _sendRate > 0 ? 1000 / _sendRate : 0;
        }
    }

    protected long SendRateMs { get; private set; } = 1000 / 60;

    [Export(PropertyHint.Range, "5, 500, 1")] long BufferDelay = 50;

    // Snapshot data
    protected readonly SortedSet<TransformSnapshot> Snapshots = [];
    protected TransformSnapshot Last = default, Current = default;
    protected TransformSnapshot Local;

    MovingAverage LatencyAvg = new();

	[ExportCategory("What To Sync")]
    [Export] protected bool SyncPosition = true;
    [Export] protected bool SyncRotation = true;

    [ExportCategory("Debug")]
    [Export] bool DebugEnabled;
    [Export] Node3D ServerDebugMesh;

    long lastWriteTime = 0;
    long renderTime;

    public long RenderTime => renderTime;

    public override void _Ready()
    {
        if (TransformNode == null)
        {
            GD.PushError("(Network Transform) Networked Node is NULL!");
        }
        else
        {
            TransformNode ??= NetworkedNode.Node as Node3D; // Set Defaults
                
            Reset();
            
        }

    }
    public override void _AuthoritySet()
    {

    }
    
    public override void _NetworkReady()
    {
        _AuthoritySet();
    }

    public void SetSyncing(bool syncing)
    {
        SyncPosition = syncing;
        SyncRotation = syncing;
    }

	protected void Reset()
    {
        Local = new() { Origin = TransformNode.GlobalPosition, Rotation = TransformNode.Quaternion, SnaphotTime = NetworkTime.TickMS };

        Snapshots.Clear();
        LatencyAvg = new();
    }
    
	public override void _Process(double delta)
    {
            
		// Update render timeMs + BufferDela
        long latency = SendRateMs + LatencyAvg.Value;
		renderTime = NetworkTime.TickMS - latency; // The timestamp at which we are currently rendering (account for latency)

      
                    
        // Should we send at all?
        if (!SyncPosition && !SyncRotation)
            return;

        if (NetworkTime.TickMS - lastWriteTime >= SendRateMs)
        {
            lastWriteTime = NetworkTime.TickMS;
            HandleWrite();
        }

    
        // Debug
        if (ServerDebugMesh != null)
        {
            ServerDebugMesh.Visible = DebugEnabled;

            if (Snapshots.Count > 0)
            {
                if (SyncPosition)
                {
                    ServerDebugMesh.GlobalPosition = Snapshots.Max.Origin;

                }
                if (SyncRotation)
                {
                    ServerDebugMesh.GlobalBasis = new Basis(Snapshots.Max.Rotation);

                }
            }
        }

		// Get snapshots at this render time
        var (last, curr) = GetSnapshotPair(renderTime);
        
        if (!last.HasValue || !curr.HasValue)
        {
            if (Last == default || Current == default) return;
            
            // The time since the last snapshot
            long snapDelta = NetworkTime.TickMS - Current.SnaphotTime;

            if (snapDelta >= SendRateMs)
            {
                // Slide time forward but collapse positions so extrapolation produces zero movement
                Last = new TransformSnapshot { SnaphotTime = Last.SnaphotTime + SendRateMs, Origin = Current.Origin, Rotation = Current.Rotation };
                Current = new TransformSnapshot { SnaphotTime = Current.SnaphotTime + SendRateMs, Origin = Current.Origin, Rotation = Current.Rotation };

                Snapshots.Add(Last);
                Snapshots.Add(Current);
            }
        }
        else
        {
            Last = last.Value;
            Current = curr.Value;
        }

        // ONLY process snapshots if not owner
        if (!NetworkedNode.AmIOwner)
        {   
            HandleSnapshots(Last, Current);
        }
        else // Read your own authorative snapshot into the buffer so we have a past if we switch owners
        {
            var snapshot = new TransformSnapshot(){ Origin = TransformNode.GlobalPosition, Rotation = TransformNode.Quaternion, SnaphotTime = NetworkTime.TickMS };
            Snapshots.Add(snapshot);
        }

        // Always debuffer to cleanup snapshots
        if (last.HasValue)
        {
            while (Snapshots.Count > 0 && Snapshots.Min.SnaphotTime < last.Value.SnaphotTime)
            {
                Snapshots.Remove(Snapshots.Min);
            }
        }
       
    }
      
    (TransformSnapshot? last, TransformSnapshot? curr) GetSnapshotPair(long renderTime)
    {
        TransformSnapshot? Last = null, Curr = null;

        foreach (TransformSnapshot snap in Snapshots)
        {
            if (snap.SnaphotTime < renderTime)
            {
                Last = snap;
            }
            if (snap.SnaphotTime >= renderTime)
            {
                Curr = snap;
                break;
            }
        }
        
        return (Last, Curr);
    }

    (Changed changed, float[] changedValues) GetChanged()
    {
        Changed changes = Changed.None;
        List<float> valuesChanged = [];

        // Pos
        if (SyncPosition)
        {
            if (Local.Origin.X != TransformNode.GlobalPosition.X) { changes |= Changed.PosX; valuesChanged.Add(TransformNode.GlobalPosition.X); }
			if (Local.Origin.Y != TransformNode.GlobalPosition.Y) { changes |= Changed.PosY; valuesChanged.Add(TransformNode.GlobalPosition.Y); }
			if (Local.Origin.Z != TransformNode.GlobalPosition.Z) { changes |= Changed.PosZ; valuesChanged.Add(TransformNode.GlobalPosition.Z); }

			Local.Origin = TransformNode.GlobalPosition;

            // Velocity
            if (TransformNode is RigidBody3D rb)
            {
                if (Local.LinearVelocity.X != rb.LinearVelocity.X) { changes |= Changed.LVelX; valuesChanged.Add(rb.LinearVelocity.X); }
                if (Local.LinearVelocity.Y != rb.LinearVelocity.Y) { changes |= Changed.LVelY; valuesChanged.Add(rb.LinearVelocity.Y); }
                if (Local.LinearVelocity.Z != rb.LinearVelocity.Z) { changes |= Changed.LVelZ; valuesChanged.Add(rb.LinearVelocity.Z); }

                Local.LinearVelocity = rb.LinearVelocity;
            }
            else if (TransformNode is CharacterBody3D cb)
            {
                if (Local.LinearVelocity.X != cb.Velocity.X) { changes |= Changed.LVelX; valuesChanged.Add(cb.Velocity.X); }
                if (Local.LinearVelocity.Y != cb.Velocity.Y) { changes |= Changed.LVelY; valuesChanged.Add(cb.Velocity.Y); }
                if (Local.LinearVelocity.Z != cb.Velocity.Z) { changes |= Changed.LVelZ; valuesChanged.Add(cb.Velocity.Z); }

                Local.LinearVelocity = cb.Velocity;
            }
        }   

        // Rot
        if (SyncRotation)
        {
            Quaternion GlobalRot = TransformNode.GlobalBasis.GetRotationQuaternion();
            
            if (Local.Rotation.X != GlobalRot.X) { changes |= Changed.RotX; valuesChanged.Add(GlobalRot.X); }
            if (Local.Rotation.Y != GlobalRot.Y) { changes |= Changed.RotY; valuesChanged.Add(GlobalRot.Y); }
            if (Local.Rotation.Z != GlobalRot.Z) { changes |= Changed.RotZ; valuesChanged.Add(GlobalRot.Z); }
            if (Local.Rotation.W != GlobalRot.W) { changes |= Changed.RotW; valuesChanged.Add(GlobalRot.W); }

            Local.Rotation = GlobalRot;
            
            // Velocity
            if (TransformNode is RigidBody3D rb)
            {
                if (Local.AngularVelocity.X != rb.AngularVelocity.X) { changes |= Changed.AVelX; valuesChanged.Add(rb.AngularVelocity.X); }
                if (Local.AngularVelocity.Y != rb.AngularVelocity.Y) { changes |= Changed.AVelY; valuesChanged.Add(rb.AngularVelocity.Y); }
                if (Local.AngularVelocity.Z != rb.AngularVelocity.Z) { changes |= Changed.AVelZ; valuesChanged.Add(rb.AngularVelocity.Z); }

                Local.AngularVelocity = rb.AngularVelocity;
            }
        }

        return (changes, [.. valuesChanged]);
    }

    void HandleWrite()
    {
        // Update 
       
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

    [Command(Channels.Unreliable, true)]
    public void SendChanged(Changed changed, float[] valuesChanged, long tickSent)
    {
        if (!NetworkedNode.AmIOwner && NetworkManager.AmIHeadless)
        {
            var snapshot = ReadSnapshot(changed, valuesChanged, tickSent);
		    Snapshots.Add(snapshot);
        }

        // Tell the clients their new info
        RelayChanged(changed, valuesChanged, tickSent);
        
    }

    [Relay(Channels.Unreliable, true, true)]
    public void RelayChanged(Changed changed, float[] valuesChanged, long tickSent)
    {
		var snapshot = ReadSnapshot(changed, valuesChanged, tickSent);
		Snapshots.Add(snapshot);

        LatencyAvg.AddSample(NetworkTime.TickMS - tickSent);        

    }

	/// <summary>
    /// Applys the data to the position by default
    /// </summary>
	protected virtual void ApplyLocal()
	{
		if (SyncPosition)
        {
            TransformNode.GlobalPosition = Local.Origin;
        }
        if (SyncRotation)
        {
            TransformNode.GlobalBasis = new Basis(Local.Rotation);
        }
	}

	/// <summary>
	///  Provides the 2 most recent snapshots in between the render time
	/// </summary>
	/// <param name="last"></param>
	/// <param name="curr"></param>
	protected virtual void HandleSnapshots(TransformSnapshot last, TransformSnapshot curr) {}

    /// <summary>
    /// Read a snapshot from values changed
    /// </summary>
    TransformSnapshot ReadSnapshot(Changed changed, float[] valuesChanged, long tickMS)
    {
        TransformSnapshot snap = Snapshots.Count > 0
        ? Snapshots.Max
        : new() { Origin = TransformNode.GlobalPosition, Rotation = new Quaternion(TransformNode.GlobalBasis) }; // Init with transform node info

        int readIndex = 0;

        snap.Origin = new()
        {
            X = (changed & Changed.PosX) > 0 ? valuesChanged[readIndex++] : snap.Origin.X,
            Y = (changed & Changed.PosY) > 0 ? valuesChanged[readIndex++] : snap.Origin.Y,
            Z = (changed & Changed.PosZ) > 0 ? valuesChanged[readIndex++] : snap.Origin.Z,
        };

        snap.LinearVelocity = new()
        {
            X = (changed & Changed.LVelX) > 0 ? valuesChanged[readIndex++] : snap.LinearVelocity.X,
            Y = (changed & Changed.LVelY) > 0 ? valuesChanged[readIndex++] : snap.LinearVelocity.Y,
            Z = (changed & Changed.LVelZ) > 0 ? valuesChanged[readIndex++] : snap.LinearVelocity.Z,
        };

        snap.Rotation = new()
        {
            X = (changed & Changed.RotX) > 0 ? valuesChanged[readIndex++] : snap.Rotation.X,
            Y = (changed & Changed.RotY) > 0 ? valuesChanged[readIndex++] : snap.Rotation.Y,
            Z = (changed & Changed.RotZ) > 0 ? valuesChanged[readIndex++] : snap.Rotation.Z,
            W = (changed & Changed.RotW) > 0 ? valuesChanged[readIndex++] : snap.Rotation.W,
        };

        snap.AngularVelocity = new()
        {
            X = (changed & Changed.AVelX) > 0 ? valuesChanged[readIndex++] : snap.AngularVelocity.X,
            Y = (changed & Changed.AVelY) > 0 ? valuesChanged[readIndex++] : snap.AngularVelocity.Y,
            Z = (changed & Changed.AVelZ) > 0 ? valuesChanged[readIndex++] : snap.AngularVelocity.Z,
        };

        snap.SnaphotTime = tickMS;
        return snap;
    }
    
    // A ushort describing what part of the transform was changed
    public enum Changed : ushort
    {
        // Position / Rotation
        None = 0,

        PosX = 1 << 0,
        PosY = 1 << 1,
        PosZ = 1 << 2,

        RotX = 1 << 4,
        RotY = 1 << 5,
        RotZ = 1 << 6,
        RotW = 1 << 7,

        // Velocities (if needed)
        LVelX = 1 << 8,
        LVelY = 1 << 9,
        LVelZ = 1 << 10,

        AVelX = 1 << 11,
        AVelY = 1 << 12,
        AVelZ = 1 << 13,

    }
}

// A transform snapshot
public struct TransformSnapshot : IComparable<TransformSnapshot>
{
    public Vector3 Origin;
    public Quaternion Rotation;

    public Vector3 LinearVelocity;
    public Vector3 AngularVelocity;

    public long SnaphotTime;
    public TransformSnapshot()
    {
        Origin = Vector3.Zero;
        Rotation = Quaternion.Identity;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
    }
    /// <summary>
    /// TO Extrapolate, use a value larger than 1 for amount
    /// </summary>
    /// <returns>A TransformSnapshot that has been Transformed from this TransformSnapshot To "After"</returns>
    public TransformSnapshot InterpWith(TransformSnapshot other, float amount)
    {
        Quaternion a = Rotation.Normalized();
        Quaternion b = other.Rotation.Normalized();

        if (!a.IsFinite())
            a = Quaternion.Identity;

        if (!b.IsFinite())
            b = Quaternion.Identity;

        return new TransformSnapshot
        {
            SnaphotTime = SnaphotTime,

            Origin = Origin.Lerp(other.Origin, amount),

            Rotation = a.Slerp(b, amount).Normalized(),

            LinearVelocity = LinearVelocity.Lerp(other.LinearVelocity, amount),

            AngularVelocity = AngularVelocity.Lerp(other.AngularVelocity, amount),
        };
    }

    public TransformSnapshot Extrapolate(float dtSeconds)
    {
        Vector3 predictedPos = Origin + LinearVelocity * dtSeconds;

        Quaternion predictedRotation = Rotation;

        float angularSpeed = AngularVelocity.Length();
        if (angularSpeed > 0.0001f)
        {
            Vector3 axis = AngularVelocity / angularSpeed;
            float angle = angularSpeed * dtSeconds;

            Quaternion deltaRot = new Quaternion(axis, angle);
            predictedRotation = (deltaRot * Rotation).Normalized();
        }

        long dtMs = (long)(dtSeconds * 1000.0f);

        return new TransformSnapshot()
        {
            SnaphotTime = SnaphotTime + dtMs,

            Origin = predictedPos,
            Rotation = predictedRotation,

            LinearVelocity = LinearVelocity,
            AngularVelocity = AngularVelocity,
        };
    }

    public int CompareTo(TransformSnapshot other)
    {
        return SnaphotTime == other.SnaphotTime ? 0 : (SnaphotTime < other.SnaphotTime ? -1 : 1);
    }
    public override bool Equals([NotNullWhen(true)] object obj)
    {
        if (obj is TransformSnapshot s)
        {
            return Origin == s.Origin && Rotation == s.Rotation ;
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
        return HashCode.Combine(Origin.GetHashCode(), Rotation.GetHashCode());
    }
}