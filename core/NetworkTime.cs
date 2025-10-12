using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ArcaneNetworking;

public class NetworkTime
{
    struct Sample
    {
        public long T0, T1, T2, T3;
        public long Offset;
        public long Delay;
    }

    // Client Connection to server
    private const int MaxSamples = 32; // Max RTT Samples
    private static readonly List<Sample> samples = new(MaxSamples);
    private static double bestOffsetMs = 0.0; // double for fractional ms during calc
    static double bestOffsetAcc = 0;
    private static bool hasOffset = false;
    private static readonly double smoothingAlpha = 0.1f; // 0..1, small = slow smoothing
    private static double smoothedRTT = 0;

    const long MaxJumpMs = 50;

    public static void Reset() { smoothedRTT = 0; hasOffset = false; samples.Clear(); }


    public static void AddRTTSample(ulong sample)
    {
        if (smoothedRTT == 0)
            smoothedRTT = sample; // first sample
        else
            smoothedRTT = smoothedRTT * (1 - smoothingAlpha) + sample * smoothingAlpha;
    }

    public static ulong SmoothedRTT => (ulong)Math.Round(smoothedRTT);


    public static long LocalTimeMs() => // Monotonic Clock
        (Stopwatch.GetTimestamp() * 1000L) / Stopwatch.Frequency;

    /// <summary>
    /// Add a new sync sample (t0 client send, t1 server receive, t2 server send, t3 client receive)
    /// All t* are in milliseconds. t0, t3 must be LocalTimeMs(); t1,t2 are server Unix ms.
    /// </summary>
   public static void AddTimeSample(long t0, long t1, long t2, long t3)
    {
        // Compute offset and delay
        long offset = ((t1 - t0) + (t2 - t3)) / 2;
        long delay  = (t3 - t0) - (t2 - t1);

        // Add sample to rolling buffer
        samples.Add(new Sample { Offset = offset, Delay = delay });
        if (samples.Count > MaxSamples)
            samples.RemoveAt(0);

        // Sort by delay and pick the lowest 20%
        var lowDelay = samples.OrderBy(s => s.Delay)
                            .Take(Math.Max(1, samples.Count / 5))
                            .ToList();

        // Median offset of those low-delay samples
        long median = lowDelay.OrderBy(s => s.Offset)
                            .ElementAt(lowDelay.Count / 2)
                            .Offset;

        // Initialize on first sync
        if (!hasOffset)
        {
            bestOffsetAcc = median;
            bestOffsetMs  = median;
            hasOffset     = true;
            return;
        }

        // Ignore tiny fluctuations (less than ~1 ms)
        if (Math.Abs(median - bestOffsetMs) < 1.0)
            return;

        // Smooth toward the median offset
        bestOffsetAcc = bestOffsetAcc * (1.0 - smoothingAlpha) + median * smoothingAlpha;
        bestOffsetMs  = bestOffsetAcc;

        // Optional: clamp huge jumps (e.g. if a bad sample sneaks in)
        double diff = bestOffsetMs - bestOffsetAcc;
        if (Math.Abs(diff) > MaxJumpMs)
            bestOffsetMs = bestOffsetAcc + Math.Sign(diff) * MaxJumpMs;

    }




    public static float InverseLerp(long from, long to, long value)
    {
        if (from == to)
            return 0f; // Avoid division by zero

        return Math.Clamp((float)(value - from) / (to - from), 0f, 1f);
    }

    /// <summary>
    /// Returns current estimate of server Unix ms
    /// </summary>
    public static long TickMS
    {
        get
        {
            if (!hasOffset || NetworkManager.AmIServer)
                return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            double local = LocalTimeMs();
            return (long)Math.Round(local + bestOffsetMs);
        }
    }

    /// <summary>
    /// Expose last delay for debugging
    /// </summary>
    public static double LastDelayMs => samples.Count == 0 ? double.PositiveInfinity : samples.Last().Delay;
}
