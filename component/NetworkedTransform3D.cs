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

    [ExportCategory("Networking Limits (For Server Authoritive)")]
    [Export] public float maxYVel = 20f;
    [Export] public float maxZXVel = 20f;

    [ExportCategory("Interpolation And Corrections")]
    [Export] public bool LinearInterpolation = true;
    [Export] public float InterpSpeed = 0.5f;
    [Export] public float hardPosError = 5f;
    [Export] public float HardRotError = 2f;


    // Queue of Snapshots (Used for Cubic Interpolation)
    public TransformSnapshot CurrentState;

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
            CurrentState = new TransformSnapshot() { Pos = TransformNode.GlobalPosition, Rot = TransformNode.Scale, Scale = TransformNode.GlobalRotation };
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
                if (CurrentState.Pos.X != TransformNode.GlobalPosition.X) { changes |= Changed.PosX; valuesChanged.Add(TransformNode.GlobalPosition.X); }
                if (CurrentState.Pos.Y != TransformNode.GlobalPosition.Y) { changes |= Changed.PosY; valuesChanged.Add(TransformNode.GlobalPosition.Y); }
                if (CurrentState.Pos.Z != TransformNode.GlobalPosition.Z) { changes |= Changed.PosZ; valuesChanged.Add(TransformNode.GlobalPosition.Z); }
            }

            // Rot
            if (SyncRotation)
            {
                 if (CurrentState.Rot.X != TransformNode.GlobalRotation.X) { changes |= Changed.RotX; valuesChanged.Add(TransformNode.GlobalRotation.X); }
                if (CurrentState.Rot.Y != TransformNode.GlobalRotation.Y) { changes |= Changed.RotY; valuesChanged.Add(TransformNode.GlobalRotation.Y); }
                if (CurrentState.Rot.Z != TransformNode.GlobalRotation.Z) { changes |= Changed.RotZ; valuesChanged.Add(TransformNode.GlobalRotation.Z); }
            }

            // Scale
            if (SyncScale)
            {
                if (CurrentState.Scale != TransformNode.Scale) { changes |= Changed.Scale; valuesChanged.Add(TransformNode.Scale.X); valuesChanged.Add(TransformNode.Scale.Y); valuesChanged.Add(TransformNode.Scale.Z); }
            }
            

            // Send RPC if changes occured
            if (changes != Changed.None)
            {
                // Send to other
                Set(AuthorityMode == AuthorityMode.Client ? [Client.serverConnection.GetID()] : [.. Server.Connections.Keys], changes, [.. valuesChanged]);
            }

        }
        else
        {
            // Process the interpolation if we aren't owner
            TransformSnapshot LocalState = new()
            {
                Pos = TransformNode.GlobalPosition,
                Rot = TransformNode.GlobalRotation,
                Scale = TransformNode.Scale
            };
            TransformSnapshot current = CurrentState.TransformWith(LocalState, InterpSpeed);

            TransformNode.GlobalPosition = current.Pos;
            TransformNode.GlobalRotation = current.Rot;
            TransformNode.Scale = current.Scale;

        }
       
    }

    // Actually apply the changed part of the transform (each value changed is the delta of change, valuesChanged is paddded)
    [MethodRPC(Channels.Unreliable)]
    public void Set(uint[] connsToSendTo, Changed changed, float[] valuesChanged)
    {
        // Record out local state if we are owner
        if (NetworkedNode.AmIOwner && AuthorityMode == AuthorityMode.Client)
        {
            CurrentState = new()
            {
                Pos = TransformNode.GlobalPosition,
                Rot = TransformNode.GlobalRotation,
                Scale = TransformNode.Scale
            };
        }
        else
        {
            // Read the "Current" snapshot we just got
            CurrentState = ReadSnapshot(changed, valuesChanged);
        }
    }


    /// <summary>
    /// Read a snapshot from values changed
    /// </summary>
    TransformSnapshot ReadSnapshot(Changed changed, float[] valuesChanged)
    {
        int readIndex = 0;

        Vector3 prevPos = new()
        {
            X = (changed & Changed.PosX) > 0 ? valuesChanged[readIndex++] : 0,
            Y = (changed & Changed.PosY) > 0 ? valuesChanged[readIndex++] : 0,
            Z = (changed & Changed.PosZ) > 0 ? valuesChanged[readIndex++] : 0,
        };

        Vector3 prevRot = new()
        {
            X = (changed & Changed.RotX) > 0 ? valuesChanged[readIndex++] : 0,
            Y = (changed & Changed.RotY) > 0 ? valuesChanged[readIndex++] : 0,
            Z = (changed & Changed.RotZ) > 0 ? valuesChanged[readIndex++] : 0,
        };

        bool updateScale = (changed & Changed.Scale) > 0;

        Vector3 prevScale = new()
        {
            X = updateScale ? valuesChanged[readIndex++] : 0,
            Y = updateScale ? valuesChanged[readIndex++] : 0,
            Z = updateScale ? valuesChanged[readIndex++] : 0,
        };

        return new()
        {
            Pos = prevPos,
            Rot = prevRot,
            Scale = prevScale
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

