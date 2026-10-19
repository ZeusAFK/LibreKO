using System.Buffers.Binary;
using Concentus;
using Concentus.Enums;
using Godot;

namespace LibreKO;

public partial class VoiceChat : Node
{
    public const string BusVoiceChat = "VoiceChat";
    public const string BusMic = "Mic";
    private const int SampleRate = PcmFrameCutter.SampleRate;
    private const int FrameSamples = PcmFrameCutter.FrameSamples;
    private const int LiveBitrate = 24000;
    private const int ExpectedLossPercent = 10;
    private const int MaxOpusPacket = 400;
    private const double HelloSeconds = 15.0;
    private const double ProbeSeconds = 3.0;
    private const double ProbeHelloSeconds = 0.5;
    private const double SpeakingHoldSeconds = 0.3;
    private const float GeneratorSeconds = 0.25f;
    private const float SilentDb = -80f;
    private const string AndroidRecordAudio = "android.permission.RECORD_AUDIO";

    public enum Transport
    {
        Off,
        Probing,
        Udp,
        Tcp,
    }

    private sealed class Speaker(int characterId, IOpusDecoder decoder, AudioStreamPlayer player)
    {
        public int CharacterId { get; } = characterId;
        public VoiceJitterBuffer Jitter { get; } = new();
        public IOpusDecoder Decoder { get; } = decoder;
        public AudioStreamPlayer Player { get; } = player;
        public AudioStreamGeneratorPlayback? Playback { get; set; }
        public float[] Pcm { get; } = new float[FrameSamples];
        public Vector2[] Stereo { get; } = new Vector2[FrameSamples];
    }

    public readonly record struct PeerMix(float Volume, bool Muted);

    public static VoiceChat? I { get; private set; }

    private AudioStreamPlayer micPlayer = null!;
    private AudioEffectCapture capture = null!;
    private bool micOn;
    private LinearResampler? micResampler;
    private float[] micMono = new float[4096];
    private float[] micLeft = new float[4096];
    private float[] micRight = new float[4096];
    private float[] micResampled = new float[8192];
    private readonly float[] frame = new float[FrameSamples];
    private readonly byte[] opus = new byte[MaxOpusPacket];
    private readonly byte[] datagram = new byte[MaxOpusPacket + VoiceSeal.Overhead + 8];
    private readonly byte[] plainBuffer = new byte[MaxOpusPacket + 16];
    private readonly PcmFrameCutter cutter = new();
    private readonly VoiceActivityGate gate = new();
    private readonly VoiceAutoGain autoGain = new();
    private IOpusEncoder? encoder;
    private VoiceSeal? seal;
    private PacketPeerUdp? udpPeer;
    private double probeLeft;
    private double helloLeft;
    private ushort seq;
    private byte flags;
    private double clock;
    private double selfSpeakingUntil = double.NegativeInfinity;
    private readonly Dictionary<int, Speaker> speakers = new();
    private readonly Dictionary<int, PeerMix> peerMixes = new();
    private readonly Dictionary<int, double> lastHeard = new();

    public event Action? StateChanged;

    public Transport CurrentTransport { get; private set; }
    public bool InVoice => roomMedia.Voice != null;
    public bool MicMuted => (flags & VoiceFlags.MicMuted) != 0;
    public bool Deafened => (flags & VoiceFlags.Deafened) != 0;
    public bool CanSpeak => roomMedia.Voice is { } voice && (voice.SelfFlags & (VoiceFlags.CannotSpeak | VoiceFlags.Silenced)) == 0;
    public bool SilencedByLeader => roomMedia.Voice is { } voice && (voice.SelfFlags & VoiceFlags.Silenced) != 0;
    public float MicLevel => gate.Level;
    public bool SelfSpeaking => clock < selfSpeakingUntil;

    private RoomMediaSession roomMedia = null!;
    private NationChatSession nationChat = null!;
    private SelfSession self = null!;
    private IRoomMediaRequests roomMediaRequests = null!;

