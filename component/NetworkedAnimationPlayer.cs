using Godot;

namespace ArcaneNetworking;

public partial class NetworkedAnimationPlayer : NetworkedComponent
{
    [Export] AnimationPlayer AnimationPlayer;

    [MethodRPC]
    public void Play(uint[] connsToSendTo, string animationName, bool backwards = false)
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
