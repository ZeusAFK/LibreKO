using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace LibreKO;

public static class Perf
{
    public enum Section
    {
        Net, Spawn, Entities, SelfAnim, Combat, Selection, Camera, Hud, Sky, MapFx, World,
        Cape, FxRoot, FxBoard, FxMesh, FxPart, FxBatch, Shine, Gear, Flinch, Ui,
        FxSpawn, SpawnBoard, SpawnMesh, SpawnParticles, SpawnSequence, SpawnOpacity,
        FxLoadDescriptor, FxLoadShape, FxBuildShape, FxLoadTexture,
        BuildScene, BuildGraft, BuildGear, BuildRest,
        EntMove, EntFacing, EntCollider, EntLod, EntAudio, EntPlates, EntStep, EntWings,
        StepSample, StepPose, StepSeek,
        BuildInstance, BuildMeta, BuildShare, BuildEnter, Count,
    }

    public static double FxMs => Ms(Section.FxRoot) + Ms(Section.FxBoard) + Ms(Section.FxMesh)
                                 + Ms(Section.FxPart) + Ms(Section.FxBatch);

    public static double FxParts => Calls(Section.FxBoard) + Calls(Section.FxMesh) + Calls(Section.FxPart);

    public enum Counter { FxSpawned, Count }

    private static readonly long[] _ticks = new long[(int)Section.Count];
    private static readonly int[] _calls = new int[(int)Section.Count];
    private static readonly double[] _ms = new double[(int)Section.Count];
    private static readonly double[] _callsPerFrame = new double[(int)Section.Count];
    private static readonly int[] _counts = new int[(int)Counter.Count];
    private static readonly int[] _totals = new int[(int)Counter.Count];
    private static readonly long[] _frameTicks = new long[(int)Section.Count];
    private static readonly int[] _frameCalls = new int[(int)Section.Count];
    private static readonly int[] _perSecond = new int[(int)Counter.Count];
    private static ulong _lastFrame;
    private static int _lastGc0;
    private static long _lastReportAt = Stopwatch.GetTimestamp();

    public readonly struct Scope : IDisposable
    {
        private readonly Section _section;
        private readonly long _start;

        public Scope(Section section)
        {
            _section = section;
            _start = Stopwatch.GetTimestamp();
            _calls[(int)section]++;
            _frameCalls[(int)section]++;
        }

        public void Dispose()
        {
            long elapsed = Stopwatch.GetTimestamp() - _start;
            _ticks[(int)_section] += elapsed;
            _frameTicks[(int)_section] += elapsed;
        }
    }

    public static Scope Measure(Section section) => new(section);

    public static void Tally(Counter counter)
    {
        _counts[(int)counter]++;
        _totals[(int)counter]++;
    }

    public static int Total(Counter counter) => _totals[(int)counter];

    public static double FrameMs(Section section) => _frameTicks[(int)section] * 1000.0 / Stopwatch.Frequency;

    public static int FrameCalls(Section section) => _frameCalls[(int)section];

    public static void ClearFrame()
    {
        Array.Clear(_frameTicks);
        Array.Clear(_frameCalls);
    }

    public static double Ms(Section section) => _ms[(int)section];

    public static double Calls(Section section) => _callsPerFrame[(int)section];

    public static int PerSecond(Counter counter) => _perSecond[(int)counter];

    public static int Gc0PerSecond { get; private set; }

    public static (int Total, int Full, int Mid, int Far, int Off) EntityTiers { get; set; }
    public const float CloseEntityDist = 30f;
    public static int CloseEntities { get; set; }
    public static bool SkipPoses, SkipMove, SkipPlates, SkipEmitters, SkipAudio, SkipFx, SkipLamps, SkipShadows, SkipSsao;