    public static VoiceChat Ensure(Node host, RoomMediaSession roomMedia, NationChatSession nationChat, SelfSession self,
        IRoomMediaRequests roomMediaRequests)
    {
        if (I != null && IsInstanceValid(I)) return I;
        var chat = new VoiceChat
        {
            Name = "VoiceChat",
            roomMedia = roomMedia,
            nationChat = nationChat,
            self = self,
            roomMediaRequests = roomMediaRequests,
        };
        host.AddChild(chat);
        return chat;
    }

    public override void _Ready()
    {
        I = this;
        ProcessMode = ProcessModeEnum.Always;
        OpusCodecFactory.AttemptToUseNativeLibrary = false;
        EnsureBuses();
        micPlayer = new AudioStreamPlayer { Name = "Microphone", Bus = BusMic };
        AddChild(micPlayer);
        ApplyVolume();
        Config.VoiceChatChanged += ApplyVolume;
        Config.VoiceChatChanged += ApplyInputDevice;
        roomMedia.VoiceJoinedEvent += OnJoined;
        roomMedia.VoiceLeftEvent += OnLeft;
        roomMedia.VoicePeerEvent += OnPeer;
        roomMedia.VoiceTcpFrameEvent += OnFrame;
        PruneNoteCache();
    }

    public override void _ExitTree()
    {
        Config.VoiceChatChanged -= ApplyVolume;
        Config.VoiceChatChanged -= ApplyInputDevice;
        roomMedia.VoiceJoinedEvent -= OnJoined;
        roomMedia.VoiceLeftEvent -= OnLeft;
        roomMedia.VoicePeerEvent -= OnPeer;
        roomMedia.VoiceTcpFrameEvent -= OnFrame;
        TearDownLive();
        if (I == this) I = null;
    }

    private void EnsureBuses()
    {
        if (AudioServer.GetBusIndex(BusVoiceChat) < 0)
        {
            int idx = AudioServer.BusCount;
            AudioServer.AddBus(idx);
            AudioServer.SetBusName(idx, BusVoiceChat);
            AudioServer.SetBusSend(idx, Audio.BusMaster);
        }
        int voice = AudioServer.GetBusIndex(BusVoiceChat);
        if (AudioServer.GetBusEffectCount(voice) == 0)
            AudioServer.AddBusEffect(voice, new AudioEffectHardLimiter { CeilingDb = VoiceLevels.LimiterCeilingDb });
        int mic = AudioServer.GetBusIndex(BusMic);
        if (mic < 0)
        {
            mic = AudioServer.BusCount;
            AudioServer.AddBus(mic);
            AudioServer.SetBusName(mic, BusMic);
            AudioServer.SetBusSend(mic, Audio.BusMaster);
            AudioServer.SetBusMute(mic, true);
            AudioServer.AddBusEffect(mic, new AudioEffectCapture { BufferLength = 0.5f });
        }
        capture = (AudioEffectCapture)AudioServer.GetBusEffect(mic, 0);
    }

    private static void ApplyVolume()
    {
        int idx = AudioServer.GetBusIndex(BusVoiceChat);
        if (idx < 0) return;
        float linear = Config.VoiceChatVolume;
        AudioServer.SetBusVolumeDb(idx, 0f);
        AudioServer.SetBusMute(idx, linear <= 0f);
        if (AudioServer.GetBusEffectCount(idx) > 0 && AudioServer.GetBusEffect(idx, 0) is AudioEffectHardLimiter limiter)
            limiter.PreGainDb = linear <= 0f ? 0f : Mathf.LinearToDb(linear);
        else AudioServer.SetBusVolumeDb(idx, linear <= 0f ? SilentDb : Mathf.LinearToDb(linear));
    }

    private static void ApplyInputDevice()
    {
        string wanted = Config.VoiceInputDevice;
        foreach (string device in AudioServer.GetInputDeviceList())
            if (device == wanted)
            {
                AudioServer.InputDevice = device;
                return;
            }
        AudioServer.InputDevice = Config.DefaultAudioDevice;
    }

    public static string[] InputDevices() => AudioServer.GetInputDeviceList();

    private Action? afterPermission;
    private bool permissionHooked;

