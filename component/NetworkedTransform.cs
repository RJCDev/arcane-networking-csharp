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
    [Export] protected long SendRate = 60;
    double SendRateMs => 1000.0d / SendRate;
    float SendsPerSec => 1.0f / SendRate;

    [Export(PropertyHint.Range, "5, 500, 1")] long BufferDelay = 50;
    protected readonly SortedSet<TransformSnapshot> Snapshots = [];
    protected TransformSnapshot Local;
    MovingAverage DelayAverage;

	[ExportCategory("What To Sync")]
    [Export] public bool SyncPosition = true;
    [Export] public bool SyncRotation = true;

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

        DelayAverage = new(BufferDelay, 0.1f);

    }
    public override void _AuthoritySet()
    {
        if (TransformNode is RigidBody3D rb)
        {
            if (NetworkManager.AmIHeadless)
            {
                if (AuthorityMode == AuthorityMode.Server)
                    rb.Freeze = false;
                else
                    rb.Freeze = true;
            }
            else if (NetworkManager.AmIClient)
            {
                if (AuthorityMode == AuthorityMode.Client && NetworkManager.AmIServer)
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

	protected void Reset()
    {
        Local = new() { Pos = TransformNode.GlobalPosition, Rot = TransformNode.Quaternion, SnaphotTime = NetworkTime.TickMS };

        Snapshots.Clear();
    }
    
	public override void _Process(double delta)
    {
		// Update render time
		renderTime = NetworkTime.TickMS - (DelayAverage.Value + (long)SendRateMs + (1000 / NetworkManager.manager.NetworkRate) + BufferDelay); // The timestamp at which we are currently rendering

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
                    ServerDebugMesh.Position = Vector3.Zero;
                    ServerDebugMesh.GlobalPosition = Snapshots.Max.Pos;

                }
                if (SyncRotation)
                {
                    ServerDebugMesh.Basis = Basis.Identity;
                    ServerDebugMesh.GlobalBasis = new Basis(Snapshots.Max.Rot);

                }
            }
        }
		
		// Handle snapshots
		var (last, curr) = GetSnapshotPair(renderTime);

        if (!last.HasValue || !curr.HasValue)
                return;

		HandleSnapshots(last.Value, curr.Value);

		while (Snapshots.Min.SnaphotTime < last.Value.SnaphotTime) // De-Buffer up to last
        {
            Snapshots.Remove(Snapshots.Min);
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
            if (Local.Pos.X != TransformNode.GlobalPosition.X) { changes |= Changed.PosX; valuesChanged.Add(TransformNode.GlobalPosition.X); }
			if (Local.Pos.Y != TransformNode.GlobalPosition.Y) { changes |= Changed.PosY; valuesChanged.Add(TransformNode.GlobalPosition.Y); }
			if (Local.Pos.Z != TransformNode.GlobalPosition.Z) { changes |= Changed.PosZ; valuesChanged.Add(TransformNode.GlobalPosition.Z); }

			Local.Pos = TransformNode.GlobalPosition;

        }

        // Rot
        if (SyncRotation)
        {
            Quaternion GlobalRot = TransformNode.GlobalBasis.GetRotationQuaternion();
			if (Local.Rot.X != GlobalRot.X) { changes |= Changed.RotX; valuesChanged.Add(GlobalRot.X); }
			if (Local.Rot.Y != GlobalRot.Y) { changes |= Changed.RotY; valuesChanged.Add(GlobalRot.Y); }
			if (Local.Rot.Z != GlobalRot.Z) { changes |= Changed.RotZ; valuesChanged.Add(GlobalRot.Z); }
			if (Local.Rot.W != GlobalRot.W) { changes |= Changed.RotW; valuesChanged.Add(GlobalRot.W); }

			Local.Rot = GlobalRot;
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
                    SendChanged(changed, changedValues, NetworkTime.TickMS);

            }
        }
    }

    [Command(Channels.Unreliable)]
    public void SendChanged(Changed changed, float[] valuesChanged, long tickSent)
    {
        // Only set on server if we as the server don't own this
        if (!NetworkedNode.AmIOwner && NetworkManager.AmIHeadless)
        { 
            Local = ReadSnapshot(changed, valuesChanged, tickSent);
            ApplyLocal();
        }

        // Tell the clients their new info
        RelayChanged(changed, valuesChanged, tickSent);
        
    }

    [Relay(Channels.Unreliable, true)]
    public void RelayChanged(Changed changed, float[] valuesChanged, long tickSent)
    {
        if (NetworkedNode.AmIOwner) return;
        
		var snapshot = ReadSnapshot(changed, valuesChanged, tickSent);
		Snapshots.Add(snapshot);

        DelayAverage.AddSample(NetworkTime.TickMS - Snapshots.Max.SnaphotTime); // Add a sample for the most recent delay

    }

	/// <summary>
    /// Applys the data to the position by default
    /// </summary>
	protected virtual void ApplyLocal()
	{
		if (SyncPosition)
        {
            TransformNode.Position = Vector3.Zero;
            TransformNode.GlobalPosition = Local.Pos;
        }
        if (SyncRotation)
        {
            TransformNode.Basis = Basis.Identity;
            TransformNode.GlobalBasis = new Basis(Local.Rot);   
        }
	}

	/// <summary>
	/// Handle Snapshots between render time
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
        ? Snapshots.Max // most recent one
        : new();

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
        return snap;
    }

    public enum SendTime
    {
        Update,
        Manual,
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

    public int CompareTo(TransformSnapshot other)
    {
        return SnaphotTime == other.SnaphotTime ? 0 : (SnaphotTime < other.SnaphotTime ? -1 : 1);
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
}