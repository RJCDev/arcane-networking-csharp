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
	Queue<Vector2> sendQueue;
	Queue<Vector2> receiveQueue;
	float[] sendBufferRaw;

	[Export] string pushToTalkAction = "voipPtt";
	[Export] AudioStreamPlayer audioInput;
	[Export] Node audioOutput;
	[Export] bool ListenToSelf = false;

	AudioStreamGeneratorPlayback playback;
	private AudioEffectCapture record;


	Vector2 lastSample;
	float interp = 0f;

	int sampleRate;
	int targetFrames => sampleRate * 50 / 1000;
	int prebufferFrames => targetFrames * 100 / 1000;

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


		sampleRate = (int)ProjectSettings.GetSetting("audio/driver/mix_rate");
		sendQueue = new();
		receiveQueue = new();
		sendBufferRaw = new float[targetFrames * 2]; // 2 Points per sample

		if (NetworkManager.AmIClient) Client.RegisterPacketHandler<VoIPPacket>(OnReceiveClient);
		if (NetworkManager.AmIServer) Server.RegisterPacketHandler<VoIPPacket>(OnReceiveServer);

	}

	public override void _Process(double delta)
	{
		
		PlayAudio();

		if (!NetworkedNode.AmIOwner) return;

		if (Input.IsActionPressed(pushToTalkAction))
		{
			Record();
		}

	}

	private void PlayAudio()
	{
		// Only start playback after prebuffer
		if (receiveQueue.Count < prebufferFrames)
			return;

		int framesWanted = playback.GetFramesAvailable();
		if (framesWanted <= 0) return;

		// Compute drift
		int drift = receiveQueue.Count - targetFrames;

		// Small speed adjustment based on drift
		// Example: ±1% speed
		float speedAdjustment = Mathf.Clamp(drift / (float)targetFrames, -0.01f, 0.01f);
		float playbackRate = 1.0f + speedAdjustment;

		Vector2[] chunk = new Vector2[framesWanted];

		for (int i = 0; i < framesWanted; i++)
		{
			Vector2 nextSample = receiveQueue.Count > 0 ? receiveQueue.Peek() : lastSample;

			float t = Math.Min(interp, 1f);
			chunk[i] = lastSample * (1 - t) + nextSample * t;

			interp += playbackRate;

			if (interp >= 1f && receiveQueue.Count > 0)
			{
				interp -= 1f;
				lastSample = receiveQueue.Dequeue();
			}
		}

		playback.PushBuffer(chunk);

    }



	private void Record()
	{
		int available = record.GetFramesAvailable();

		if (available > 0)
		{
			int framesToTake = Math.Min(targetFrames, available);
			foreach (var frame in record.GetBuffer(framesToTake))
				sendQueue.Enqueue(frame);
		}

		// Flush the batch
		if (sendQueue.Count >= targetFrames) // 50 ms
		{
			var packet = CreatePacket(sendQueue);
			Client.Send(packet, Channels.Reliable, true); // Send VOIP

			if (ListenToSelf) OnReceiveClient(packet); // Should we listen to the output? 
		}
	}

	VoIPPacket CreatePacket(Queue<Vector2> frames)
	{
		// Deque samples into raw buffer
		for (int i = 0; i < targetFrames; i++)
		{
			var Sample = sendQueue.Dequeue();
			sendBufferRaw[i * 2] = Sample.X;
			sendBufferRaw[i * 2 + 1] = Sample.Y;
		}
				
		return new VoIPPacket() { Buffer = sendBufferRaw };
	}

	void OnReceiveClient(VoIPPacket packet)
	{
		// Push into buffer
		for (int i = 0; i < packet.Buffer.Count / 2; i++)
		{
			receiveQueue.Enqueue(new Vector2(packet.Buffer[i * 2], packet.Buffer[i * 2 + 1]));
		}
			
		
	}
	void OnReceiveServer(VoIPPacket packet, int conn)
	{
		// Relay instantly
		Server.SendAllExcept(packet, Channels.Reliable, true, conn); // Send VOIP
	}
	

}
