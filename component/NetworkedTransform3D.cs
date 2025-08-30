using Godot;
using ArcaneNetworking;
using System.Collections.Generic;

[GlobalClass]
public partial class NetworkedTransform3D : NetworkedComponent
{
    Node3D TransformNode;
    [ExportGroup("Send Rate")]
    [Export] public SendTime SendTime = SendTime.Physics;

    [ExportGroup("Send Channel")]
    [Export] public Channels SendChannel = Channels.Reliable;

    [ExportCategory("Selections")]
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

    public Vector3 PositionError = Vector3.Zero;
    public Vector3 RotationError = Vector3.Zero;

    // Queue of Snapshots (Used for Cubic Interpolation)
    public TransformSnapshot[] CurrentState = new TransformSnapshot[2];

    public override void _Ready()
    {
        if (NetworkedNode.Node is not Node3D)
        {
            GD.PushError("(Network Transform) Networked Node's Parent is NOT a Node3D!");
        }
        else TransformNode = NetworkedNode.Node as Node3D;
    }
    public override void _PhysicsProcess(double delta)
    {
        // Process the interpolation
        TransformSnapshot current = CurrentState[0].TransformWith(CurrentState[1], InterpSpeed);

        TransformNode.GlobalPosition = current.Pos;
        TransformNode.GlobalPosition = current.Pos;
        TransformNode.GlobalPosition = current.Pos;
    }

    // Actually apply the changed part of the transform (each value changed is the delta of change, valuesChanged is paddded)
    public void Set(Changed changed, float[] valuesChanged)
    {
        // Record our local position so we can interpolate between this and the new one (cubic)
        CurrentState[0] = new()
        {
            Pos = TransformNode.GlobalPosition,
            Rot = TransformNode.GlobalBasis.GetRotationQuaternion(),
            Scale = TransformNode.Scale
        };

        // Read the "Current" snapshot we just got
        CurrentState[1] = ReadSnapshot(changed, valuesChanged);
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

        Quaternion prevRot = new()
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

        Pos = PosX | PosY | PosZ,
        Rot = RotX | RotY | RotZ
    }


    // A transform snapshot
    public struct TransformSnapshot
    {
        public Vector3 Pos;
        public Quaternion Rot;
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
    }


}

