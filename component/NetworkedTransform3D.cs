using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ArcaneNetworking;

public enum CorrectionMode
{
    INTERPOLATION,
    EXTRAPOLATION,
    NONE
}

[GlobalClass]
public partial class NetworkedTransform3D : NetworkedTransform
{


    [ExportCategory("Corrections")]
    CorrectionMode _correctionMode = CorrectionMode.INTERPOLATION;
    [Export] CorrectionMode CorrectionMode
    {
        get => _correctionMode;
        set
        {
            if (TransformNode != null) Reset();
            _correctionMode = value;
        }
    }

    PhysicsBody3D _physicsBody = null;
    public override void _Ready()
    {
        base._Ready();

        if (TransformNode is PhysicsBody3D pb)
            _physicsBody = pb;
    }

    protected override void HandleSnapshots(TransformSnapshot last, TransformSnapshot curr)
    {   
        switch (_correctionMode)
        {
            case CorrectionMode.INTERPOLATION:

                // Interpolation factor based on RenderTime
                float interpT = NetworkTime.InverseLerp(last.SnaphotTime, curr.SnaphotTime, RenderTime);

                // Interpolate transform
                Local = last.InterpWith(curr, interpT);

                break;

            case CorrectionMode.EXTRAPOLATION:

                // 1. Interpolate snapshots last and current
                float interpE = NetworkTime.InverseLerp(last.SnaphotTime, curr.SnaphotTime, RenderTime);
                Local = last.InterpWith(curr, interpE);

                // 2. Extrapolate based on velocity
                long snapshotDeltaMS = NetworkTime.TickMS - curr.SnaphotTime - (curr.SnaphotTime - last.SnaphotTime);
                float snapshotDeltaS = snapshotDeltaMS / 1000.0f;

                Vector3 linearVelocity = curr.Pos - last.Pos;
                Vector3 angularVelocity = GetAngularDelta(last.Rot, curr.Rot);
                
                TransformSnapshot extrap = new()
                {
                    SnaphotTime = Local.SnaphotTime + snapshotDeltaMS,
                    Pos = Local.Pos + linearVelocity,
                    Rot = Local.Rot * Quaternion.FromEuler(angularVelocity),
                };

                if (_physicsBody is RigidBody3D rb)
                {
                    rb.LinearVelocity = linearVelocity / snapshotDeltaS;
                    rb.AngularVelocity = angularVelocity / snapshotDeltaS;
                }
                else if (_physicsBody is CharacterBody3D cb)
                {
                    cb.Velocity = linearVelocity / snapshotDeltaMS;
                }
                
                // GD.Print("Local Time: " + Local.SnaphotTime + " | Render Time: " + (RenderTime + snapshotDeltaMS) + " | Extrap Snapshot Time: " + extrap.SnaphotTime);
                // GD.Print("Current Time: " + curr.SnaphotTime + " | Last Time: " + last.SnaphotTime + " | Network Time: " + NetworkTime.TickMS);
                // GD.Print("------");
                // Interpolate transform
                Local = extrap;

                break;

            case CorrectionMode.NONE: // No interpolation — just snap to current snapshot

                Local = curr;

                break;
            
        }

        ApplyLocal();
    
    }

    // Get delta
    public Vector3 GetAngularDelta(Quaternion oldQuat, Quaternion newQuat)
    {
        // 1. Calculate the rotation difference (relative rotation)
        // q_diff * old = new  =>  q_diff = new * old.Inverse()
        Quaternion qDiff = newQuat * oldQuat.Normalized().Inverse();
        qDiff = qDiff.Normalized();

        if (!qDiff.IsFinite())
            return Vector3.Zero;

        // 2. Extract the rotation axis and angle (in radians)
        // Godot Quaternions have GetAngle() and GetAxis() methods
        float angle = qDiff.GetAngle();
        Vector3 axis = qDiff.GetAxis();

        // 3. Handle the shortest path (Quaternions double-cover rotations)
        // If the angle is greater than PI, we should rotate the other way
        if (angle > Mathf.Pi)
        {
            angle -= Mathf.Tau; // Subtract 2PI
        }

        // 4. Velocity = (Axis Angle) / Time
        return axis * angle;
    }


}