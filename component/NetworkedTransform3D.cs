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
    CorrectionMode _correctionMode = CorrectionMode.EXTRAPOLATION;

    [Export] public float ExtrapThreshold;
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
    
    public Vector3 LinearVelocity = Vector3.Zero;
    public Vector3 AngularVelocity = Vector3.Zero;

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
                long extrapDeltaMS = NetworkTime.TickMS - Local.SnaphotTime; // Time between interpolated and now
                float snapshotDeltaS = extrapDeltaMS / 1000.0f;

                if (snapshotDeltaS <= 0) 
                {
                    GD.Print(snapshotDeltaS + " " + extrapDeltaMS);
                    break; // Sanity check
                }

                TransformSnapshot extrap = new()
                {
                    SnaphotTime = Local.SnaphotTime + extrapDeltaMS,
                    Pos = Local.Pos + LinearVelocity,
                    Rot = Local.Rot * Quaternion.FromEuler(AngularVelocity),
                };

                LinearVelocity = curr.Pos - Local.Pos;
                AngularVelocity = GetAngularDelta(Local.Rot, curr.Rot);

                // // Dont over extrapolate if large movement (possibly teleport)
                // if (LinearVelocity.LengthSquared() > ExtrapThreshold)
                // {
                //     ApplyLocal();
                //     return;
                // }


                 GD.Print("Snapshot Curr Last Pos: " + curr.Pos + " " +  last.Pos);
                    GD.Print("Local Pos: " + Local.Pos);
                    GD.Print("Extrap Vel: " + LinearVelocity);
                    GD.Print("Local SNapshot Time: " + Local.SnaphotTime);
                    GD.Print("Extrap Delta: " + extrapDeltaMS);
                    GD.Print("----");

                // 3. Set velocity to keep simulation happy 
                // TODO Send velocity instead of infer it from snapshots so we get proper acceleration prediction
                // Something is wrong here


                if (_physicsBody is RigidBody3D rb)
                {
                    rb.LinearVelocity = LinearVelocity / snapshotDeltaS;
                    rb.AngularVelocity = AngularVelocity / snapshotDeltaS;
                }
                else if (_physicsBody is CharacterBody3D cb)
                {
                    cb.Velocity = AngularVelocity / snapshotDeltaS;
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

        if (!oldQuat.IsFinite() || !newQuat.IsFinite())
            return Vector3.One;

        Quaternion qDiff = newQuat * oldQuat.Normalized().Inverse();
        qDiff = qDiff.Normalized();

        if (!qDiff.IsFinite())
            return Vector3.One;

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