    private bool AskForMicrophone(Action then)
    {
        if (OS.GetName() != "Android") return true;
        foreach (string granted in OS.GetGrantedPermissions())
            if (granted == AndroidRecordAudio) return true;
        afterPermission = then;
        if (!permissionHooked)
        {
            permissionHooked = true;
            GetTree().OnRequestPermissionsResult += OnPermissionResult;
        }
        OS.RequestPermission(AndroidRecordAudio);
        return false;
    }

    private void OnPermissionResult(string permission, bool granted)
    {
        if (permission != AndroidRecordAudio) return;
        var then = afterPermission;
        afterPermission = null;
        if (granted) then?.Invoke();
        else NoteNotice?.Invoke(noticeTarget, "Voice chat needs access to the microphone.");
    }

    public void Join()
    {
        if (InVoice || !nationChat.NationChat.InRoom) return;
        noticeTarget = "";
        if (!AskForMicrophone(Join)) return;
        roomMediaRequests.VoiceJoin();
    }

    public void Leave() => roomMediaRequests.VoiceLeave();

    public void ToggleMute() => SetFlags((byte)(flags ^ VoiceFlags.MicMuted));

    public void ToggleDeafen() => SetFlags((byte)(flags ^ VoiceFlags.Deafened));

    private void SetFlags(byte flags)
    {
        this.flags = flags;
        roomMediaRequests.VoiceState(this.flags);
        foreach (var speaker in speakers.Values) ApplyMix(speaker);
        StateChanged?.Invoke();
    }

    public bool IsSpeaking(int characterId) =>
        lastHeard.TryGetValue(characterId, out double heard) && clock - heard < SpeakingHoldSeconds;

    internal void PreviewSpeaking(int characterId, bool isSelf)
    {
        if (isSelf) selfSpeakingUntil = double.PositiveInfinity;
        else lastHeard[characterId] = double.PositiveInfinity;
    }

    public PeerMix MixOf(int characterId) => peerMixes.TryGetValue(characterId, out var mix) ? mix : new PeerMix(1f, false);

    public void SetMix(int characterId, PeerMix mix)
    {
        peerMixes[characterId] = new PeerMix(Math.Clamp(mix.Volume, 0f, VoiceLevels.MaxVolume), mix.Muted);
        if (speakers.TryGetValue(characterId, out var speaker)) ApplyMix(speaker);
        StateChanged?.Invoke();
    }

    private void ApplyMix(Speaker speaker)
    {
        var mix = MixOf(speaker.CharacterId);
        bool silent = mix.Muted || mix.Volume <= 0f || Deafened;
        speaker.Player.VolumeDb = silent ? SilentDb : Mathf.LinearToDb(mix.Volume);
    }

