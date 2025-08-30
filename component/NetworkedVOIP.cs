using Godot;
using MessagePack;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

public partial class VOIPReceive : Node
{
	public static List<float> voipReceiveBuffers = new List<float>();

	//VOIP
	[Export] public float InputThreshold = 0.005f;
	[Export] public AudioStreamPlayer VOIPPlayer;
	public AudioStreamGeneratorPlayback playback;
	//Visual
	[Export] MeshInstance3D PlayingIndicator;
	

	public override void _Ready()
	{
		VOIPPlayer.Stream = new AudioStreamGenerator();

		VOIPPlayer.Play();
		playback = VOIPPlayer.GetStreamPlayback() as AudioStreamGeneratorPlayback;

	}


	public override void _Process(double delta)
	{
		//receive
		if (voipReceiveBuffers.Count > 0)
		{
			for (int i = playback.GetFramesAvailable(); i < voipReceiveBuffers.Count; i++)
			{
				playback.PushFrame(new Vector2(voipReceiveBuffers[0], voipReceiveBuffers[0]));

				voipReceiveBuffers.RemoveAt(0);
			}
			PlayingIndicator.Visible = true;
		}
		if (playback.GetFramesAvailable() == 32767) PlayingIndicator.Visible = false;


	}

	void ReceiveData(ulong player, float[] data)
	{
		//if (NetworkSyncronizer._networkOwner != player) return;

		voipReceiveBuffers.AddRange(data);
	}

}
