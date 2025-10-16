using System;
using Godot;

namespace ArcaneNetworking;

[GlobalClass]
public partial class NetworkedAnimationPlayer : NetworkedComponent
{
    [Export] public AnimationPlayer LocalPlayer;

    public bool IsPlaying(string anim = "") => anim == "" ? LocalPlayer.IsPlaying() : LocalPlayer.CurrentAnimation == anim;

    // Play
    [Command(Channels.Reliable)]
    public void Play(string animationName, bool backwards = false) => PlayRelay(animationName, backwards);
    [Relay(Channels.Reliable)]
    void PlayRelay(string animationName, bool backwards = false)
    {
        if (LocalPlayer.CurrentAnimation != animationName)
        {
            if (!backwards) LocalPlayer.Play(animationName);
            else LocalPlayer.PlayBackwards(animationName);
        }
    }

    // Seek
    [Command(Channels.Reliable)]
    public void Seek(double seconds, bool freezeSeek = false) => SeekRelay(seconds, freezeSeek);
    void SeekRelay(double seconds, bool freezeSeek = false)
    {
        LocalPlayer.SpeedScale = freezeSeek ? 0 : 1;
        LocalPlayer.Seek(seconds);
    }

    // Set Speed
    [Command(Channels.Reliable)]
    public void SetSpeed(float timeScale) => SetSpeedRelay(timeScale);

    [Relay(Channels.Reliable)]
    void SetSpeedRelay(float timeScale) => LocalPlayer.SpeedScale = timeScale;

    // Stop
    [Command(Channels.Reliable)]
    public void Stop(bool reset = false)
    {
        if (reset) LocalPlayer.Play("RESET");
        StopRelay();
    }

    [Relay(Channels.Reliable)]
    public void StopRelay() => LocalPlayer.Stop();

}
