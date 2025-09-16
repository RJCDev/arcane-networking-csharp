using System;
using System.Diagnostics;
using Godot;

namespace ArcaneNetworking;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public class ServerTime
{
    private const int MaxSamples = 32;
    private readonly List<Sample> samples = new List<Sample>(MaxSamples);
    private double chosenOffsetMs = 0.0; // double for fractional ms during calc
    private bool hasOffset = false;
    private readonly double smoothingAlpha; // 0..1, small = slow smoothing

    public ServerTime(double smoothingAlpha = 0.05)
    {
        this.smoothingAlpha = smoothingAlpha;
    }

    public static long LocalTimeMs() =>
        (Stopwatch.GetTimestamp() * 1000L) / Stopwatch.Frequency;

    private class Sample
    {
        public long T0 { get; set; } // client send local ms
        public long T1 { get; set; } // server receive ms (server)
        public long T2 { get; set; } // server transmit ms (server)
        public long T3 { get; set; } // client recv local ms
        public double Offset { get; set; }
        public double Delay { get; set; }
    }

    /// <summary>
    /// Add a new sync sample (t0 client send, t1 server receive, t2 server send, t3 client receive)
    /// All t* are in milliseconds. t0, t3 must be LocalTimeMs(); t1,t2 are server Unix ms.
    /// </summary>
    public void AddSample(long t0, long t1, long t2, long t3)
    {
        // Do math in doubles to avoid integer truncation
        double offset = ((t1 - (double)t0) + (t2 - (double)t3)) / 2.0;
        double delay = (t3 - (double)t0) - (t2 - (double)t1);

        var s = new Sample { T0 = t0, T1 = t1, T2 = t2, T3 = t3, Offset = offset, Delay = delay };

        samples.Add(s);
        if (samples.Count > MaxSamples) samples.RemoveAt(0);

        // Choose best sample: the one with minimum delay
        var best = samples.OrderBy(x => x.Delay).First();

        if (!hasOffset)
        {
            chosenOffsetMs = best.Offset;
            hasOffset = true;
        }
        else
        {
            // Smooth toward the best-offset to avoid jumping on noisy packets
            chosenOffsetMs = chosenOffsetMs * (1.0 - smoothingAlpha) + best.Offset * smoothingAlpha;
        }
    }

    /// <summary>
    /// Returns current estimate of server Unix ms
    /// </summary>
    public long NowMs
    {
        get
        {
            if (!hasOffset)
                throw new InvalidOperationException("ServerClock has no samples yet.");

            double local = LocalTimeMs();
            return (long)Math.Round(local + chosenOffsetMs);
        }
    }

    /// <summary>
    /// Expose last delay for debugging
    /// </summary>
    public double LastDelayMs => samples.Count == 0 ? double.PositiveInfinity : samples.Last().Delay;
}
