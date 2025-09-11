using ArcaneNetworking;
using Godot;
using MessagePack;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

[MessagePackObject]
public partial struct VoIPPacket : Packet
{
	[Key(0)]
	public ArraySegment<float> Buffer;
}

[GlobalClass]
public partial class NetworkedVOIP : NetworkedComponent
{
	//VOIP
	float[] sendBuffer;
	Queue<Vector2> recieveBuffer;
	[Export] string pushToTalkAction = "voipPtt";
	[Export] AudioStreamPlayer audioInput;
	[Export] Node audioOutput;
	[Export] bool ListenToSelf = false;

	AudioStreamGeneratorPlayback playback;
	private AudioEffectCapture record;

	int mixRate;

	int maxFrames => (int)(mixRate * 0.1f);
	int targetFrames => (int)(mixRate * 0.05f);
	public override void _Ready()
	{
		audioInput.Play();

		if (audioOutput is AudioStreamPlayer3D proxPlayer)
		{
			proxPlayer.Play();
			playback = (AudioStreamGeneratorPlayback)proxPlayer.GetStreamPlayback();
		}
		else if (audioOutput is AudioStreamPlayer2D globalPlayer)
		{
			globalPlayer.Play();
			playback = (AudioStreamGeneratorPlayback)globalPlayer.GetStreamPlayback();
		}

		int idx = AudioServer.GetBusIndex("Record");
		record = (AudioEffectCapture)AudioServer.GetBusEffect(idx, 0);


		mixRate = (int)ProjectSettings.GetSetting("audio/driver/mix_rate");

		sendBuffer = new float[512 * 2];
		recieveBuffer = new(512);

		if (NetworkManager.AmIClient) Client.RegisterPacketHandler<VoIPPacket>(OnReceiveClient);
		if (NetworkManager.AmIServer) Server.RegisterPacketHandler<VoIPPacket>(OnReceiveServer);

	}

	public override void _Process(double delta)
	{
		if (recieveBuffer.Count > mixRate * 0.05f) PlayAudio();

		if (!NetworkedNode.AmIOwner) return;

		if (Input.IsActionPressed(pushToTalkAction))
		{
			Record();
		}
		else record.ClearBuffer();
	}

	private void PlayAudio()
	{
		if (recieveBuffer.Count > maxFrames)
		{
			GD.PushWarning("Audio queue overflow – dropping samples");
			while (recieveBuffer.Count > targetFrames)
				recieveBuffer.Dequeue();
		}
		
		// How many frames can we feed into generator right now?
		int framesWanted = playback.GetFramesAvailable();
		if (framesWanted > 0 && recieveBuffer.Count >= framesWanted)
		{
			Vector2[] chunk = new Vector2[framesWanted];
			for (int i = 0; i < framesWanted; i++)
				chunk[i] = recieveBuffer.Dequeue();
			playback.PushBuffer(chunk);
		}

	}

	private void Record()
	{
		// Get frames from the capture effect

		int available = record.GetFramesAvailable();

		if (record.GetFramesAvailable() > 0)
		{
			var buffer = record.GetBuffer(available);

			var packet = CreatePacket(buffer);
			Client.Send(packet, Channels.Reliable, true); // Send VOIP

			if (ListenToSelf) OnReceiveClient(packet); // Should we listen to the output?


		}

	}

	VoIPPacket CreatePacket(Vector2[] frames)
	{
		// Make sure to resize the send buffer every time we get frames
		Array.Resize(ref sendBuffer, frames.Length * 2);

		for (int i = 0; i < frames.Length; i++)
		{
			sendBuffer[i * 2] = frames[i].X;
			sendBuffer[i * 2 + 1] = frames[i].Y;
		}
		return new VoIPPacket() { Buffer = sendBuffer };
	}

	void OnReceiveClient(VoIPPacket packet)
	{
		// Push into buffer
		for (int i = 0; i < packet.Buffer.Count / 2; i++)
			recieveBuffer.Enqueue(new Vector2(packet.Buffer[i * 2], packet.Buffer[i * 2 + 1]));
		
	}
	void OnReceiveServer(VoIPPacket packet, int conn)
	{
		// Relay instantly
		Server.SendAllExcept(packet, Channels.Reliable, true, conn); // Send VOIP
	}
	

}
