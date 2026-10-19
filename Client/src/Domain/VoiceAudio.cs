using System;

namespace LibreKO.Domain;

public enum JitterOutput
{
    Wait,
    Frame,
    Lost,
}

public sealed class VoiceJitterBuffer
{
    public const int TargetFrames = 3;
    public const int Capacity = 32;
    public const int MaxConcealed = 5;

    private readonly byte[]?[] frames = new byte[Capacity][];
    private readonly ushort[] seqs = new ushort[Capacity];
    private ushort next;
    private bool started;
    private bool playing;
    private int concealed;

    public bool Playing => playing;

    public int Buffered
    {
        get
        {
            int count = 0;
            for (int i = 0; i < Capacity; i++)
                if (frames[i] != null && Ahead(seqs[i]) is >= 0 and < Capacity) count++;
            return count;
        }
    }

    public void Push(ushort seq, byte[] frame)
    {
        if (!started)
        {
            next = seq;
            started = true;
        }
        int ahead = Ahead(seq);
        if (ahead < 0)
        {
            if (playing || ahead <= -Capacity) return;
            next = seq;
        }
        else if (ahead >= Capacity)
        {
            Reset();
            next = seq;
            started = true;
        }
        int slot = seq % Capacity;
        frames[slot] = frame;
        seqs[slot] = seq;
    }

    public JitterOutput Pop(out byte[]? frame)
    {
        frame = null;
        if (!started) return JitterOutput.Wait;
        if (!playing)
        {
            if (Buffered < TargetFrames) return JitterOutput.Wait;
            playing = true;
        }
        if (Take(next, out frame))
        {
            next++;
            concealed = 0;
            return JitterOutput.Frame;
        }
        if (Buffered > 0)
        {
            if (concealed < MaxConcealed)
            {
                next++;
                concealed++;
                Peek(next, out frame);
                return JitterOutput.Lost;
            }
            next = Earliest();
            concealed = 0;
            if (Take(next, out frame))
            {
                next++;
                return JitterOutput.Frame;
            }
        }
        playing = false;
        if (Buffered == 0) started = false;
        return JitterOutput.Wait;
    }

    public void Reset()
    {
        Array.Clear(frames);
        started = false;
        playing = false;
        concealed = 0;
    }

    private int Ahead(ushort seq) => (short)(ushort)(seq - next);

    private ushort Earliest()
    {
        int best = Capacity;
        for (int i = 0; i < Capacity; i++)
            if (frames[i] != null && Ahead(seqs[i]) is >= 0 and var ahead && ahead < best) best = ahead;
        return (ushort)(next + (best == Capacity ? 0 : best));
    }

    private bool Take(ushort seq, out byte[]? frame)
    {
        int slot = seq % Capacity;
        frame = frames[slot];
        if (frame == null || seqs[slot] != seq) { frame = null; return false; }
        frames[slot] = null;
        return true;
    }

    private void Peek(ushort seq, out byte[]? frame)
    {
        int slot = seq % Capacity;
        frame = frames[slot] != null && seqs[slot] == seq ? frames[slot] : null;
    }
}

public static class VoiceLevels
{
    public const float MaxVolume = 2f;
    public const float DefaultVolume = 1f;
    public const float LimiterCeilingDb = -0.3f;
    private const float UnbalancedRatio = 1f / 16f;

    public static void ToMono(ReadOnlySpan<float> left, ReadOnlySpan<float> right, Span<float> mono)
    {
        double leftEnergy = 0, rightEnergy = 0;
        for (int i = 0; i < left.Length; i++)
        {
            leftEnergy += left[i] * left[i];
            rightEnergy += right[i] * right[i];
        }
        bool leftOnly = rightEnergy < leftEnergy * UnbalancedRatio;
        bool rightOnly = leftEnergy < rightEnergy * UnbalancedRatio;
        for (int i = 0; i < left.Length; i++)
            mono[i] = leftOnly ? left[i] : rightOnly ? right[i] : (left[i] + right[i]) * 0.5f;
    }
}

public sealed class VoiceAutoGain
{
    public const float TargetRms = 0.2f;
    public const float MaxGain = 16f;
    public const float MinGain = 0.5f;
    public const float Knee = 0.75f;
    public const float Ceiling = 0.98f;
    public const float PeakLimit = 1.4f;
    private const float RiseRate = 0.05f;
    private const float FallRate = 0.4f;
    private const float QuietRms = 1e-4f;

    private float applied = 1f;

    public float Gain { get; private set; } = 1f;