    public static string BisectLine() =>
        $"bisect  poses {(SkipPoses ? "OFF" : "on")}   move {(SkipMove ? "OFF" : "on")}   plates {(SkipPlates ? "OFF" : "on")}"
        + $"   emitters {(SkipEmitters ? "OFF" : "on")}   audio {(SkipAudio ? "OFF" : "on")}   fx {(SkipFx ? "OFF" : "on")}"
        + $"   lamps {(SkipLamps ? "OFF" : "on")} (lit {FxLampLight.Lit})";
    public static int FxVisible { get; private set; }
    private static int _fxVisibleAccum;
    private static ulong _fxVisibleFrame = ulong.MaxValue;

    public static void CountFxVisible()
    {
        ulong frame = Engine.GetProcessFrames();
        if (frame != _fxVisibleFrame)
        {
            FxVisible = _fxVisibleAccum;
            _fxVisibleAccum = 0;
            _fxVisibleFrame = frame;
        }
        _fxVisibleAccum++;
    }

    private const int FrameWindow = 2400;
    private static readonly double[] _frameTimes = new double[FrameWindow];
    private static readonly double[] _frameSorted = new double[FrameWindow];
    private static readonly int[] _closeRing = new int[FrameWindow];
    private static readonly int[] _fxVisibleRing = new int[FrameWindow];
    private static int _frameHead, _frameCount;
    private static readonly double[] BucketEdgesMs = { 16.7, 33.3, 66.7, 133.3, 250.0 };
    private const int SparkFrames = 60;
    private const string SparkGlyphs = "▁▂▃▄▅▆▇█";

    public static void FrameTick(double delta)
    {
        _frameTimes[_frameHead] = delta;
        _closeRing[_frameHead] = CloseEntities;
        _fxVisibleRing[_frameHead] = FxVisible;
        _frameHead = (_frameHead + 1) % FrameWindow;
        if (_frameCount < FrameWindow) _frameCount++;
    }

    public static double CloseEntitiesAverage(double windowSeconds) => WindowAverage(_closeRing, windowSeconds);
    public static double FxVisibleAverage(double windowSeconds) => WindowAverage(_fxVisibleRing, windowSeconds);

    private static double WindowAverage(int[] ring, double windowSeconds)
    {
        int n = 0;
        double total = 0, sum = 0;
        for (int i = 0; i < _frameCount && total < windowSeconds; i++)
        {
            int slot = (_frameHead - 1 - i + FrameWindow) % FrameWindow;
            total += _frameTimes[slot];
            sum += ring[slot];
            n++;
        }
        return n > 0 ? sum / n : 0;
    }

    public static int CopyRecentFrames(float[] dest)
    {
        int n = Math.Min(dest.Length, _frameCount);
        for (int i = 0; i < n; i++)
            dest[n - 1 - i] = (float)(_frameTimes[(_frameHead - 1 - i + FrameWindow) % FrameWindow] * 1000.0);
        return n;
    }

