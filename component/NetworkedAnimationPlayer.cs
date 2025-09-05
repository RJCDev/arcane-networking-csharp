using Godot;

namespace ArcaneNetworking;

public partial class NetworkedAnimationPlayer : NetworkedComponent
{
    [Export] AnimationPlayer AnimationPlayer;

    public bool IsPlaying(string anim) => AnimationPlayer.CurrentAnimation == anim;

    [MethodRPC]
    public void Play(uint[] connsToSendTo, string animationName, bool backwards = false)
    {
        PlayLocal(animationName, backwards); // Play local anytime this is called
        if (!NetworkedNode.AmIOwner) // OnReceive
        {
            // Relay
            if (NetworkManager.AmIServer)
            {
                uint[] relayConnections = Server.GetConnsExcluding(Client.serverConnection.localID, NetworkedNode.OwnerID);

                if (relayConnections.Length > 0)
                {
                    //GD.Print("[Server] Relaying For: " + NetworkedNode.NetID); // If im headless, send to all, if not, then send to all but our local connection, and the owner of this object
                    Play(relayConnections, animationName, backwards);
                }
            }
        }
    }
    void PlayLocal(string animationName, bool backwards = false)
    {
        if (AnimationPlayer.CurrentAnimation != animationName)
        {
            if (!backwards) AnimationPlayer.Play(animationName);
            else AnimationPlayer.PlayBackwards(animationName);
        }
    }

    [MethodRPC]
    public void Seek(uint[] connsToSendTo, double seconds, bool freezeSeek = false)
    {
        AnimationPlayer.SpeedScale = freezeSeek ? 0 : 1;
        AnimationPlayer.Seek(seconds);
    }

    [MethodRPC]
    public void SetSpeed(uint[] connsToSendTo, float timeScale) => AnimationPlayer.SpeedScale = timeScale;

    [MethodRPC]
    public void PlayBackwards(uint[] connsToSendTo, string animationName) => AnimationPlayer.PlayBackwards(animationName);

    [MethodRPC]
    public void Stop(uint[] connsToSendTo) => AnimationPlayer.Stop();

}
