using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace LibreKO;

internal static class EngineProfile
{
    private const string VisualProfiler = "visual";
    private const int TopPasses = 10;
    private const double MicrosPerMs = 1000.0;
    private const double NanosPerMs = 1_000_000.0;
    private const float Smoothing = 0.1f;

    private static readonly Dictionary<string, (double Cpu, double Gpu)> _smoothed = new();

    internal static bool Enabled { get; private set; }

    internal static bool Available => EngineDebugger.IsActive();

    internal static void Toggle()
    {
        if (!Available) return;
        Enabled = !Enabled;
        EngineDebugger.ProfilerEnable(VisualProfiler, Enabled);
        _smoothed.Clear();
    }

    internal static void Sample()
    {
        if (!Enabled || RenderingServer.GetRenderingDevice() is not { } rd) return;
        uint count = rd.GetCapturedTimestampsCount();
        if (count < 2) return;
        var frame = new Dictionary<string, (double Cpu, double Gpu)>();
        for (uint i = 0; i + 1 < count; i++)
        {
            string name = rd.GetCapturedTimestampName(i);
            double cpu = (rd.GetCapturedTimestampCpuTime(i + 1) - rd.GetCapturedTimestampCpuTime(i)) / MicrosPerMs;
            double gpu = (rd.GetCapturedTimestampGpuTime(i + 1) - rd.GetCapturedTimestampGpuTime(i)) / NanosPerMs;
            frame[name] = frame.TryGetValue(name, out var t) ? (t.Cpu + cpu, t.Gpu + gpu) : (cpu, gpu);
        }
        foreach (var (name, t) in frame)
            _smoothed[name] = _smoothed.TryGetValue(name, out var s)
                ? (s.Cpu + (t.Cpu - s.Cpu) * Smoothing, s.Gpu + (t.Gpu - s.Gpu) * Smoothing)
                : t;
    }

    internal static string Lines()
    {
        if (!Available) return "engine passes: start with --remote-debug to enable";
        if (!Enabled) return "engine passes: off (Ctrl+Shift+F11)";
        var sb = new StringBuilder("engine passes cpu:");
        foreach (var (name, t) in _smoothed.OrderByDescending(kv => kv.Value.Cpu).Take(TopPasses))
            sb.Append($"  {name} {t.Cpu:0.00}");
        sb.Append("\nengine passes gpu:");
        foreach (var (name, t) in _smoothed.OrderByDescending(kv => kv.Value.Gpu).Take(TopPasses))
            sb.Append($"  {name} {t.Gpu:0.00}");
        return sb.ToString();
    }

    internal static string Report()
    {
        var sb = new StringBuilder("pass                                        cpu ms    gpu ms\n");
        foreach (var (name, t) in _smoothed.OrderByDescending(kv => kv.Value.Cpu))
            sb.AppendLine($"{name,-42} {t.Cpu,8:0.000} {t.Gpu,9:0.000}");
        return sb.ToString();
    }
}
