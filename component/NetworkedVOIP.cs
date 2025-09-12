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
	public ArraySegment<byte> Buffer;
}

[GlobalClass]
public partial class NetworkedVOIP : NetworkedComponent
{
	JitterBuffer jitterBuffer;
	//VOIP
	Vector2[] captureBuffer;  // from AudioEffectCapture
	byte[] sendBuffer;
	Vector2[] receiveBuffer;

	[Export] string pushToTalkAction = "voipPtt";
	[Export] AudioStreamPlayer audioInput;
	[Export] Node audioOutput;
	[Export] bool ListenToSelf = false;

	AudioStreamGeneratorPlayback playback;
	private AudioEffectCapture record;

	Vector2 lastSample;
	float interp = 0f;

	int sampleRate;
	int targetFrames => (int)(sampleRate * 0.015f);

	public override void _Ready()
	{
		jitterBuffer = new(targetFrames);

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

		captureBuffer = new Vector2[targetFrames];
		sendBuffer = new byte[captureBuffer.Length * 2]; // 2 Points per sample
		receiveBuffer = new Vector2[captureBuffer.Length];

		if (NetworkManager.AmIClient) Client.RegisterPacketHandler<VoIPPacket>(OnReceiveClient);
		if (NetworkManager.AmIServer) Server.RegisterPacketHandler<VoIPPacket>(OnReceiveServer);

	}

	public override void _Process(double delta)
	{
		if (!NetworkedNode.AmIOwner) return;
		
		if (Input.IsActionPressed(pushToTalkAction))
		{
			Record();
		}
	}
    public override void _PhysicsProcess(double delta)
    {
        PlayAudioIfAvailable();
		
    }

	void PlayAudioIfAvailable()
	{
		while (jitterBuffer.IsReady && playback.CanPushBuffer(targetFrames))
		{
			var frame = jitterBuffer.Pop();
			playback.PushBuffer(new ReadOnlySpan<Vector2>(frame));
		}
	}
	private void Record()
	{
		if (record.CanGetBuffer(targetFrames))
		{
			var packet = CreatePacket();
			Client.Send(packet, Channels.Unreliable, true); // Send VOIP

			if (ListenToSelf) OnReceiveClient(packet); // Should we listen to the output? 

		}
	}

	VoIPPacket CreatePacket()
	{
		captureBuffer = record.GetBuffer(targetFrames);

		int sendBytes = CompressFrame(captureBuffer, sendBuffer);

		return new VoIPPacket() { Buffer = new ArraySegment<byte>(sendBuffer, 0, sendBytes) };
	}

	int CompressFrame(Vector2[] buffer, byte[] sendBuffer)
	{
		int length = buffer.Length;

		for (int i = 0; i < length; i++)
		{
			// Downmix to mono
			float mono = (buffer[i].X + buffer[i].Y) * 0.5f;
			short pcm = (short)(Mathf.Clamp(mono, -1f, 1f) * 32767);

			// Write PCM16 into sendBuffer
			sendBuffer[i * 2] = (byte)(pcm & 0xFF);
			sendBuffer[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
		}

		return length * 2; // number of bytes written
	}
	int DecompressFrame(ArraySegment<byte> data, int byteCount, Vector2[] receiveBuffer)
	{
		int sampleCount = byteCount / 2;

		for (int i = 0; i < sampleCount; i++)
		{
			short pcm = (short)(data[i * 2] | (data[i * 2 + 1] << 8));
			float sample = pcm / 32767f;

			// Expand mono to stereo
			receiveBuffer[i].X = sample;
			receiveBuffer[i].Y = sample;
		}

		return sampleCount; // number of Vector2 samples written
	}
	void OnReceiveClient(VoIPPacket packet)
	{
		// Push into buffer
		int read = DecompressFrame(packet.Buffer, packet.Buffer.Count, receiveBuffer);

		var frame = new Vector2[read];
    	Array.Copy(receiveBuffer, frame, read);

		jitterBuffer.Push(frame);
	}
	void OnReceiveServer(VoIPPacket packet, int conn)
	{
		// Relay instantly
		Server.SendAllExcept(packet, Channels.Unreliable, true, conn); // Send VOIP
	}


}

public class JitterBuffer
{
    private readonly Queue<Vector2[]> buffer = new();
    private readonly int packetSize;

    public JitterBuffer(int packetSize, int capacityPackets = 3)
    {
        this.packetSize = packetSize;
        for (int i = 0; i < capacityPackets * 2; i++)
            buffer.Enqueue(new Vector2[packetSize]);
    }

    private readonly Queue<Vector2[]> filled = new();

    public void Push(Vector2[] samples)
    {
        if (filled.Count < buffer.Count)
        {
            filled.Enqueue(samples);
        }
    }

    public Vector2[] Pop()
    {
        return filled.Count > 0 ? filled.Dequeue() : new Vector2[packetSize];
    }

    public bool IsReady => filled.Count > 0;
}