    public static string FrameSummary(double windowSeconds, out string histogram, out string spark)
    {
        histogram = spark = "";
        if (_frameCount == 0) return "";
        int n = 0;
        double total = 0;
        for (int i = 0; i < _frameCount && total < windowSeconds; i++)
        {
            double t = _frameTimes[(_frameHead - 1 - i + FrameWindow) % FrameWindow];
            _frameSorted[n++] = t;
            total += t;
        }
        Array.Sort(_frameSorted, 0, n);
        double p50 = _frameSorted[n / 2] * 1000.0;
        double p95 = _frameSorted[Math.Min(n - 1, (int)(n * 0.95))] * 1000.0;
        double worst = _frameSorted[n - 1] * 1000.0;
        double avgFps = total > 0 ? n / total : 0;

        var counts = new int[BucketEdgesMs.Length + 1];
        for (int i = 0; i < n; i++)
        {
            double ms = _frameSorted[i] * 1000.0;
            int b = 0;
            while (b < BucketEdgesMs.Length && ms > BucketEdgesMs[b]) b++;
            counts[b]++;
        }
        var sb = new System.Text.StringBuilder("frame ms  ");
        for (int b = 0; b < counts.Length; b++)
        {
            string label = b == 0 ? $"<{BucketEdgesMs[0]:0}" : b == BucketEdgesMs.Length ? $">{BucketEdgesMs[^1]:0}"
                : $"{BucketEdgesMs[b - 1]:0}-{BucketEdgesMs[b]:0}";
            sb.Append(label).Append(' ').Append((100.0 * counts[b] / n).ToString("0")).Append("%   ");
        }
        histogram = sb.ToString().TrimEnd();

        var line = new System.Text.StringBuilder();
        int shown = Math.Min(SparkFrames, _frameCount);
        for (int i = shown - 1; i >= 0; i--)
        {
            double ms = _frameTimes[(_frameHead - 1 - i + FrameWindow) % FrameWindow] * 1000.0;
            int glyph = worst > 0 ? (int)Math.Round(ms / worst * (SparkGlyphs.Length - 1)) : 0;
            line.Append(SparkGlyphs[Math.Clamp(glyph, 0, SparkGlyphs.Length - 1)]);
        }
        spark = line.ToString();
        return $"{windowSeconds:0}s avg {avgFps:0.0} fps   p50 {p50:0} ms   p95 {p95:0} ms   worst {worst:0} ms   frames {n}";
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private static readonly Dictionary<int, TimeSpan> _threadCpu = new();
    private static uint _mainThreadId;
    private static readonly List<(int Id, double Percent)> _busy = new();

    public static string ThreadLoad()
    {
        if (_busy.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var (id, percent) in _busy)
            sb.Append(sb.Length > 0 ? "  " : "").Append(id == (int)_mainThreadId ? "main" : $"t{id}").Append(' ')
              .Append(percent.ToString("0")).Append('%');
        return sb.ToString();
    }

    private static void SampleThreads(double seconds)
    {
        if (_mainThreadId == 0) _mainThreadId = GetCurrentThreadId();
        _busy.Clear();
        if (seconds <= 0) return;
        foreach (ProcessThread thread in Process.GetCurrentProcess().Threads)
        {
            TimeSpan total;
            try { total = thread.TotalProcessorTime; }
            catch (Exception) { continue; }
            if (_threadCpu.TryGetValue(thread.Id, out var previous))
            {
                double percent = (total - previous).TotalSeconds / seconds * 100.0;
                if (percent >= 5.0) _busy.Add((thread.Id, percent));
            }
            _threadCpu[thread.Id] = total;
        }
        _busy.Sort((a, b) => b.Percent.CompareTo(a.Percent));
        if (_busy.Count > 4) _busy.RemoveRange(4, _busy.Count - 4);
    }

    public static double PendingMs(Section section) => _ticks[(int)section] * 1000.0 / Stopwatch.Frequency;

    public static void Report()
    {
        ulong frame = Engine.GetProcessFrames();
        ulong frames = Math.Max(1, frame - _lastFrame);
        _lastFrame = frame;
        double perFrameMs = 1000.0 / Stopwatch.Frequency / frames;
        for (int i = 0; i < _ticks.Length; i++)
        {
            _ms[i] = _ticks[i] * perFrameMs;
            _callsPerFrame[i] = _calls[i] / (double)frames;
            _ticks[i] = 0;
            _calls[i] = 0;
        }
        long now = Stopwatch.GetTimestamp();
        double seconds = (now - _lastReportAt) / (double)Stopwatch.Frequency;
        _lastReportAt = now;
        for (int i = 0; i < _counts.Length; i++)
        {
            _perSecond[i] = seconds > 0 ? (int)Math.Round(_counts[i] / seconds) : 0;
            _counts[i] = 0;
        }
        int gc0 = GC.CollectionCount(0);
        Gc0PerSecond = seconds > 0 ? (int)Math.Round((gc0 - _lastGc0) / seconds) : 0;
        _lastGc0 = gc0;
        SampleThreads(seconds);
    }
}
