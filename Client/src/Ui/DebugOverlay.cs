using Godot;

namespace LibreKO;

public partial class DebugOverlay : CanvasLayer
{
    private RichTextLabel _label = null!;
    private VBoxContainer _stack = null!;
    private FrameGraph _graph = null!;
    private const float GraphHeight = 96f;
    private double _accum;
    private bool _detail;
    private const float CompactWidth = 320f;
    private const float DetailWidth = 860f;
    private const double FrameWindowSeconds = 10.0;

    private const string Green = "#5cff5c";
    private const string Yellow = "#ffd24a";
    private const string Red = "#ff5555";

    public override void _Ready()
    {
        Layer = 128;
        Visible = false;

        _label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _graph = new FrameGraph
        {
            Visible = false,
            CustomMinimumSize = new Vector2(DetailWidth, GraphHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _stack = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(CompactWidth, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        HudPlacement.Stats(new Vector2(HudAnchor.Edge, HudAnchor.Edge)).ApplyTo(_stack);
        _label.AddThemeFontSizeOverride("normal_font_size", 14);
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", 4);
        _stack.AddChild(_graph);
        _stack.AddChild(_label);
        AddChild(_stack);
    }

    private bool InWorld => GetTree().CurrentScene is World;

    private readonly AutoBisect _auto = new();
    private bool _probed;

    private void SetDetail(bool detail)
    {
        _detail = detail;
        _stack.CustomMinimumSize = new Vector2(_detail ? DetailWidth : CompactWidth, 0f);
        _graph.Visible = _detail;
        _accum = 1.0;
    }

    public override void _Process(double delta)
    {
        Perf.FrameTick(delta);
        if (!_probed) { _probed = true; Callable.From(() => FrameProbe.Install(GetTree().Root)).CallDeferred(); }
        _auto.Tick(delta);
        EngineProfile.Sample();
        bool show = InWorld && Config.ShowStats;
        if (Visible != show) Visible = show;
        FrameProbe.Detailed = show && _detail;
        if (!show) return;

        _accum += delta;
        if (_accum < 0.25)
            return;
        _accum = 0;

        double fps = Engine.GetFramesPerSecond();
        double avgMs = fps > 0 ? 1000.0 / fps : 0;

        string c = FpsColor(fps);
        string text = $"[color={c}]FPS {fps:0}[/color]   [color={c}]{avgMs:0} ms[/color]{Ping()}";
        if (_auto.Running) text += $"\n[color={Yellow}]{_auto.Status}[/color]";
        if (_detail)
            text += "\n" + Breakdown();
        _label.Text = $"[right]{text}[/right]";
    }

    private static string Ping()
    {
        if (Net.I is not { Connected: true } net)
            return "";
        int ms = net.PingMs;
        return ms < 0
            ? $"   [color={Red}]PING —[/color]"
            : $"   [color={PingColor(ms)}]PING {ms} ms[/color]";
    }

    private static string PingColor(int ms)
        => ms < 90 ? Green : ms < 180 ? Yellow : Red;

    private static string Breakdown()
    {
        double scriptMs = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        double physMs = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
        ulong draws = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
        ulong objects = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame);
        ulong prims = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
        ulong vram = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed);
        double nodes = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        double orphans = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);

        const string dim = "#b9c4d4";
        Perf.Report();
        double tracked = Perf.Ms(Perf.Section.World) + Perf.Ms(Perf.Section.Net)
                         + Perf.Ms(Perf.Section.Cape) + Perf.FxMs
                         + Perf.Ms(Perf.Section.Shine) + Perf.Ms(Perf.Section.Gear)
                         + Perf.Ms(Perf.Section.Flinch) + Perf.Ms(Perf.Section.Ui);
        string frames = Perf.FrameSummary(FrameWindowSeconds, out string histogram, out _);
        return
            $"[color={dim}]{frames}[/color]\n" +
            $"[color={dim}]{histogram}[/color]\n" +
            $"[color={dim}]{FrameProbe.Line()}[/color]\n" +
            $"[color={dim}]{SpikeLog.Line()}[/color]\n" +
            $"[color={dim}]close players (<{Perf.CloseEntityDist:0} u) {Perf.CloseEntities}"
            + $"   {FrameWindowSeconds:0}s avg {Perf.CloseEntitiesAverage(FrameWindowSeconds):0.0}"
            + $"   effects visible {Perf.FxVisible}   {FrameWindowSeconds:0}s avg {Perf.FxVisibleAverage(FrameWindowSeconds):0.0}"
            + $"   sampler {CrowdAnimator.Steps}/frame[/color]\n" +
            $"[color={dim}]{Perf.BisectLine()}[/color]\n" +
            $"[color={dim}]{EngineProfile.Lines()}[/color]\n" +
            $"[color={dim}]script {scriptMs:0.0} ms   phys {physMs:0.0} ms[/color]\n" +
            $"[color={dim}]world {Perf.Ms(Perf.Section.World):0.0}   ents {Perf.Ms(Perf.Section.Entities):0.0}" +
            $" [{Perf.EntityTiers.Total}: {Perf.EntityTiers.Full} full {Perf.EntityTiers.Mid} mid" +
            $" {Perf.EntityTiers.Far} far {Perf.EntityTiers.Off} off]" +
            $"   self {Perf.Ms(Perf.Section.SelfAnim):0.0}   combat {Perf.Ms(Perf.Section.Combat):0.0}" +
            $"   sel {Perf.Ms(Perf.Section.Selection):0.0}   cam {Perf.Ms(Perf.Section.Camera):0.0}" +
            $"   hud {Perf.Ms(Perf.Section.Hud):0.0}   sky {Perf.Ms(Perf.Section.Sky):0.0}" +
            $"   mapfx {Perf.Ms(Perf.Section.MapFx):0.0}   spawn {Perf.Ms(Perf.Section.Spawn):0.0}" +
            $" (scene {Perf.Ms(Perf.Section.BuildScene):0.0} [inst {Perf.Ms(Perf.Section.BuildInstance):0.0} share {Perf.Ms(Perf.Section.BuildShare):0.0}" +
            $" meta {Perf.Ms(Perf.Section.BuildMeta):0.0} enter {Perf.Ms(Perf.Section.BuildEnter):0.0}] graft {Perf.Ms(Perf.Section.BuildGraft):0.0}" +
            $" gear {Perf.Ms(Perf.Section.BuildGear):0.0} rest {Perf.Ms(Perf.Section.BuildRest):0.0})[/color]\n" +
            $"[color={dim}]ents: move {Perf.Ms(Perf.Section.EntMove):0.0}   facing {Perf.Ms(Perf.Section.EntFacing):0.0}" +
            $"   collider {Perf.Ms(Perf.Section.EntCollider):0.0}   lod {Perf.Ms(Perf.Section.EntLod):0.0}   audio {Perf.Ms(Perf.Section.EntAudio):0.0}" +
            $"   plates {Perf.Ms(Perf.Section.EntPlates):0.0}   step {Perf.Ms(Perf.Section.EntStep):0.0} ({Perf.Calls(Perf.Section.EntStep):0})" +
            $"   wings {Perf.Ms(Perf.Section.EntWings):0.0} ({Perf.Calls(Perf.Section.EntWings):0})" +
            $"   [step: sample {Perf.Ms(Perf.Section.StepSample):0.0} pose {Perf.Ms(Perf.Section.StepPose):0.0} seek {Perf.Ms(Perf.Section.StepSeek):0.0}][/color]\n" +
            $"[color={dim}]cape {Perf.Ms(Perf.Section.Cape):0.0}   fx {Perf.FxMs:0.0}" +
            $"   shine {Perf.Ms(Perf.Section.Shine):0.0}   gear {Perf.Ms(Perf.Section.Gear):0.0}" +
            $"   flinch {Perf.Ms(Perf.Section.Flinch):0.0}   ui {Perf.Ms(Perf.Section.Ui):0.0}" +
            $"   net {Perf.Ms(Perf.Section.Net):0.0}   engine {Mathf.Max(0.0, scriptMs - tracked):0.0}" +
            $"   gc0 {Perf.Gc0PerSecond}/s   threads {Perf.ThreadLoad()}[/color]\n" +
            $"[color={dim}]fx parts {Perf.FxParts:0}" +
            $"  (boards {Perf.Calls(Perf.Section.FxBoard):0} {Perf.Ms(Perf.Section.FxBoard):0.0} ms" +
            $"   meshes {Perf.Calls(Perf.Section.FxMesh):0} {Perf.Ms(Perf.Section.FxMesh):0.0} ms" +
            $"   particles {Perf.Calls(Perf.Section.FxPart):0} {Perf.Ms(Perf.Section.FxPart):0.0} ms" +
            $"   roots {Perf.Calls(Perf.Section.FxRoot):0} {Perf.Ms(Perf.Section.FxRoot):0.0} ms" +
            $"   batch {Perf.Ms(Perf.Section.FxBatch):0.0} ms)   shared emitters {FxEmitterPool.Live}   board batches {FxBoardBatch.LiveBatches}[/color]\n" +
            $"[color={dim}]spawn {Perf.Ms(Perf.Section.FxSpawn):0.0} ms   spawned {Perf.PerSecond(Perf.Counter.FxSpawned)}/s" +
            $"  (boards {Perf.Ms(Perf.Section.SpawnBoard):0.0}   meshes {Perf.Ms(Perf.Section.SpawnMesh):0.0}" +
            $"   particles {Perf.Ms(Perf.Section.SpawnParticles):0.0})   pooled parts {Fx.PooledParts}   pending parts {Fx.PendingParts}[/color]\n" +
            $"[color={dim}]draws {draws}   objs {objects}   tris {prims / 1000}k   {PassSplit()}[/color]\n" +
            $"[color={dim}]vram {vram / (1024 * 1024)} MB (textures {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed) / (1024 * 1024)}" +
            $" buffers {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.BufferMemUsed) / (1024 * 1024)})   nodes {nodes:0}   orphan {orphans:0}[/color]\n" +
            $"[color={dim}]{Present()}[/color]\n" +
            $"[color={dim}]{Render3D()}   {RenderTimes()}[/color]";
    }

