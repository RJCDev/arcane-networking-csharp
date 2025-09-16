using System;
using System.Diagnostics;

namespace ArcaneNetworking;

public class ServerTime
{
    private long offsetMs;

    private static long LocalTimeMs() =>
        Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;

    public void Sync(long clientSendTime, long serverTime, long clientReceiveTime)
    {
        long rtt = clientReceiveTime - clientSendTime;
        long latency = rtt / 2;

        // Estimate server time at the moment client received the pong
        long estimatedServerTimeAtReceive = serverTime + latency;

        // Offset = how far off our monotonic clock is from server clock
        offsetMs = estimatedServerTimeAtReceive - clientReceiveTime;
    }

    public long NowMs => LocalTimeMs() + offsetMs;
}