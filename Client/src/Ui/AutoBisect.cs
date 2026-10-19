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

    private readonly List<Step> steps = new();
    private readonly List<double> frames = new();
    private readonly List<Result> results = new();
    private readonly List<CanvasLayer> hud = new();
    private int index = -1;
    private double clock, closeSum, effectsSum, processSum, flushSum, drawSum, otherSum, cpuSum, gpuSum, drawsSum;
    private Rid viewport;
    private bool measuring;
    private bool descend;
    private string report = "auto_bisect";

    internal bool Running => index >= 0;

    internal string Status => Running
        ? $"auto-bisect {index + 1}/{steps.Count}: {steps[index].Label} ({(measuring ? "measuring" : "settling")})"
        : "";

    private void Reset(SceneTree tree, string report, bool descend)
    {
        steps.Clear();
        results.Clear();
        hud.Clear();
        this.descend = descend;
        this.report = report;
        viewport = tree.Root.GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(viewport, true);
        if (tree.CurrentScene is { } scene)
            foreach (var child in scene.GetChildren())
                if (child is CanvasLayer { Visible: true } layer) hud.Add(layer);
    }

    private static void Nothing() { }

    internal void Start(SceneTree tree, System.Action<bool> setDetail)
    {
        Reset(tree, "auto_bisect", false);
        steps.Add(new("baseline", Nothing, Nothing, SettleSeconds));
        steps.Add(new("poses off", () => Perf.SkipPoses = true, () => Perf.SkipPoses = false, SettleSeconds));
        steps.Add(new("movement off", () => Perf.SkipMove = true, () => Perf.SkipMove = false, SettleSeconds));
        steps.Add(new("plates off", () => Perf.SkipPlates = true, () => Perf.SkipPlates = false, SettleSeconds));
        steps.Add(new("audio off", () => Perf.SkipAudio = true, () => Perf.SkipAudio = false, SettleSeconds));
        steps.Add(new("lamps off", () => Perf.SkipLamps = true, () => Perf.SkipLamps = false, SettleSeconds));
        steps.Add(new("sun shadows off", () => Perf.SkipShadows = true, () => Perf.SkipShadows = false, SettleSeconds));
        steps.Add(new("ssao + prepass off", () => Perf.SkipSsao = true, () => Perf.SkipSsao = false, SettleSeconds));
        steps.Add(new("minimap hidden", () => SetMiniMap(tree, false), () => SetMiniMap(tree, true), SettleSeconds));
        steps.Add(new("hud hidden", () => SetHud(false), () => SetHud(true), SettleSeconds));
        steps.Add(new("overlay compact", () => setDetail(false), () => setDetail(true), SettleSeconds));
        steps.Add(new("new emitters off", () => Perf.SkipEmitters = true, () => Perf.SkipEmitters = false, LongSettleSeconds));
        steps.Add(new("new effects off", () => Perf.SkipFx = true, () => Perf.SkipFx = false, LongSettleSeconds));
        steps.Add(new("baseline again", Nothing, Nothing, LongSettleSeconds));
        Begin(0);
    }

    internal void StartHudSweep(SceneTree tree)
    {
        Reset(tree, "hud_sweep", true);
        steps.Add(new("baseline", Nothing, Nothing, SettleSeconds));
        if (tree.CurrentScene is { } scene)
            foreach (var child in scene.GetChildren())
                if (child is CanvasLayer { Visible: true } layer && VisibleItems(layer).Any())
                    steps.Add(HideStep(layer));
        steps.Add(new("baseline again", Nothing, Nothing, SettleSeconds));
        Begin(0);
    }

    internal void StartSceneSweep(SceneTree tree)
    {
        Reset(tree, "scene_sweep", true);
        steps.Add(new("baseline", Nothing, Nothing, SettleSeconds));
        if (tree.CurrentScene is { } scene)
        {
            foreach (var node in VisibleSpatials(scene))
                if (node is not Sky) steps.Add(HideStep(node));
            if (scene.GetChildren().OfType<Sky>().FirstOrDefault() is { } sky)
                foreach (var node in VisibleSpatials(sky)) steps.Add(HideStep(node));
        }
        var viewportWindow = tree.Root;
        if (viewportWindow.FindWorld3D()?.Environment is { } env)
        {
            if (env.GlowEnabled) steps.Add(new("glow off", () => env.GlowEnabled = false, () => env.GlowEnabled = true, SettleSeconds));
            if (env.FogEnabled) steps.Add(new("fog off", () => env.FogEnabled = false, () => env.FogEnabled = true, SettleSeconds));
            if (env.VolumetricFogEnabled) steps.Add(new("volumetric fog off", () => env.VolumetricFogEnabled = false, () => env.VolumetricFogEnabled = true, SettleSeconds));
            if (env.SdfgiEnabled) steps.Add(new("sdfgi off", () => env.SdfgiEnabled = false, () => env.SdfgiEnabled = true, SettleSeconds));
            if (env.SsilEnabled) steps.Add(new("ssil off", () => env.SsilEnabled = false, () => env.SsilEnabled = true, SettleSeconds));
            if (env.AdjustmentEnabled) steps.Add(new("adjustments off", () => env.AdjustmentEnabled = false, () => env.AdjustmentEnabled = true, SettleSeconds));
        }
        steps.Add(new("ssao + prepass off", () => Perf.SkipSsao = true, () => Perf.SkipSsao = false, SettleSeconds));
        steps.Add(new("sun shadows off", () => Perf.SkipShadows = true, () => Perf.SkipShadows = false, SettleSeconds));
        var msaa = viewportWindow.Msaa3D;
        if (msaa != Viewport.Msaa.Disabled)
            steps.Add(new("msaa off", () => viewportWindow.Msaa3D = Viewport.Msaa.Disabled, () => viewportWindow.Msaa3D = msaa, SettleSeconds));
        var screenAa = viewportWindow.ScreenSpaceAA;
        if (screenAa != Viewport.ScreenSpaceAAEnum.Disabled)
            steps.Add(new("screen-space aa off", () => viewportWindow.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled, () => viewportWindow.ScreenSpaceAA = screenAa, SettleSeconds));
        float scale = viewportWindow.Scaling3DScale;
        steps.Add(new($"3d scale {LowRenderScale:0.00}", () => viewportWindow.Scaling3DScale = LowRenderScale, () => viewportWindow.Scaling3DScale = scale, SettleSeconds));
        steps.Add(new("hud hidden", () => SetHud(false), () => SetHud(true), SettleSeconds));
        steps.Add(new("baseline again", Nothing, Nothing, SettleSeconds));
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

    private double Cost(Result r) => report == "scene_sweep" ? r.Avg : r.Process + r.Flush;

    private double BaselineCost()
    {
        var baselines = results.Where(r => r.Label.StartsWith("baseline")).ToList();
        return baselines.Count == 0 ? 0 : baselines.Average(Cost);
    }

    private bool Descend()
    {
        descend = false;
        double baseline = BaselineCost();
        var worst = steps.Select((step, i) => (step, drop: baseline - Cost(results[i])))
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
            foreach (var item in targets) { steps.Add(HideStep(item)); added++; }
        }
        if (added == 0) return false;
        steps.Add(new("baseline final", Nothing, Nothing, SettleSeconds));
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
        foreach (var layer in hud)
            if (GodotObject.IsInstanceValid(layer)) layer.Visible = visible;
    }

    private void Begin(int index)
    {
        this.index = index;
        clock = 0;
        measuring = false;
        frames.Clear();
        closeSum = effectsSum = processSum = flushSum = drawSum = otherSum = cpuSum = gpuSum = drawsSum = 0;
        steps[index].Enter();
    }

    internal void Tick(double delta)
    {
        if (!Running) return;
        clock += delta;
        var step = steps[index];
        if (!measuring)
        {
            if (clock < step.Settle) return;
            measuring = true;
            clock = 0;
            return;
        }
        frames.Add(delta * 1000.0);
        closeSum += Perf.CloseEntities;
        effectsSum += Perf.FxVisible;
        var split = FrameProbe.Last;
        processSum += split.Process;
        flushSum += split.Flush;
        drawSum += split.Draw;
        otherSum += split.Other;
        cpuSum += RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport);
        gpuSum += RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport);
        drawsSum += RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
        if (clock < MeasureSeconds) return;

        var sorted = frames.OrderBy(f => f).ToList();
        int n = sorted.Count;
        results.Add(new(step.Label, sorted[n / 2], sorted.Average(), closeSum / n, effectsSum / n,
            processSum / n, flushSum / n, drawSum / n, otherSum / n, cpuSum / n, gpuSum / n, drawsSum / n));
        step.Leave();
        if (index + 1 < steps.Count || (descend && Descend())) Begin(index + 1);
        else Finish();
    }

    private void Finish()
    {
        index = -1;
        double baseline = results.Where(r => r.Label.StartsWith("baseline")).Average(r => r.Avg);
        var sb = new StringBuilder();
        sb.AppendLine($"auto-bisect frame {Engine.GetProcessFrames()}  baseline avg {baseline:0.0} ms"
                      + $"  (first {results[0].Avg:0.0}, last {results[^1].Avg:0.0})");
        sb.AppendLine($"{"step",-LabelWidth}  p50 ms   avg ms   vs baseline   close avg   effects avg   process   flush    draw   other   rcpu    gpu   draws");
        foreach (var r in results)
            sb.AppendLine($"{r.Label,-LabelWidth} {r.P50,7:0.00} {r.Avg,8:0.00} {r.Avg - baseline,12:+0.00;-0.00;0.00} {r.Close,11:0} {r.Effects,13:0}"
                          + $" {r.Process,9:0.00} {r.Flush,7:0.00} {r.Draw,7:0.00} {r.Other,7:0.00} {r.RenderCpu,6:0.00} {r.Gpu,6:0.00} {r.Draws,7:0}");
        string file = CaptureFiles.Path($"{report}_{Time.GetDatetimeStringFromSystem().Replace(':', '-')}.txt");
        using (var f = Godot.FileAccess.Open(file, Godot.FileAccess.ModeFlags.Write))
            f?.StoreString(sb.ToString());
        GD.Print($"[{report}] {file}");
    }
}
