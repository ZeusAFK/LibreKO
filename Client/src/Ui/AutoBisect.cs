using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace LibreKO;

internal sealed class AutoBisect
{
    private const double SettleSeconds = 2.0;
    private const double LongSettleSeconds = 5.0;
    private const double MeasureSeconds = 5.0;
    private const int LabelWidth = 40;
    private const int DescendLayers = 2;
    private const int DescendMaxChildren = 24;
    private const float LowRenderScale = 0.5f;

    private sealed record Step(string Label, System.Action Enter, System.Action Leave, double Settle, Node? Target = null);

    private sealed record Result(string Label, double P50, double Avg, double Close, double Effects,
        double Process, double Flush, double Draw, double Other, double RenderCpu, double Gpu, double Draws);

    private readonly List<Step> _steps = new();
    private readonly List<double> _frames = new();
    private readonly List<Result> _results = new();
    private readonly List<CanvasLayer> _hud = new();
    private int _index = -1;
    private double _clock, _closeSum, _effectsSum, _processSum, _flushSum, _drawSum, _otherSum, _cpuSum, _gpuSum, _drawsSum;
    private Rid _viewport;
    private bool _measuring;
    private bool _descend;
    private string _report = "auto_bisect";

    internal bool Running => _index >= 0;

    internal string Status => Running
        ? $"auto-bisect {_index + 1}/{_steps.Count}: {_steps[_index].Label} ({(_measuring ? "measuring" : "settling")})"
        : "";

    private void Reset(SceneTree tree, string report, bool descend)
    {
        _steps.Clear();
        _results.Clear();
        _hud.Clear();
        _descend = descend;
        _report = report;
        _viewport = tree.Root.GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(_viewport, true);
        if (tree.CurrentScene is { } scene)
            foreach (var child in scene.GetChildren())
                if (child is CanvasLayer { Visible: true } layer) _hud.Add(layer);
    }

    private static void Nothing() { }

    internal void Start(SceneTree tree, System.Action<bool> setDetail)
    {
        Reset(tree, "auto_bisect", false);
        _steps.Add(new("baseline", Nothing, Nothing, SettleSeconds));
        _steps.Add(new("poses off", () => Perf.SkipPoses = true, () => Perf.SkipPoses = false, SettleSeconds));
        _steps.Add(new("movement off", () => Perf.SkipMove = true, () => Perf.SkipMove = false, SettleSeconds));
        _steps.Add(new("plates off", () => Perf.SkipPlates = true, () => Perf.SkipPlates = false, SettleSeconds));
        _steps.Add(new("audio off", () => Perf.SkipAudio = true, () => Perf.SkipAudio = false, SettleSeconds));
        _steps.Add(new("lamps off", () => Perf.SkipLamps = true, () => Perf.SkipLamps = false, SettleSeconds));
        _steps.Add(new("sun shadows off", () => Perf.SkipShadows = true, () => Perf.SkipShadows = false, SettleSeconds));
        _steps.Add(new("ssao + prepass off", () => Perf.SkipSsao = true, () => Perf.SkipSsao = false, SettleSeconds));
        _steps.Add(new("minimap hidden", () => SetMiniMap(tree, false), () => SetMiniMap(tree, true), SettleSeconds));
        _steps.Add(new("hud hidden", () => SetHud(false), () => SetHud(true), SettleSeconds));
        _steps.Add(new("overlay compact", () => setDetail(false), () => setDetail(true), SettleSeconds));
        _steps.Add(new("new emitters off", () => Perf.SkipEmitters = true, () => Perf.SkipEmitters = false, LongSettleSeconds));
        _steps.Add(new("new effects off", () => Perf.SkipFx = true, () => Perf.SkipFx = false, LongSettleSeconds));
        _steps.Add(new("baseline again", Nothing, Nothing, LongSettleSeconds));
        Begin(0);
    }

    internal void StartHudSweep(SceneTree tree)
    {
        Reset(tree, "hud_sweep", true);
        _steps.Add(new("baseline", Nothing, Nothing, SettleSeconds));
        if (tree.CurrentScene is { } scene)
            foreach (var child in scene.GetChildren())
                if (child is CanvasLayer { Visible: true } layer && VisibleItems(layer).Any())
                    _steps.Add(HideStep(layer));
        _steps.Add(new("baseline again", Nothing, Nothing, SettleSeconds));
        Begin(0);
    }

