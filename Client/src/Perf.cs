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

    private static readonly long[] ticks = new long[(int)Section.Count];
    private static readonly int[] calls = new int[(int)Section.Count];
    private static readonly double[] milliseconds = new double[(int)Section.Count];
    private static readonly double[] callsPerFrame = new double[(int)Section.Count];
    private static readonly int[] counts = new int[(int)Counter.Count];
    private static readonly int[] totals = new int[(int)Counter.Count];
    private static readonly long[] frameTicks = new long[(int)Section.Count];
    private static readonly int[] frameCalls = new int[(int)Section.Count];
    private static readonly int[] perSecond = new int[(int)Counter.Count];
    private static ulong lastFrame;
    private static int lastGc0;
    private static long lastReportAt = Stopwatch.GetTimestamp();

    public readonly struct Scope : IDisposable
    {
        private readonly Section section;
        private readonly long start;

        public Scope(Section section)
        {
            this.section = section;
            start = Stopwatch.GetTimestamp();
            calls[(int)section]++;
            frameCalls[(int)section]++;
        }

        public void Dispose()
        {
            long elapsed = Stopwatch.GetTimestamp() - start;
            ticks[(int)section] += elapsed;
            frameTicks[(int)section] += elapsed;
        }
    }

    public static Scope Measure(Section section) => new(section);

    public static void Tally(Counter counter)
    {
        counts[(int)counter]++;
        totals[(int)counter]++;
    }

    public static int Total(Counter counter) => totals[(int)counter];

    public static double FrameMs(Section section) => frameTicks[(int)section] * 1000.0 / Stopwatch.Frequency;

    public static int FrameCalls(Section section) => frameCalls[(int)section];

    public static void ClearFrame()
    {
        Array.Clear(frameTicks);
        Array.Clear(frameCalls);
    }

    public static double Ms(Section section) => milliseconds[(int)section];

    public static double Calls(Section section) => callsPerFrame[(int)section];

    public static int PerSecond(Counter counter) => perSecond[(int)counter];

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
    private static int fxVisibleAccum;
    private static ulong fxVisibleFrame = ulong.MaxValue;

    public static void CountFxVisible()
    {
        ulong frame = Engine.GetProcessFrames();
        if (frame != fxVisibleFrame)
        {
            FxVisible = fxVisibleAccum;
            fxVisibleAccum = 0;
            fxVisibleFrame = frame;
        }
        fxVisibleAccum++;
    }

    private const int FrameWindow = 2400;
    private static readonly double[] frameTimes = new double[FrameWindow];
    private static readonly double[] frameSorted = new double[FrameWindow];
    private static readonly int[] closeRing = new int[FrameWindow];
    private static readonly int[] fxVisibleRing = new int[FrameWindow];
    private static int frameHead, frameCount;
    private static readonly double[] BucketEdgesMs = { 16.7, 33.3, 66.7, 133.3, 250.0 };
    private const int SparkFrames = 60;
    private const string SparkGlyphs = "▁▂▃▄▅▆▇█";

    public static void FrameTick(double delta)
    {
        frameTimes[frameHead] = delta;
        closeRing[frameHead] = CloseEntities;
        fxVisibleRing[frameHead] = FxVisible;
        frameHead = (frameHead + 1) % FrameWindow;
        if (frameCount < FrameWindow) frameCount++;
    }

    public static double CloseEntitiesAverage(double windowSeconds) => WindowAverage(closeRing, windowSeconds);
    public static double FxVisibleAverage(double windowSeconds) => WindowAverage(fxVisibleRing, windowSeconds);

    private static double WindowAverage(int[] ring, double windowSeconds)
    {
        int n = 0;
        double total = 0, sum = 0;
        for (int i = 0; i < frameCount && total < windowSeconds; i++)
        {
            int slot = (frameHead - 1 - i + FrameWindow) % FrameWindow;
            total += frameTimes[slot];
            sum += ring[slot];
            n++;
        }
        return n > 0 ? sum / n : 0;
    }

    public static int CopyRecentFrames(float[] dest)
    {
        int n = Math.Min(dest.Length, frameCount);
        for (int i = 0; i < n; i++)
            dest[n - 1 - i] = (float)(frameTimes[(frameHead - 1 - i + FrameWindow) % FrameWindow] * 1000.0);
        return n;
    }

    public static string FrameSummary(double windowSeconds, out string histogram, out string spark)
    {
        histogram = spark = "";
        if (frameCount == 0) return "";
        int n = 0;
        double total = 0;
        for (int i = 0; i < frameCount && total < windowSeconds; i++)
        {
            double t = frameTimes[(frameHead - 1 - i + FrameWindow) % FrameWindow];
            frameSorted[n++] = t;
            total += t;
        }
        Array.Sort(frameSorted, 0, n);
        double p50 = frameSorted[n / 2] * 1000.0;
        double p95 = frameSorted[Math.Min(n - 1, (int)(n * 0.95))] * 1000.0;
        double worst = frameSorted[n - 1] * 1000.0;
        double avgFps = total > 0 ? n / total : 0;

        var counts = new int[BucketEdgesMs.Length + 1];
        for (int i = 0; i < n; i++)
        {
            double ms = frameSorted[i] * 1000.0;
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
        int shown = Math.Min(SparkFrames, frameCount);
        for (int i = shown - 1; i >= 0; i--)
        {
            double ms = frameTimes[(frameHead - 1 - i + FrameWindow) % FrameWindow] * 1000.0;
            int glyph = worst > 0 ? (int)Math.Round(ms / worst * (SparkGlyphs.Length - 1)) : 0;
            line.Append(SparkGlyphs[Math.Clamp(glyph, 0, SparkGlyphs.Length - 1)]);
        }
        spark = line.ToString();
        return $"{windowSeconds:0}s avg {avgFps:0.0} fps   p50 {p50:0} ms   p95 {p95:0} ms   worst {worst:0} ms   frames {n}";
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private static readonly Dictionary<int, TimeSpan> threadCpu = new();
    private static uint mainThreadId;
    private static readonly List<(int Id, double Percent)> busy = new();

    public static string ThreadLoad()
    {
        if (busy.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var (id, percent) in busy)
            sb.Append(sb.Length > 0 ? "  " : "").Append(id == (int)mainThreadId ? "main" : $"t{id}").Append(' ')
              .Append(percent.ToString("0")).Append('%');
        return sb.ToString();
    }

    private static void SampleThreads(double seconds)
    {
        if (mainThreadId == 0) mainThreadId = GetCurrentThreadId();
        busy.Clear();
        if (seconds <= 0) return;
        foreach (ProcessThread thread in Process.GetCurrentProcess().Threads)
        {
            TimeSpan total;
            try { total = thread.TotalProcessorTime; }
            catch (Exception) { continue; }
            if (threadCpu.TryGetValue(thread.Id, out var previous))
            {
                double percent = (total - previous).TotalSeconds / seconds * 100.0;
                if (percent >= 5.0) busy.Add((thread.Id, percent));
            }
            threadCpu[thread.Id] = total;
        }
        busy.Sort((a, b) => b.Percent.CompareTo(a.Percent));
        if (busy.Count > 4) busy.RemoveRange(4, busy.Count - 4);
    }

    public static double PendingMs(Section section) => ticks[(int)section] * 1000.0 / Stopwatch.Frequency;

    public static void Report()
    {
        ulong frame = Engine.GetProcessFrames();
        ulong frames = Math.Max(1, frame - lastFrame);
        lastFrame = frame;
        double perFrameMs = 1000.0 / Stopwatch.Frequency / frames;
        for (int i = 0; i < ticks.Length; i++)
        {
            milliseconds[i] = ticks[i] * perFrameMs;
            callsPerFrame[i] = calls[i] / (double)frames;
            ticks[i] = 0;
            calls[i] = 0;
        }
        long now = Stopwatch.GetTimestamp();
        double seconds = (now - lastReportAt) / (double)Stopwatch.Frequency;
        lastReportAt = now;
        for (int i = 0; i < counts.Length; i++)
        {
            perSecond[i] = seconds > 0 ? (int)Math.Round(counts[i] / seconds) : 0;
            counts[i] = 0;
        }
        int gc0 = GC.CollectionCount(0);
        Gc0PerSecond = seconds > 0 ? (int)Math.Round((gc0 - lastGc0) / seconds) : 0;
        lastGc0 = gc0;
        SampleThreads(seconds);
    }
}
