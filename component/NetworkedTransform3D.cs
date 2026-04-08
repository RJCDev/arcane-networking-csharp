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

    [Export] public float TeleportThreshold = 1f;

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

                // 1. Interpolate between last and current snapshot
                float interpE = NetworkTime.InverseLerp(last.SnaphotTime, curr.SnaphotTime, RenderTime);
                Local = last.InterpWith(curr, interpE);

                // 2. Use Local snapshot time as the base
                
                double dt = (NetworkTime.TickMS - RenderTime) / 1000.0d;
                if (dt <= 0.0)
                    return;

                // 3. Snapshot interval (for velocity calc)
                double snapshotIntervalS = (curr.SnaphotTime - last.SnaphotTime) / 1000.0d;
                if (snapshotIntervalS <= 0.0)
                    return;

                // 4. Compute velocities from snapshots
                Vector3 linearDelta = curr.Pos - last.Pos;
                Vector3 linearVelocity = linearDelta / (float)snapshotIntervalS;

                Vector3 angularDelta = GetAngularDelta(last.Rot, curr.Rot);
                Vector3 omega = angularDelta / (float)snapshotIntervalS;
                    
                Quaternion omegaQuat = new Quaternion(omega.X, omega.Y, omega.Z, 0);
                Quaternion derivative = omegaQuat * curr.Rot * 0.5f;

                // 6. Build extrapolated snapshot
                TransformSnapshot extrap = new()
                {
                    SnaphotTime = NetworkTime.TickMS,
                    Pos = curr.Pos + linearVelocity * (float)dt,
                    Rot = curr.Rot + derivative * (float)snapshotIntervalS,
                };

                Local = extrap;

                // 7. Correction
                Vector3 positionError = extrap.Pos - TransformNode.GlobalPosition;
                float linearError = positionError.Length();

                float linearCorrectionTime = Mathf.Clamp(linearError / 10f, 0.05f, 0.3f);
                Vector3 correctionVelocity = positionError / linearCorrectionTime;

                float linearCorrectionWeight = Mathf.Clamp(linearError, 0.0f, 1.0f);

                if (_physicsBody is RigidBody3D rb)
                {
                    // Linear correction
                    rb.LinearVelocity = rb.LinearVelocity.Lerp(correctionVelocity, linearCorrectionWeight);

                    // Angular correction
                    Quaternion currentRot = rb.GlobalTransform.Basis.GetRotationQuaternion();
                    float angularError = currentRot.AngleTo(extrap.Rot);

                    float angularCorrectionWeight = Mathf.Clamp(angularError, 0.0f, 1.0f);

                    rb.AngularVelocity = rb.AngularVelocity.Lerp(omega, angularCorrectionWeight);
                }
                else if (_physicsBody is CharacterBody3D cb)
                {
                    cb.Velocity = cb.Velocity.Lerp(correctionVelocity, linearCorrectionWeight);
                    TransformNode.GlobalBasis = new Basis(extrap.Rot);
                }
                else
                {
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
        if (!oldQuat.IsFinite() || !newQuat.IsFinite())
            return Vector3.Zero;

        Quaternion delta = newQuat * oldQuat.Inverse();

        // Ensure shortest path (VERY important)
        if (delta.W < 0.0f)
            delta = -delta;

        delta = delta.Normalized();

        float angle = 2.0f * Mathf.Acos(Mathf.Clamp(delta.W, -1.0f, 1.0f));

        // Clamp to avoid huge rotations (optional but recommended)
        angle = Mathf.Wrap(angle, -Mathf.Pi, Mathf.Pi);

        float sinHalfAngle = Mathf.Sqrt(1.0f - delta.W * delta.W);

        Vector3 axis;
        if (sinHalfAngle < 0.001f)
            axis = new Vector3(delta.X, delta.Y, delta.Z);
        else
            axis = new Vector3(delta.X, delta.Y, delta.Z) / sinHalfAngle;

        return axis * angle; // radians
    }

}