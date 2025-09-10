// common config struct, instead of passing 10 parameters manually every time.
using System;
using Godot;

namespace kcp2k
{
    [GlobalClass]
    public partial class KcpConfig : Resource
    {
        // socket configuration ////////////////////////////////////////////////
        // DualMode uses both IPv6 and IPv4. not all platforms support it.
        // (Nintendo Switch, etc.)
        [Export] public bool DualMode = true;

        // UDP servers use only one socket.
        // maximize buffer to handle as many connections as possible.
        //
        //   M1 mac pro:
        //     recv buffer default: 786896 (771 KB)
        //     send buffer default:  9216 (9 KB)
        //     max configurable: ~7 MB
        [Export] public int RecvBufferSize = 1024 * 1024 * 7;
        [Export] public int SendBufferSize = 1024 * 9;

        // kcp configuration ///////////////////////////////////////////////////
        // configurable MTU in case kcp sits on top of other abstractions like
        // encrypted transports, relays, etc.
        [Export] public int Mtu = Kcp.MTU_DEF;

        // NoDelay is recommended to reduce latency. This also scales better
        // without buffers getting full.
        [Export] public bool NoDelay = true;

        // KCP internal update interval. 100ms is KCP default, but a lower
        // interval is recommended to minimize latency and to scale to more
        // networked entities.
        [Export] public uint Interval = 10;

        // KCP fastresend parameter. Faster resend for the cost of higher
        // bandwidth.
        [Export] public int FastResend = 0;

        // KCP congestion window heavily limits messages flushed per update.
        // congestion window may actually be broken in kcp:
        // - sending max sized message @ M1 mac flushes 2-3 messages per update
        // - even with super large send/recv window, it requires thousands of
        //   update calls
        // best to leave this disabled, as it may significantly increase latency.
        [Export] public bool CongestionWindow = false;

        // KCP window size can be modified to support higher loads.
        // for example, Mirror Benchmark requires:
        //   128, 128 for 4k monsters
        //   512, 512 for 10k monsters
        //  8192, 8192 for 20k monsters
        [Export] public uint SendWindowSize = Kcp.WND_SND;
        [Export] public uint ReceiveWindowSize = Kcp.WND_RCV;

        // timeout in milliseconds
        [Export] public int Timeout = KcpPeer.DEFAULT_TIMEOUT;

        // maximum retransmission attempts until dead_link
        [Export] public uint MaxRetransmits = Kcp.DEADLINK;

        // constructor /////////////////////////////////////////////////////////
        // constructor with defaults for convenience.
        // makes it easy to define "new KcpConfig(DualMode=false)" etc.
        public KcpConfig(
            bool DualMode = true,
            int RecvBufferSize = 1024 * 1024 * 7,
            int SendBufferSize = 1024 * 9,
            int Mtu = Kcp.MTU_DEF,
            bool NoDelay = true,
            uint Interval = 10,
            int FastResend = 0,
            bool CongestionWindow = false,
            uint SendWindowSize = Kcp.WND_SND,
            uint ReceiveWindowSize = Kcp.WND_RCV,
            int Timeout = KcpPeer.DEFAULT_TIMEOUT,
            uint MaxRetransmits = Kcp.DEADLINK)
        {
            this.DualMode = DualMode;
            this.RecvBufferSize = RecvBufferSize;
            this.SendBufferSize = SendBufferSize;
            this.Mtu = Mtu;
            this.NoDelay = NoDelay;
            this.Interval = Interval;
            this.FastResend = FastResend;
            this.CongestionWindow = CongestionWindow;
            this.SendWindowSize = SendWindowSize;
            this.ReceiveWindowSize = ReceiveWindowSize;
            this.Timeout = Timeout;
            this.MaxRetransmits = MaxRetransmits;
        }
        public KcpConfig() // Paramterless Constructor for Godot Exports
        {
            DualMode = true;
            RecvBufferSize = 1024 * 1024 * 7;
            SendBufferSize = 1024 * 9;
            Mtu = Kcp.MTU_DEF;
            NoDelay = true;
            Interval = 10;
            FastResend = 0;
            CongestionWindow = false;
            SendWindowSize = Kcp.WND_SND;
            ReceiveWindowSize = Kcp.WND_RCV;
            Timeout = KcpPeer.DEFAULT_TIMEOUT;
            MaxRetransmits = Kcp.DEADLINK;
        }
    }
}