    private void OnJoined(VoiceSession session)
    {
        TearDownLive();
        flags = 0;
        encoder = OpusCodecFactory.CreateEncoder(SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
        encoder.Bitrate = LiveBitrate;
        encoder.UseInbandFEC = true;
        encoder.PacketLossPercent = ExpectedLossPercent;
        encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
        seq = 0;
        gate.Reset();
        autoGain.Reset();
        cutter.Clear();
        bool udp = session.UdpPort != 0 && VoiceSeal.Supported && Game.Connection.ServerHost.Length > 0;
        if (udp)
        {
            seal = new VoiceSeal(session.SessionId, session.Key);
            udpPeer = new PacketPeerUdp();
            string address = Game.Connection.ServerHost.IsValidIPAddress() ? Game.Connection.ServerHost : IP.ResolveHostname(Game.Connection.ServerHost, IP.Type.Ipv4);
            udp = address.Length > 0 && udpPeer.ConnectToHost(address, session.UdpPort) == Error.Ok;
        }
        if (udp)
        {
            CurrentTransport = Transport.Probing;
            probeLeft = ProbeSeconds;
            SendHello();
        }
        else UseTcp();
        RetainMic(true);
        StateChanged?.Invoke();
    }

    private void OnLeft()
    {
        TearDownLive();
        StateChanged?.Invoke();
    }

    private void OnPeer(int characterId, RoomMemberChange change, byte flags)
    {
        if (change == RoomMemberChange.Left && speakers.Remove(characterId, out var speaker))
            speaker.Player.QueueFree();
        StateChanged?.Invoke();
    }

    private void TearDownLive()
    {
        foreach (var speaker in speakers.Values)
            if (IsInstanceValid(speaker.Player)) speaker.Player.QueueFree();
        speakers.Clear();
        lastHeard.Clear();
        udpPeer?.Close();
        udpPeer = null;
        seal?.Dispose();
        seal = null;
        encoder = null;
        CurrentTransport = Transport.Off;
        selfSpeakingUntil = double.NegativeInfinity;
        RetainMic(false);
    }

    private void UseTcp()
    {
        CurrentTransport = Transport.Tcp;
        udpPeer?.Close();
        udpPeer = null;
        roomMediaRequests.VoiceTransport(true);
        StateChanged?.Invoke();
    }

    private void SendHello()
    {
        helloLeft = CurrentTransport == Transport.Probing ? ProbeHelloSeconds : HelloSeconds;
        SendDatagram([VoiceDatagram.Hello]);
    }

    private void SendDatagram(ReadOnlySpan<byte> plain)
    {
        if (udpPeer == null || seal == null) return;
        int length = seal.Seal(VoiceSeal.ClientToServer, plain, datagram);
        udpPeer.PutPacket(datagram.AsSpan(0, length).ToArray());
    }

    public override void _Process(double delta)
    {
        clock += delta;
        if (micOn) PumpMicrophone();
        if (CurrentTransport is Transport.Probing or Transport.Udp) PumpUdp(delta);
        foreach (var speaker in speakers.Values) FillSpeaker(speaker);
        ProcessNotes();
    }

    private void RetainMic(bool live)
    {
        bool want = (live && InVoice) || Recording;
        if (want == micOn) return;
        micOn = want;
        if (want)
        {
            ApplyInputDevice();
            capture.ClearBuffer();
            micResampler = new LinearResampler((int)AudioServer.GetMixRate(), SampleRate);
            micPlayer.Stream = new AudioStreamMicrophone();
            micPlayer.Play();
        }
        else
        {
            micPlayer.Stop();
            micPlayer.Stream = null;
            cutter.Clear();
            gate.Reset();
        }
    }

    private void PumpMicrophone()
    {
        int available = capture.GetFramesAvailable();
        if (available <= 0 || micResampler == null) return;
        Vector2[] stereo = capture.GetBuffer(available);
        if (micMono.Length < stereo.Length)
        {
            micMono = new float[stereo.Length * 2];
            micLeft = new float[stereo.Length * 2];
            micRight = new float[stereo.Length * 2];
        }
        for (int i = 0; i < stereo.Length; i++)
        {
            micLeft[i] = stereo[i].X;
            micRight[i] = stereo[i].Y;
        }
        VoiceLevels.ToMono(micLeft.AsSpan(0, stereo.Length), micRight.AsSpan(0, stereo.Length), micMono);
        int room = micResampler.MaxOutput(stereo.Length);
        if (micResampled.Length < room) micResampled = new float[room * 2];
        int written = micResampler.Process(micMono.AsSpan(0, stereo.Length), micResampled);
        cutter.Push(micResampled.AsSpan(0, written));
        while (cutter.TryTake(frame))
        {
            bool open = gate.Process(frame, Config.VoiceThreshold);
            bool voice = gate.Above(Config.VoiceThreshold);
            if (Recording) RecordFrame(frame, voice);
            if (!InVoice) continue;
            autoGain.Process(frame, voice);
            Transmit(open);
        }
    }

    private bool PushToTalkHeld() =>
        KeyBinds.Held(KeyAction.VoicePushToTalk) && GetViewport()?.GuiGetFocusOwner() is not (LineEdit or TextEdit);

    private void Transmit(bool gateOpen)
    {
        if (encoder == null || MicMuted || !CanSpeak || CurrentTransport == Transport.Off) return;
        bool send = Config.VoicePushToTalk ? PushToTalkHeld() : gateOpen;
        if (!send) return;
        int length;
        try
        {
            length = encoder.Encode(frame, FrameSamples, opus, opus.Length);
        }
        catch (OpusException)
        {
            return;
        }
        if (length <= 0) return;
        ushort seq = this.seq++;
        selfSpeakingUntil = clock + SpeakingHoldSeconds;
        if (CurrentTransport == Transport.Tcp)
        {
            roomMediaRequests.VoiceFrame(seq, opus.AsSpan(0, length));
            return;
        }
        plainBuffer[0] = VoiceDatagram.Frame;
        BinaryPrimitives.WriteUInt16LittleEndian(plainBuffer.AsSpan(1), seq);
        opus.AsSpan(0, length).CopyTo(plainBuffer.AsSpan(VoiceDatagram.ClientFrameHeader));
        SendDatagram(plainBuffer.AsSpan(0, VoiceDatagram.ClientFrameHeader + length));
    }

    private void PumpUdp(double delta)
    {
        if (udpPeer == null || seal == null) return;
        while (udpPeer.GetAvailablePacketCount() > 0)
        {
            byte[] packet = udpPeer.GetPacket();
            if (!seal.TryOpen(VoiceSeal.ServerToClient, packet, plainBuffer, out int length) || length < 1) continue;
            if (plainBuffer[0] == VoiceDatagram.Hello)
            {
                if (CurrentTransport == Transport.Probing)
                {
                    CurrentTransport = Transport.Udp;
                    StateChanged?.Invoke();
                }
                continue;
            }
            if (plainBuffer[0] != VoiceDatagram.Frame || length <= VoiceDatagram.ServerFrameHeader) continue;
            int speaker = BinaryPrimitives.ReadInt32LittleEndian(plainBuffer.AsSpan(1));
            ushort seq = BinaryPrimitives.ReadUInt16LittleEndian(plainBuffer.AsSpan(5));
            OnFrame(speaker, seq, plainBuffer.AsSpan(VoiceDatagram.ServerFrameHeader, length - VoiceDatagram.ServerFrameHeader).ToArray());
        }
        if (CurrentTransport == Transport.Probing && (probeLeft -= delta) <= 0)
        {
            UseTcp();
            return;
        }
        if ((helloLeft -= delta) <= 0) SendHello();
    }

    private void OnFrame(int characterId, ushort seq, byte[] opus)
    {
        if (!InVoice || characterId == self.MyCharId) return;
        if (!speakers.TryGetValue(characterId, out var speaker))
        {
            var player = new AudioStreamPlayer
            {
                Name = $"Voice{characterId}",
                Bus = BusVoiceChat,
                Stream = new AudioStreamGenerator
                {
                    MixRateMode = AudioStreamGenerator.AudioStreamGeneratorMixRate.Custom,
                    MixRate = SampleRate,
                    BufferLength = GeneratorSeconds,
                },
            };
            AddChild(player);
            player.Play();
            speaker = new Speaker(characterId, OpusCodecFactory.CreateDecoder(SampleRate, 1), player)
            {
                Playback = player.GetStreamPlayback() as AudioStreamGeneratorPlayback,
            };
            speakers[characterId] = speaker;
            ApplyMix(speaker);
        }
        lastHeard[characterId] = clock;
        speaker.Jitter.Push(seq, opus);
    }

    private static void FillSpeaker(Speaker speaker)
    {
        var playback = speaker.Playback;
        if (playback == null) return;
        while (playback.GetFramesAvailable() >= FrameSamples)
        {
            var output = speaker.Jitter.Pop(out byte[]? packet);
            if (output == JitterOutput.Wait) return;
            int decoded;
            try
            {
                decoded = output == JitterOutput.Frame
                    ? speaker.Decoder.Decode(packet, speaker.Pcm, FrameSamples, false)
                    : speaker.Decoder.Decode(packet ?? ReadOnlySpan<byte>.Empty, speaker.Pcm, FrameSamples, packet != null);
            }
            catch (OpusException)
            {
                continue;
            }
            for (int i = 0; i < decoded; i++) speaker.Stereo[i] = new Vector2(speaker.Pcm[i], speaker.Pcm[i]);
            if (decoded > 0) playback.PushBuffer(decoded == FrameSamples ? speaker.Stereo : speaker.Stereo[..decoded]);
        }
    }
}

public static class VoiceDatagram
{
    public const byte Hello = 1;
    public const byte Frame = 2;
    public const int ClientFrameHeader = 3;
    public const int ServerFrameHeader = 7;
}
