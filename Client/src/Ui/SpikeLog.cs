using System;
using System.Linq;
using System.Text;
using Godot;

namespace LibreKO;

internal static class SpikeLog
{
    private const double MinSpikeMs = 40.0;
    private const double SpikeFactor = 2.5;
    private const double AverageBlend = 0.02;
    private const int TopSections = 6;
    private const int CompileSampleFrames = 15;
    private const string FileName = "spikes.txt";

    private static double _averageMs;
    private static long _compiles = -1;
    private static int _compileAge;
    private static readonly int[] _gc = new int[3];
    private static int _fxSpawned;
    private static bool _started;
    private static string? _file;

    internal static int Count { get; private set; }
    internal static string Last { get; private set; } = "";

    internal static void Frame(double delta)
    {
        double ms = delta * 1000.0;
        int fxSpawned = Perf.Total(Perf.Counter.FxSpawned);
        if (_started && ms >= Math.Max(MinSpikeMs, _averageMs * SpikeFactor)) Record(ms, fxSpawned);
        else if (++_compileAge >= CompileSampleFrames) SampleCompiles();
        _averageMs = _started ? _averageMs + (ms - _averageMs) * AverageBlend : ms;
        for (int g = 0; g < _gc.Length; g++) _gc[g] = GC.CollectionCount(g);
        _fxSpawned = fxSpawned;
        _started = true;
        Perf.ClearFrame();
    }

    internal static string Line() => Count == 0 ? "spikes 0" : $"spikes {Count}   last {Last}";

    private static long PipelineCompiles() =>
        (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsCanvas)
        + (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsMesh)
        + (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsSurface)
        + (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsDraw)
        + (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsSpecialization);

    private static long SampleCompiles()
    {
        long before = _compiles;
        _compiles = PipelineCompiles();
        _compileAge = 0;
        return before < 0 ? 0 : Math.Max(0, _compiles - before);
    }

    private static void Record(double ms, int fxSpawned)
    {
        Count++;
        long compiles = SampleCompiles();
        var top = Enum.GetValues<Perf.Section>()
            .Where(s => s != Perf.Section.Count)
            .Select(s => (Section: s, Ms: Perf.FrameMs(s), Calls: Perf.FrameCalls(s)))
            .Where(x => x.Ms >= 0.5)
            .OrderByDescending(x => x.Ms)
            .Take(TopSections)
            .Select(x => x.Calls > 1 ? $"{x.Section} {x.Ms:0.0} x{x.Calls}" : $"{x.Section} {x.Ms:0.0}");
        var split = FrameProbe.Last;
        string gc = string.Join("/", Enumerable.Range(0, _gc.Length).Select(g => GC.CollectionCount(g) - _gc[g]));
        string sections = string.Join(", ", top);
        Last = $"{ms:0} ms: {(sections.Length > 0 ? sections : "no timed section")}";
        var sb = new StringBuilder();
        sb.Append($"{Time.GetTimeStringFromSystem()}  frame {Engine.GetProcessFrames()}  {ms,6:0.0} ms (avg {_averageMs:0.0})  ");
        sb.Append($"[{sections}]  split {split.Process:0.0}/{split.Flush:0.0}/{split.Draw:0.0}/{split.Other:0.0}  ");
        sb.Append($"pipelines +{compiles}  gc {gc}  fx spawned {fxSpawned - _fxSpawned}  close {Perf.CloseEntities}  effects {Perf.FxVisible}");
        Write(sb.ToString());
    }

    private static void Write(string line)
    {
        if (CaptureFiles.Exported) return;
        try
        {
            if (_file == null)
            {
                _file = CaptureFiles.Path(FileName);
                System.IO.File.AppendAllText(_file, $"=== session {Time.GetDatetimeStringFromSystem()}{System.Environment.NewLine}");
            }
            System.IO.File.AppendAllText(_file, line + System.Environment.NewLine);
        }
        catch (System.IO.IOException) { }
    }
}
