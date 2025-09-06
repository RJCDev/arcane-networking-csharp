using Godot;

namespace ArcaneNetworking;

public partial class NetworkedAnimationPlayer : NetworkedComponent
{
    [Export] AnimationPlayer AnimationPlayer;

    public bool IsPlaying(string anim) => AnimationPlayer.CurrentAnimation == anim;

    [Command]
    public void Play(uint[] connsToSendTo, string animationName, bool backwards = false)
    {
        PlayLocal(animationName, backwards); // Play local anytime this is called
        if (!NetworkedNode.AmIOwner) // OnReceive
        {
            // Relay
            if (NetworkManager.AmIServer)
            {
                //GD.Print("[Server] Relaying For: " + NetworkedNode.NetID); // If im headless, send to all, if not, then send to all but our local connection, and the owner of this object
                
                
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

    [Command]
    public void Seek(uint[] connsToSendTo, double seconds, bool freezeSeek = false)
    {
        AnimationPlayer.SpeedScale = freezeSeek ? 0 : 1;
        AnimationPlayer.Seek(seconds);
    }

    [Command]
    public void SetSpeed(uint[] connsToSendTo, float timeScale) => AnimationPlayer.SpeedScale = timeScale;

    [Command]
    public void PlayBackwards(uint[] connsToSendTo, string animationName) => AnimationPlayer.PlayBackwards(animationName);

    [Command]
    public void Stop(uint[] connsToSendTo) => AnimationPlayer.Stop();

}
