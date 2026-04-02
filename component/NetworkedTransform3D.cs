using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ArcaneNetworking;

[GlobalClass]
public partial class NetworkedTransform3D : NetworkedTransform
{
    [Export] bool ApplyVelocity = false;

    [ExportCategory("Interpolation And Corrections")]
    bool interp = true;
    [Export] bool LinearInterpolation
    {
        get => interp;
        set
        {
            if (TransformNode != null) Reset();
            interp = value;
        }
    }

    private Vector3 _prevInterpPos;
    private Vector3 _velocity;

    // Public accessors
    public Vector3 Velocity => _velocity;

    protected override void HandleSnapshots(TransformSnapshot last, TransformSnapshot curr)
    {   
        // Headless server doesn't interpolate
        if (NetworkManager.AmIHeadless)
            return;

        if (interp)
        {
            // Interpolation factor based on RenderTime
            float interpT = NetworkTime.InverseLerp(last.SnaphotTime, curr.SnaphotTime, RenderTime);

            // Interpolate transform
            Local = last.InterpWith(curr, interpT);
        }
        else
        {
            // No interpolation — just snap to current snapshot
            Local = curr;
        }

        // Compute velocity still using snapshot delta
        float snapshotDelta = (curr.SnaphotTime - last.SnaphotTime) / 1000f; // seconds
        
        if (snapshotDelta > 0f)
            _velocity = (curr.Pos - last.Pos) / snapshotDelta;

        // Apply transforms to node
        ApplyLocal();
    }

    protected override void ApplyLocal()
    {
        base.ApplyLocal();

        if (ApplyVelocity && TransformNode is RigidBody3D rb)
            rb.LinearVelocity = _velocity;
    }
}