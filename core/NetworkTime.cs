using System;
using System.Diagnostics;
using Godot;

namespace ArcaneNetworking;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public class NetworkTime
{
    private const int MaxSamples = 32;
    private static readonly List<Sample> samples = new List<Sample>(MaxSamples);
    private static double chosenOffsetMs = 0.0; // double for fractional ms during calc
    private static bool hasOffset = false;
    private static readonly double smoothingAlpha = 0.05; // 0..1, small = slow smoothing

    static readonly Queue<ulong> rttSamples = [];

    public static void AddRTTSample(ulong sample)
    {
        rttSamples.Enqueue(sample);

        if (rttSamples.Count > 60)
            rttSamples.Dequeue();
    }

    public static ulong GetRTTAvg()
    {
        if (rttSamples.Count == 0) return 0;

        ulong avg = 0;
        uint count = (uint)rttSamples.Count;
        for (int i = 0; i < count; i++)
        {
            avg += rttSamples.ElementAt(i);
        }
        avg /= count;
        return avg;
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
    public static void AddSample(long t0, long t1, long t2, long t3)
    {
        // Do math in doubles to avoid integer truncation
        long offset = ((t1 - t0) + (t2 - t3)) / 2;
        long delay = (t3 - t0) - (t2 - t1);

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
    public static long TickMS
    {
        get
        {
            if (NetworkManager.AmIServer)
            {
                return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
            else
            {
                if (!hasOffset)
                    return 0;

                double local = LocalTimeMs();
                return (long)Math.Round(local + chosenOffsetMs);
            }
        }
    }

    /// <summary>
    /// Expose last delay for debugging
    /// </summary>
    public static double LastDelayMs => samples.Count == 0 ? double.PositiveInfinity : samples.Last().Delay;
}