    public void Process(Span<float> frame, bool speech)
    {
        if (frame.IsEmpty) return;
        float sum = 0f, peak = 0f;
        foreach (float sample in frame)
        {
            sum += sample * sample;
            peak = Math.Max(peak, Math.Abs(sample));
        }
        float rms = MathF.Sqrt(sum / frame.Length);
        if (speech && rms > QuietRms)
        {
            float wanted = Math.Clamp(TargetRms / rms, MinGain, MaxGain);
            Gain += (wanted - Gain) * (wanted < Gain ? FallRate : RiseRate);
        }
        float target = peak * Gain > PeakLimit ? PeakLimit / peak : Gain;
        float from = Math.Min(applied, target);
        for (int i = 0; i < frame.Length; i++)
        {
            float gain = from + (target - from) * (i + 1) / frame.Length;
            frame[i] = SoftLimit(frame[i] * gain);
        }
        applied = target;
    }

    public static float SoftLimit(float sample)
    {
        float size = Math.Abs(sample);
        if (size <= Knee) return sample;
        float limited = Knee + (Ceiling - Knee) * MathF.Tanh((size - Knee) / (Ceiling - Knee));
        return MathF.CopySign(limited, sample);
    }

    public void Reset()
    {
        Gain = 1f;
        applied = 1f;
    }
}

public sealed class VoiceActivityGate
{
    public const float MinDb = -60f;
    public const float MaxDb = -15f;
    public const int HangoverFrames = 15;
    private const double Floor = 1e-6;

    private int hang;

    public float LevelDb { get; private set; } = MinDb;

    public float Level => Position(LevelDb);

    public static float ThresholdDb(float threshold) => MinDb + (MaxDb - MinDb) * Math.Clamp(threshold, 0f, 1f);

    public static float Position(float db) => Math.Clamp((db - MinDb) / (MaxDb - MinDb), 0f, 1f);

    public bool Above(float threshold) => LevelDb >= ThresholdDb(threshold);

    public bool Process(ReadOnlySpan<float> frame, float threshold)
    {
        double sum = 0;
        foreach (float sample in frame) sum += sample * sample;
        double rms = frame.Length == 0 ? 0 : Math.Sqrt(sum / frame.Length);
        LevelDb = Math.Max(MinDb, (float)(20 * Math.Log10(Math.Max(rms, Floor))));
        if (LevelDb >= ThresholdDb(threshold))
        {
            hang = HangoverFrames;
            return true;
        }
        if (hang > 0)
        {
            hang--;
            return true;
        }
        return false;
    }

    public void Reset()
    {
        hang = 0;
        LevelDb = MinDb;
    }
}

public sealed class PcmFrameCutter
{
    public const int SampleRate = 48000;
    public const int FrameSamples = 960;
    public const int FrameMs = 20;
    private const int MaxPendingFrames = 10;

    private readonly float[] pending = new float[FrameSamples * MaxPendingFrames];
    private int count;

    public int Pending => count;

    public void Push(ReadOnlySpan<float> mono)
    {
        if (mono.Length >= pending.Length)
        {
            mono[^pending.Length..].CopyTo(pending);
            count = pending.Length;
            return;
        }
        int overflow = count + mono.Length - pending.Length;
        if (overflow > 0)
        {
            Array.Copy(pending, overflow, pending, 0, count - overflow);
            count -= overflow;
        }
        mono.CopyTo(pending.AsSpan(count));
        count += mono.Length;
    }

    public bool TryTake(Span<float> frame)
    {
        if (count < FrameSamples || frame.Length < FrameSamples) return false;
        pending.AsSpan(0, FrameSamples).CopyTo(frame);
        count -= FrameSamples;
        Array.Copy(pending, FrameSamples, pending, 0, count);
        return true;
    }

    public void Clear() => count = 0;
}

public sealed class LinearResampler(int fromRate, int toRate)
{
    private readonly double step = fromRate / (double)toRate;
    private double position;
    private float last;

    public bool PassThrough => fromRate == toRate;

    public int MaxOutput(int inputLength) => (int)Math.Ceiling((inputLength + 1) / step) + 1;

    public int Process(ReadOnlySpan<float> input, Span<float> output)
    {
        if (input.Length == 0) return 0;
        if (PassThrough)
        {
            int copy = Math.Min(input.Length, output.Length);
            input[..copy].CopyTo(output);
            return copy;
        }
        int written = 0;
        while (written < output.Length)
        {
            int index = (int)Math.Floor(position);
            if (index + 1 >= input.Length) break;
            float a = index < 0 ? last : input[index];
            float b = input[index + 1];
            output[written++] = a + (b - a) * (float)(position - index);
            position += step;
        }
        position -= input.Length;
        last = input[^1];
        return written;
    }
}
