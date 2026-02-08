using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ArcaneNetworking;

[GlobalClass]
public partial class NetworkedTransform3D : NetworkedTransform
{

    [ExportCategory("Interpolation And Corrections")]
    bool linearInterpolation = true;
    [Export]
    bool LinearInterpolation
    {
        get
        {
            return linearInterpolation;
        }
        set
        {
            if (TransformNode != null) Reset();
            linearInterpolation = value;
        }
    }
  
    protected override void HandleSnapshots(TransformSnapshot last, TransformSnapshot curr)
    {
        if (linearInterpolation)
        {
            // Make sure we have at least 2 snapshots
            if (NetworkManager.AmIHeadless)
                return;

            float interpT = NetworkTime.InverseLerp(last.SnaphotTime, curr.SnaphotTime, RenderTime);
        
            Local = last.InterpWith(curr, interpT);
        }

        // Apply transforms
        ApplyLocal();
    }

}