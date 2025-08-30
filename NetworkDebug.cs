using Godot;
using System;

public partial class NetworkDebug : Node
{
    static long bytesDwnCntr = 0, bytesUpCntr = 0;
    public static double KbpsDwn = 0, KbpsUp = 0;

    public static void OnPacketIn(byte[] data) => bytesDwnCntr += data.Length;
    public static void OnPacketOut(byte[] data) => bytesUpCntr += data.Length;
    public static void ClcltPckSz(double msElapsed)
    {
        if (msElapsed <= 0) return;

        // Bytes/sec
        double downBps = bytesDwnCntr * 1000.0 / msElapsed;
        double upBps = bytesUpCntr * 1000.0 / msElapsed;

        // Convert to kilobits/sec (divide by 1024, then multiply by 8)
        KbpsDwn = downBps * 8.0 / 1024.0;
        KbpsUp = upBps * 8.0 / 1024.0;

        // Reset counters
        bytesDwnCntr = 0;
        bytesUpCntr = 0;

    }
}

