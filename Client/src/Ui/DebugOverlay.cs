using Godot;

namespace LibreKO;

public partial class DebugOverlay : CanvasLayer
{
    private RichTextLabel _label = null!;
    private double _accum;
    private bool _detail;
    private const float CompactWidth = 320f;
    private const float DetailWidth = 560f;

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
        };
        _label.CustomMinimumSize = new Vector2(CompactWidth, 0f);
        HudPlacement.Stats(new Vector2(HudAnchor.Edge, HudAnchor.Edge)).ApplyTo(_label);
        _label.AddThemeFontSizeOverride("normal_font_size", 14);
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", 4);
        AddChild(_label);
    }

    private bool InWorld => GetTree().CurrentScene is World;

    public override void _Process(double delta)
    {
        bool show = InWorld && Config.ShowStats;
        if (Visible != show) Visible = show;
        if (!show) return;

        _accum += delta;
        if (_accum < 0.25)
            return;
        _accum = 0;

        double fps = Engine.GetFramesPerSecond();
        double avgMs = fps > 0 ? 1000.0 / fps : 0;

        string c = FpsColor(fps);
        string text = $"[color={c}]FPS {fps:0}[/color]   [color={c}]{avgMs:0} ms[/color]{Ping()}";
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
        return
            $"[color={dim}]script {scriptMs:0.0} ms   phys {physMs:0.0} ms[/color]\n" +
            $"[color={dim}]draws {draws}   objs {objects}   tris {prims / 1000}k[/color]\n" +
            $"[color={dim}]vram {vram / (1024 * 1024)} MB   nodes {nodes:0}   orphan {orphans:0}[/color]\n" +
            $"[color={dim}]{Present()}[/color]\n" +
            $"[color={dim}]{Render3D()}[/color]";
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

    private static string FpsColor(double fps)
        => fps >= 55 ? Green : fps >= 30 ? Yellow : Red;

    public override void _Input(InputEvent ev)
    {
        if (ev is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (!InWorld) return;
        var bind = KeyBinds.Get(KeyAction.PerformanceOverlay);
        if (!bind.Assigned || KeyChord.From(key) != bind) return;

        if (!Visible) { Visible = true; _detail = false; }
        else if (!_detail) _detail = true;
        else { Visible = false; _detail = false; }
        _label.CustomMinimumSize = new Vector2(_detail ? DetailWidth : CompactWidth, 0f);
        _accum = 1.0;
        Config.SetShowStats(Visible);
    }
}
