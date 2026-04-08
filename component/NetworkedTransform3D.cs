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

    [Export] public float TeleportThreshold = 5f;

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
            {
                // 1. Interpolate to buffered render time
                float interpE = NetworkTime.InverseLerp(last.SnaphotTime, curr.SnaphotTime, RenderTime);
                Local = last.InterpWith(curr, interpE);

                // 2. Extrapolate forward from render time → now
                double dt = (NetworkTime.TickMS - RenderTime) / 1000.0d;
                if (dt <= 0.0)
                    return;

                TransformSnapshot extrap = Local.Extrapolate((float)dt);

                // Interpolate if we arent close enough (just apply with no velocity)
                if (TransformNode.GlobalPosition.DistanceSquaredTo(extrap.Origin) > TeleportThreshold * TeleportThreshold)
                {
                    ApplyLocal();
                    return;
                }

                // 3. Apply correction depending on body type
                if (_physicsBody is RigidBody3D rb)
                {
                    Vector3 posError = extrap.Origin - rb.GlobalPosition;

                    Quaternion currentRot = rb.GlobalBasis.GetRotationQuaternion().Normalized();
                    Quaternion targetRot  = extrap.Rotation; // already normalized from Extrapolate()
                    Quaternion rotError   = targetRot * currentRot.Inverse();

                    // Ensure shortest path before extracting angle
                    if (rotError.W < 0f)
                        rotError = -rotError;

                    Vector3 rotAxis = new Vector3(rotError.X, rotError.Y, rotError.Z);
                    float   sinHalf = rotAxis.Length();
                    rotAxis         = sinHalf > 0.0001f ? rotAxis / sinHalf : Vector3.Zero;
                    float rotAngle  = 2.0f * Mathf.Atan2(sinHalf, rotError.W);

                    // Local.LinearVelocity/AngularVelocity are already the extrapolated velocities
                    // so we just add the positional/rotational error correction on top
                    // Get the RID of your physics body
                    Rid bodyRid = rb.GetRid();

                    // Set the states via physics server
                    float posStiffness = 0.5f; // tweak between 0 and 1
                    Vector3 newVelocity = extrap.LinearVelocity + posError * posStiffness / (float)dt;

                    PhysicsServer3D.BodySetState(
                        bodyRid, 
                        PhysicsServer3D.BodyState.LinearVelocity,
                        newVelocity
                    );

                    float rotStiffness = 0.5f; // tweak between 0 and 1
                    Vector3 correctionAngular = sinHalf > 0.0001f ? rotAxis * (rotAngle * rotStiffness / (float)dt) : Vector3.Zero;
                    Vector3 angularVelocity = extrap.AngularVelocity + correctionAngular;

                    PhysicsServer3D.BodySetState(
                        bodyRid, 
                        PhysicsServer3D.BodyState.AngularVelocity, 
                        angularVelocity
                    );
                }
                else if (_physicsBody is CharacterBody3D cb)
                {
                    const float SnapThreshold    = 0.5f;
                    const float SmoothCorrection = 0.3f;

                    Vector3 posError = extrap.Origin - cb.GlobalPosition;

                    if (posError.Length() > SnapThreshold)
                    {
                        cb.GlobalPosition = extrap.Origin;
                    }
                    else
                    {
                        cb.GlobalPosition = cb.GlobalPosition.Lerp(extrap.Origin, SmoothCorrection);
                    }

                    cb.Quaternion = curr.Rotation;

                    // Extrapolate() preserves velocity as-is, so this is already the predicted velocity
                    cb.Velocity = extrap.LinearVelocity;
                    cb.MoveAndSlide();
                }
                else
                {
                    ApplyLocal();
                }
                    break;
}

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