    internal void StartSceneSweep(SceneTree tree)
    {
        Reset(tree, "scene_sweep", true);
        _steps.Add(new("baseline", Nothing, Nothing, SettleSeconds));
        if (tree.CurrentScene is { } scene)
        {
            foreach (var node in VisibleSpatials(scene))
                if (node is not Sky) _steps.Add(HideStep(node));
            if (scene.GetChildren().OfType<Sky>().FirstOrDefault() is { } sky)
                foreach (var node in VisibleSpatials(sky)) _steps.Add(HideStep(node));
        }
        var viewport = tree.Root;
        if (viewport.FindWorld3D()?.Environment is { } env)
        {
            if (env.GlowEnabled) _steps.Add(new("glow off", () => env.GlowEnabled = false, () => env.GlowEnabled = true, SettleSeconds));
            if (env.FogEnabled) _steps.Add(new("fog off", () => env.FogEnabled = false, () => env.FogEnabled = true, SettleSeconds));
            if (env.VolumetricFogEnabled) _steps.Add(new("volumetric fog off", () => env.VolumetricFogEnabled = false, () => env.VolumetricFogEnabled = true, SettleSeconds));
            if (env.SdfgiEnabled) _steps.Add(new("sdfgi off", () => env.SdfgiEnabled = false, () => env.SdfgiEnabled = true, SettleSeconds));
            if (env.SsilEnabled) _steps.Add(new("ssil off", () => env.SsilEnabled = false, () => env.SsilEnabled = true, SettleSeconds));
            if (env.AdjustmentEnabled) _steps.Add(new("adjustments off", () => env.AdjustmentEnabled = false, () => env.AdjustmentEnabled = true, SettleSeconds));
        }
        _steps.Add(new("ssao + prepass off", () => Perf.SkipSsao = true, () => Perf.SkipSsao = false, SettleSeconds));
        _steps.Add(new("sun shadows off", () => Perf.SkipShadows = true, () => Perf.SkipShadows = false, SettleSeconds));
        var msaa = viewport.Msaa3D;
        if (msaa != Viewport.Msaa.Disabled)
            _steps.Add(new("msaa off", () => viewport.Msaa3D = Viewport.Msaa.Disabled, () => viewport.Msaa3D = msaa, SettleSeconds));
        var screenAa = viewport.ScreenSpaceAA;
        if (screenAa != Viewport.ScreenSpaceAAEnum.Disabled)
            _steps.Add(new("screen-space aa off", () => viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled, () => viewport.ScreenSpaceAA = screenAa, SettleSeconds));
        float scale = viewport.Scaling3DScale;
        _steps.Add(new($"3d scale {LowRenderScale:0.00}", () => viewport.Scaling3DScale = LowRenderScale, () => viewport.Scaling3DScale = scale, SettleSeconds));
        _steps.Add(new("hud hidden", () => SetHud(false), () => SetHud(true), SettleSeconds));
        _steps.Add(new("baseline again", Nothing, Nothing, SettleSeconds));
        Begin(0);
    }

    private static IEnumerable<Node3D> VisibleSpatials(Node parent) =>
        parent.GetChildren().OfType<Node3D>().Where(n => n.Visible && n is not Camera3D);

    private static IEnumerable<CanvasItem> VisibleItems(Node parent) =>
        parent.GetChildren().OfType<CanvasItem>().Where(c => c.Visible);

    private static string Describe(Node node)
    {
        string text = node is CanvasLayer layer
            ? $"L{layer.Layer} " + string.Join(",", VisibleItems(layer).Select(c => c.Name.ToString()))
            : node is Node3D spatial
                ? $"{(node.GetParent() is Sky ? "Sky/" : "")}{node.Name} ({CountVisuals(spatial)} visuals)"
                : $"  {node.GetParent()?.Name}/{node.Name} ({node.GetClass()})";
        return text.Length > LabelWidth ? text[..LabelWidth] : text;
    }

    private static int CountVisuals(Node node)
    {
        int count = node is VisualInstance3D { Visible: true } ? 1 : 0;
        foreach (var child in node.GetChildren()) count += CountVisuals(child);
        return count;
    }

    private static Step HideStep(Node node)
    {
        bool was = true;
        void Enter()
        {
            if (!GodotObject.IsInstanceValid(node)) return;
            if (node is CanvasLayer layer) { was = layer.Visible; layer.Visible = false; }
            else if (node is CanvasItem item) { was = item.Visible; item.Visible = false; }
            else if (node is Node3D spatial) { was = spatial.Visible; spatial.Visible = false; }
        }
        void Leave()
        {
            if (!GodotObject.IsInstanceValid(node)) return;
            if (node is CanvasLayer layer) layer.Visible = was;
            else if (node is CanvasItem item) item.Visible = was;
            else if (node is Node3D spatial) spatial.Visible = was;
        }
        return new(Describe(node), Enter, Leave, SettleSeconds, node);
    }

    private double Cost(Result r) => _report == "scene_sweep" ? r.Avg : r.Process + r.Flush;

    private double BaselineCost()
    {
        var baselines = _results.Where(r => r.Label.StartsWith("baseline")).ToList();
        return baselines.Count == 0 ? 0 : baselines.Average(Cost);
    }

