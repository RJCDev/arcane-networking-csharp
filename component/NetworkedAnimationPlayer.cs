using Godot;
using ArcaneNetworking;

public partial class NetworkedAnimationPlayer : NetworkedComponent
{
    [Export] AnimationPlayer AnimationPlayer;

    [MethodRPC]
    public void Play(string animationName, bool backwards = false)
    {
        if (AnimationPlayer.CurrentAnimation != animationName)
        {
            if (!backwards) AnimationPlayer.Play(animationName);
            else AnimationPlayer.PlayBackwards(animationName);
        }
    }

    [MethodRPC]
    public void Seek(double seconds, bool freezeSeek = false)
    {
        AnimationPlayer.SpeedScale = freezeSeek ? 0 : 1;
        AnimationPlayer.Seek(seconds);
    } 

    [MethodRPC]
    public void SetSpeed(float timeScale) => AnimationPlayer.SpeedScale = timeScale; 

    [MethodRPC]
    public void PlayBackwards(string animationName) => AnimationPlayer.PlayBackwards(animationName);

    [MethodRPC]
    public void Stop() => AnimationPlayer.Stop();

}
