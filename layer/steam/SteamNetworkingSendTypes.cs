namespace ArcaneNetworking
{
    public class SteamNetworkSendTypes
    {
        public const int k_nSteamNetworkingSend_Unreliable = 0;
        public const int k_nSteamNetworkingSend_NoNagle = 1;
        public const int k_nSteamNetworkingSend_UnreliableNoNagle = k_nSteamNetworkingSend_Unreliable | k_nSteamNetworkingSend_NoNagle;
        public const int k_nSteamNetworkingSend_NoDelay = 4;
        public const int k_nSteamNetworkingSend_UnreliableNoDelay = k_nSteamNetworkingSend_Unreliable | k_nSteamNetworkingSend_NoDelay | k_nSteamNetworkingSend_NoNagle;
        public const int k_nSteamNetworkingSend_Reliable = 8;
        public  const int k_nSteamNetworkingSend_ReliableNoNagle = k_nSteamNetworkingSend_Reliable|k_nSteamNetworkingSend_NoNagle;

    }
}