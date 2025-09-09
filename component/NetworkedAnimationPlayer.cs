using System;
using Godot;

namespace ArcaneNetworking;

public partial class NetworkedAnimationPlayer : NetworkedComponent
{
    [Export] AnimationPlayer AnimationPlayer;

    public bool IsPlaying(string anim = "") => anim == "" ? AnimationPlayer.IsPlaying() : AnimationPlayer.IsPlaying() && AnimationPlayer.CurrentAnimation == anim;

    // Play
    [Command(Channels.Reliable)]
    public void Play(string animationName, bool backwards = false) => PlayRelay(animationName, backwards);
    [Relay(Channels.Reliable)]
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

    [Relay(Channels.Reliable)]
    void SetSpeedRelay(float timeScale) => AnimationPlayer.SpeedScale = timeScale;

    // Stop
    [Command(Channels.Reliable)]
    public void Stop(bool reset = false)
    {
        if (reset) AnimationPlayer.Play("RESET");
        StopRelay();
    }

    [Relay(Channels.Reliable)]
    public void StopRelay() => AnimationPlayer.Stop();

}