    private bool Descend()
    {
        _descend = false;
        double baseline = BaselineCost();
        var worst = _steps.Select((step, i) => (step, drop: baseline - Cost(_results[i])))
            .Where(x => x.step.Target is CanvasLayer or Node3D)
            .OrderByDescending(x => x.drop)
            .Take(DescendLayers)
            .ToList();
        int added = 0;
        foreach (var (step, _) in worst)
        {
            if (step.Target is not { } target || !GodotObject.IsInstanceValid(target)) continue;
            List<Node> targets;
            if (target is Node3D spatial)
                targets = VisibleSpatials(spatial).Cast<Node>().ToList();
            else
            {
                var items = VisibleItems(target).ToList();
                targets = (items.Count == 1 ? VisibleItems(items[0]).ToList() : items).Cast<Node>().ToList();
            }
            if (targets.Count < 2 || targets.Count > DescendMaxChildren) continue;
            foreach (var item in targets) { _steps.Add(HideStep(item)); added++; }
        }
        if (added == 0) return false;
        _steps.Add(new("baseline final", Nothing, Nothing, SettleSeconds));
        return true;
    }

    private static void SetMiniMap(SceneTree tree, bool visible)
    {
        if (tree.CurrentScene is { } scene && Find<MiniMap>(scene) is { } map) map.Visible = visible;
    }

    private static T? Find<T>(Node node) where T : Node
    {
        if (node is T found) return found;
        foreach (var child in node.GetChildren())
            if (Find<T>(child) is { } deeper) return deeper;
        return null;
    }

    private void SetHud(bool visible)
    {
        foreach (var layer in _hud)
            if (GodotObject.IsInstanceValid(layer)) layer.Visible = visible;
    }

    private void Begin(int index)
    {
        _index = index;
        _clock = 0;
        _measuring = false;
        _frames.Clear();
        _closeSum = _effectsSum = _processSum = _flushSum = _drawSum = _otherSum = _cpuSum = _gpuSum = _drawsSum = 0;
        _steps[index].Enter();
    }

    internal void Tick(double delta)
    {
        if (!Running) return;
        _clock += delta;
        var step = _steps[_index];
        if (!_measuring)
        {
            if (_clock < step.Settle) return;
            _measuring = true;
            _clock = 0;
            return;
        }
        _frames.Add(delta * 1000.0);
        _closeSum += Perf.CloseEntities;
        _effectsSum += Perf.FxVisible;
        var split = FrameProbe.Last;
        _processSum += split.Process;
        _flushSum += split.Flush;
        _drawSum += split.Draw;
        _otherSum += split.Other;
        _cpuSum += RenderingServer.ViewportGetMeasuredRenderTimeCpu(_viewport);
        _gpuSum += RenderingServer.ViewportGetMeasuredRenderTimeGpu(_viewport);
        _drawsSum += RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
        if (_clock < MeasureSeconds) return;

        var sorted = _frames.OrderBy(f => f).ToList();
        int n = sorted.Count;
        _results.Add(new(step.Label, sorted[n / 2], sorted.Average(), _closeSum / n, _effectsSum / n,
            _processSum / n, _flushSum / n, _drawSum / n, _otherSum / n, _cpuSum / n, _gpuSum / n, _drawsSum / n));
        step.Leave();
        if (_index + 1 < _steps.Count || (_descend && Descend())) Begin(_index + 1);
        else Finish();
    }

    private void Finish()
    {
        _index = -1;
        double baseline = _results.Where(r => r.Label.StartsWith("baseline")).Average(r => r.Avg);
        var sb = new StringBuilder();
        sb.AppendLine($"auto-bisect frame {Engine.GetProcessFrames()}  baseline avg {baseline:0.0} ms"
                      + $"  (first {_results[0].Avg:0.0}, last {_results[^1].Avg:0.0})");
        sb.AppendLine($"{"step",-LabelWidth}  p50 ms   avg ms   vs baseline   close avg   effects avg   process   flush    draw   other   rcpu    gpu   draws");
        foreach (var r in _results)
            sb.AppendLine($"{r.Label,-LabelWidth} {r.P50,7:0.00} {r.Avg,8:0.00} {r.Avg - baseline,12:+0.00;-0.00;0.00} {r.Close,11:0} {r.Effects,13:0}"
                          + $" {r.Process,9:0.00} {r.Flush,7:0.00} {r.Draw,7:0.00} {r.Other,7:0.00} {r.RenderCpu,6:0.00} {r.Gpu,6:0.00} {r.Draws,7:0}");
        string file = CaptureFiles.Path($"{_report}_{Time.GetDatetimeStringFromSystem().Replace(':', '-')}.txt");
        using (var f = Godot.FileAccess.Open(file, Godot.FileAccess.ModeFlags.Write))
            f?.StoreString(sb.ToString());
        GD.Print($"[{_report}] {file}");
    }
}
