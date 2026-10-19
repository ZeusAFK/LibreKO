using System;
using System.Collections.Generic;
using System.IO;
using Concentus;
using Concentus.Enums;
using Concentus.Oggfile;

namespace LibreKO.Domain;

public sealed class VoiceNoteRecorder : IDisposable
{
    public const int Bitrate = 20000;
    public const int LeadInMs = 300;
    public const double LeadInSeconds = LeadInMs / 1000.0;
    private const int ResamplerQuality = 5;
    private const int LeadInFrames = LeadInMs / PcmFrameCutter.FrameMs;
    private const int MaxWaitFrames = VoiceNoteRules.MaxSeconds * 1000 / PcmFrameCutter.FrameMs;

    private readonly MemoryStream file = new();
    private readonly OpusOggWriteStream ogg;
    private readonly float[] frame = new float[PcmFrameCutter.FrameSamples];
    private readonly List<float> levels = new();
    private readonly List<float[]> beforeVoice = new();
    private bool voiceHeard;

    public VoiceNoteRecorder()
    {
        OpusCodecFactory.AttemptToUseNativeLibrary = false;
        var encoder = OpusCodecFactory.CreateEncoder(PcmFrameCutter.SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
        encoder.Bitrate = Bitrate;
        encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
        ogg = new OpusOggWriteStream(encoder, file, new OpusTags(), PcmFrameCutter.SampleRate, ResamplerQuality, true);
    }

    public int Samples { get; private set; }

    public double Seconds => Samples / (double)PcmFrameCutter.SampleRate;

    public bool Full => Seconds >= VoiceNoteRules.MaxSeconds || beforeVoice.Count >= MaxWaitFrames;

    public void Write(ReadOnlySpan<float> pcm, bool voice = true)
    {
        if (!voiceHeard && !voice)
        {
            beforeVoice.Add(pcm.ToArray());
            return;
        }
        if (!voiceHeard)
        {
            voiceHeard = true;
            for (int i = Math.Max(0, beforeVoice.Count - LeadInFrames); i < beforeVoice.Count; i++) Encode(beforeVoice[i]);
            beforeVoice.Clear();
        }
        Encode(pcm);
    }

    private void Encode(ReadOnlySpan<float> pcm)
    {
        int count = Math.Min(pcm.Length, frame.Length);
        pcm[..count].CopyTo(frame);
        ogg.WriteSamples(frame, 0, count);
        levels.Add(VoiceWaveform.Rms(pcm[..count]));
        Samples += count;
    }

    public byte[] Waveform() => VoiceWaveform.FromLevels(levels);

    public byte[] Finish()
    {
        foreach (var frame in beforeVoice) Encode(frame);
        beforeVoice.Clear();
        ogg.Finish();
        return file.ToArray();
    }

    public void Dispose() => file.Dispose();
}

public static class VoiceNoteCodec
{
    public const float TargetPeak = 0.89f;
    public const float MaxGain = 8f;

    public static float Normalize(short[] pcm)
    {
        int peak = 0;
        foreach (short sample in pcm) peak = Math.Max(peak, Math.Abs((int)sample));
        if (peak == 0) return 1f;
        float gain = Math.Min(MaxGain, TargetPeak * short.MaxValue / peak);
        if (gain <= 1f) return 1f;
        for (int i = 0; i < pcm.Length; i++)
            pcm[i] = (short)Math.Clamp((int)Math.Round(pcm[i] * gain), short.MinValue, short.MaxValue);
        return gain;
    }

    public static byte[] Encode(ReadOnlySpan<float> pcm48k)
    {
        using var recorder = new VoiceNoteRecorder();
        for (int offset = 0; offset + PcmFrameCutter.FrameSamples <= pcm48k.Length; offset += PcmFrameCutter.FrameSamples)
            recorder.Write(pcm48k.Slice(offset, PcmFrameCutter.FrameSamples));
        return recorder.Finish();
    }

    public static short[] Decode(byte[] file)
    {
        OpusCodecFactory.AttemptToUseNativeLibrary = false;
        var decoder = OpusCodecFactory.CreateDecoder(PcmFrameCutter.SampleRate, 1);
        using var stream = new MemoryStream(file);
        var ogg = new OpusOggReadStream(decoder, stream);
        var pcm = new List<short>(PcmFrameCutter.SampleRate * VoiceNoteRules.MaxSeconds);
        while (ogg.HasNextPacket)
        {
            short[] packet = ogg.DecodeNextPacket();
            if (packet != null) pcm.AddRange(packet);
        }
        return pcm.ToArray();
    }
}