    private static bool _measuringRender;

    private static string RenderTimes()
    {
        if (Engine.GetMainLoop() is not SceneTree { Root: { } vp })
            return "";
        var rid = vp.GetViewportRid();
        if (!_measuringRender)
        {
            RenderingServer.ViewportSetMeasureRenderTime(rid, true);
            _measuringRender = true;
            return "";
        }
        double cpu = RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid);
        double gpu = RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid);
        double setup = RenderingServer.GetFrameSetupTimeCpu();
        return $"render cpu {cpu:0.0} ms   gpu {gpu:0.0} ms   setup {setup:0.0} ms";
    }

    private static string PassSplit()
    {
        if (Engine.GetMainLoop() is not SceneTree { Root: { } vp })
            return "";
        int Objects(Viewport.RenderInfoType type) => vp.GetRenderInfo(type, Viewport.RenderInfo.ObjectsInFrame);
        int Draws(Viewport.RenderInfoType type) => vp.GetRenderInfo(type, Viewport.RenderInfo.DrawCallsInFrame);
        return $"3d {Objects(Viewport.RenderInfoType.Visible)}/{Draws(Viewport.RenderInfoType.Visible)}" +
               $"   shadow {Objects(Viewport.RenderInfoType.Shadow)}/{Draws(Viewport.RenderInfoType.Shadow)}" +
               $"   2d {Objects(Viewport.RenderInfoType.Canvas)}/{Draws(Viewport.RenderInfoType.Canvas)}";
    }

    private static string Present()
    {
        var vsync = DisplayServer.WindowGetVsyncMode();
        float hz = DisplayServer.ScreenGetRefreshRate(DisplayServer.WindowGetCurrentScreen());
        string cap = Engine.MaxFps == 0 ? "uncapped" : $"cap {Engine.MaxFps}";
        return $"vsync {vsync}   {cap}   screen {hz:0}Hz";
    }

    private static string Render3D()
    {
        if (Engine.GetMainLoop() is not SceneTree { Root: { } vp })
            return "";
        var win = vp.Size;
        float s = vp.Scaling3DScale;
        return $"3D {win.X * s:0}x{win.Y * s:0}/{win.X}x{win.Y}  {vp.Scaling3DMode}  {vp.Msaa3D}";
    }

    private bool ToggleBisect(KeyChord chord)
    {
        static bool Is(KeyAction action, KeyChord chord)
        {
            var bind = KeyBinds.Get(action);
            return bind.Assigned && chord == bind;
        }
        if (Is(KeyAction.PerfSkipPoses, chord)) Perf.SkipPoses = !Perf.SkipPoses;
        else if (Is(KeyAction.PerfSkipMove, chord)) Perf.SkipMove = !Perf.SkipMove;
        else if (Is(KeyAction.PerfSkipPlates, chord)) Perf.SkipPlates = !Perf.SkipPlates;
        else if (Is(KeyAction.PerfSkipEmitters, chord)) Perf.SkipEmitters = !Perf.SkipEmitters;
        else if (Is(KeyAction.PerfSkipAudio, chord)) Perf.SkipAudio = !Perf.SkipAudio;
        else if (Is(KeyAction.PerfSkipFx, chord)) Perf.SkipFx = !Perf.SkipFx;
        else if (Is(KeyAction.PerfSkipLamps, chord)) Perf.SkipLamps = !Perf.SkipLamps;
        else if (Is(KeyAction.PerfCensus, chord)) GD.Print($"[census] {SceneCensus.Write(GetTree())}");
        else if (Is(KeyAction.PerfEngineProfile, chord)) EngineProfile.Toggle();
        else if (Is(KeyAction.PerfAutoBisect, chord) && !_auto.Running)
        {
            Visible = true;
            Config.SetShowStats(true);
            SetDetail(true);
            _auto.Start(GetTree(), SetDetail);
        }
        else if (Is(KeyAction.PerfHudSweep, chord) && !_auto.Running)
        {
            Visible = true;
            Config.SetShowStats(true);
            SetDetail(true);
            _auto.StartHudSweep(GetTree());
        }
        else if (Is(KeyAction.PerfSceneSweep, chord) && !_auto.Running)
        {
            Visible = true;
            Config.SetShowStats(true);
            SetDetail(true);
            _auto.StartSceneSweep(GetTree());
        }
        else return false;
        return true;
    }

    private static string FpsColor(double fps)
        => fps >= 55 ? Green : fps >= 30 ? Yellow : Red;

    public override void _Input(InputEvent ev)
    {
        if (ev is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (!InWorld) return;
        var chord = KeyChord.From(key);
        if (ToggleBisect(chord)) return;
        var bind = KeyBinds.Get(KeyAction.PerformanceOverlay);
        if (!bind.Assigned || chord != bind) return;

        if (!Visible) { Visible = true; _detail = false; }
        else if (!_detail) _detail = true;
        else { Visible = false; _detail = false; }
        SetDetail(_detail);
        Config.SetShowStats(Visible);
    }
}
