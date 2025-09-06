using System;
using Godot;

namespace ArcaneNetworking;

public partial class NetworkedAnimationPlayer : NetworkedComponent
{
    [Export] AnimationPlayer AnimationPlayer;

    public bool IsPlaying(string anim) => AnimationPlayer.CurrentAnimation == anim;

    // Play
    [Command(Channels.Reliable)]
    public void Play(string animationName, bool backwards = false) => PlayRelay(animationName, backwards);
    [Relay]
    void PlayRelay(string animationName, bool backwards = false)
    {
        if (AnimationPlayer.CurrentAnimation != animationName)
        {
            if (!backwards) AnimationPlayer.Play(animationName);
            else AnimationPlayer.PlayBackwards(animationName);
        }
    }

    // Seek
    [Command(Channels.Reliable)]
    public void Seek(double seconds, bool freezeSeek = false) => SeekRelay(seconds, freezeSeek);
    void SeekRelay(double seconds, bool freezeSeek = false)
    {
        AnimationPlayer.SpeedScale = freezeSeek ? 0 : 1;
        AnimationPlayer.Seek(seconds);
    }

    // Set Speed
    [Command(Channels.Reliable)]
    public void SetSpeed(float timeScale) => SetSpeedRelay(timeScale);

    [Relay]
    void SetSpeedRelay(float timeScale) => AnimationPlayer.SpeedScale = timeScale;

    // Stop
    [Command(Channels.Reliable)]
    public void Stop() => StopRelay();

    [Relay]
    public void StopRelay() => AnimationPlayer.Stop();

}
