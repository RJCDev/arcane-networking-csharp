using System;
using System.Diagnostics;

public class SyncedStopwatch
{
    private readonly Stopwatch stopwatch = new Stopwatch();
    private readonly long serverStartTimeMs; // UTC Unix ms from server

    public SyncedStopwatch(long serverStartTimeMs)
    {
        this.serverStartTimeMs = serverStartTimeMs;

        // Get current UTC Unix time in ms
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // How long since the server started
        long alreadyElapsedMs = nowMs - serverStartTimeMs;

        // Start the stopwatch from that offset
        stopwatch.Start();
        ElapsedOffsetMs = alreadyElapsedMs;
    }

    public long ElapsedOffsetMs { get; }

    /// <summary>
    /// Get total elapsed ms since the server start time
    /// </summary>
    public long ElapsedMs => ElapsedOffsetMs + stopwatch.ElapsedMilliseconds;

    public TimeSpan Elapsed => TimeSpan.FromMilliseconds(ElapsedMs);
}
