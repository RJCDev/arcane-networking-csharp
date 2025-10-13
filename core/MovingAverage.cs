using System;
using Godot;

namespace ArcaneNetworking;

public class MovingAverage
{
    public double Smoothing; // 0..1, small = slow smoothing
    private double movingAverage;
    public MovingAverage(double startValue = 0, double smooth = 0.1f)
    {
        movingAverage = startValue;
        Smoothing = smooth;
    }
    public long Value => (long)Math.Round(movingAverage);
    public void AddSample(long sample)
    {
        if (movingAverage == 0)
            movingAverage = sample; // first sample
        else
            movingAverage = movingAverage * (1 - Smoothing) + sample * Smoothing;            
    }

}
