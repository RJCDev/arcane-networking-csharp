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

/// <summary>
/// Non-invasive NetworkTransform3D component for Node3D's and PhysicsBodies
/// <para> None: </para>
/// 
/// <para> 1. Applies the most recent snapshot to the transform specified in TransformNode </para>
/// 
/// <para> Interpolation: </para>
/// 
/// <para> 1. Takes the last 2 snapshots and smooths between them utilizing the RenderTime variable </para>
/// 
/// <para> Extrapolation: </para>
///
/// <para> 3. Apply to local simulation (Linear / Angular Velocity) </para>
/// <para> 1. Utilize interpolation above </para>
/// <para> 2. Extrapolates to get predicted position utilizing the delta of the positions in the last 2 snapshots </para>
/// <para> 3. Apply to local simulation (Linear / Angular Velocity) </para>
/// </summary>

[GlobalClass]
public partial class NetworkedTransform3D : NetworkedTransform
{
    [ExportCategory("Corrections")]
    CorrectionMode _correctionMode = CorrectionMode.EXTRAPOLATION;

    [Export] public float ExtrapFixThreshold = 0.5f;

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
    
    public Vector3 LinearDelta = Vector3.Zero;
    public Vector3 AngularDelta = Vector3.Zero;

    public override void _Ready()
    {
        base._Ready();

        if (TransformNode is PhysicsBody3D pb)
            _physicsBody = pb;
        
        NetworkedNode.OnOwnerChanged += _NewOwner;
           
    }
    
    void _NewOwner(int newOwner)
    {
        // Update weather we should use gravity or not
        if (TransformNode is RigidBody3D rb)
            rb.GravityScale = NetworkedNode.AmIOwner ? 1 : 0;
        
    }

    protected override void HandleSnapshots(TransformSnapshot last, TransformSnapshot curr)
    {   
        switch (_correctionMode)
        {
            case CorrectionMode.INTERPOLATION:

                // 1. Interpolation factor based on RenderTime
                float interpT = NetworkTime.InverseLerp(last.SnaphotTime, curr.SnaphotTime, RenderTime);

                // Interpolate transform
                Local = last.InterpWith(curr, interpT);

                // 2. Apply to Node3D
                ApplyLocal();

                break;

            case CorrectionMode.EXTRAPOLATION:
                
                
                // 1. Interpolate snapshots last and current
                float interpE = NetworkTime.InverseLerp(last.SnaphotTime, curr.SnaphotTime, RenderTime);
                // Interpolate transform
                Local = last.InterpWith(curr, interpE);

                // 2. Extrapolate based on delta between now and where we want to be
                long extrapDeltaMS = NetworkTime.TickMS - curr.SnaphotTime;
                double snapshotDeltaS = extrapDeltaMS / 1000.0d;
                if (snapshotDeltaS <= 0)
                    break; // Sanity check

                // 3. Get extrapolated transform (closest possible thing we have to "now" on a remote client)
                LinearDelta = curr.Pos - last.Pos;
                AngularDelta = GetAngularDelta(last.Rot, curr.Rot);

                TransformSnapshot extrap = new()
                {
                    SnaphotTime = Local.SnaphotTime + extrapDeltaMS,
                    Pos = Local.Pos + LinearDelta,
                    Rot = Local.Rot * Quaternion.FromEuler(AngularDelta),
                };
                // Update Local
                Local = extrap;

                // 4. Compute position error between where physics body is and where it should be
                Vector3 positionError = extrap.Pos - TransformNode.GlobalPosition;
                float linearError = positionError.Length();
                Vector3 angularError = GetAngularDelta(extrap.Rot, TransformNode.Quaternion);

                // 5. Steer toward extrapolated position via velocity — no direct position sets
                // correctionTime scales with error: small drift = gentle, large gap = faster but still smooth
                float correctionTime = Mathf.Clamp(linearError / 10.0f, 0.05f, 0.3f);
                Vector3 correctionVelocity = positionError / correctionTime;

               if (_physicsBody is RigidBody3D rb)
                {
                    float correctionWeight = Mathf.Clamp(linearError / ExtrapFixThreshold, 0.0f, 1.0f);
                    rb.LinearVelocity = rb.LinearVelocity.Lerp(correctionVelocity, correctionWeight);

                    // Time between the two snapshots, not between snapshot and now
                    double snapshotIntervalS = (curr.SnaphotTime - last.SnaphotTime) / 1000.0d;

                    Vector3 targetAngularVelocity = snapshotIntervalS > 0 
                        ? AngularDelta / (float)snapshotIntervalS 
                        : Vector3.Zero;

                    rb.AngularVelocity = rb.AngularVelocity.Lerp(targetAngularVelocity, correctionWeight);
                }
                else if (_physicsBody is CharacterBody3D cb)
                {
                    float correctionWeight = Mathf.Clamp(linearError / ExtrapFixThreshold, 0.0f, 1.0f);
                    cb.Velocity = cb.Velocity.Lerp(correctionVelocity, correctionWeight);

                    TransformNode.Quaternion = extrap.Rot;

                }
                else
                {
                    // No physics body — apply directly to transform
                    ApplyLocal();
                }


                break;

            case CorrectionMode.NONE: // No interpolation — just snap to current snapshot

                Local = curr;
                
                ApplyLocal();

                break;
            
        }

       
    
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