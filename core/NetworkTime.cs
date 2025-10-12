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
    private static readonly double smoothingAlpha = 0.05; // 0..1, small = slow smoothing
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
    public static ulong GetSmoothedRTT() { return (ulong)smoothedRTT; }


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
        long delay = (t3 - t0) - (t2 - t1);

        // Add sample to rolling buffer
        samples.Add(new Sample { T0 = t0, T1 = t1, T2 = t2, T3 = t3, Offset = offset, Delay = delay });
        if (samples.Count > MaxSamples) samples.RemoveAt(0);

        // Find sample with minimum delay
        Sample best = samples[0];
        for (int i = 1; i < samples.Count; i++)
        {
            if (samples[i].Delay < best.Delay)
                best = samples[i];
        }

        // Initialize offset if first time
        if (!hasOffset)
        {
            bestOffsetAcc = best.Offset;
            bestOffsetMs = best.Offset;
            hasOffset = true;
            return;
        }

        // Clamp sudden jumps
        long diff = (long)(best.Offset - bestOffsetMs);
        if (Math.Abs(diff) > MaxJumpMs)
            best.Offset = (long)(bestOffsetMs + Math.Sign(diff) * MaxJumpMs);

        // Smooth toward the best-offset
        bestOffsetAcc = bestOffsetAcc * (1.0 - smoothingAlpha) + best.Offset * smoothingAlpha;
        bestOffsetMs = (long)Math.Round(bestOffsetAcc);
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
