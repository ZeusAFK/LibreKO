using Godot;

namespace LibreKO.Features.Hud;

internal sealed class GenieSubFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly GenieFeature genie;
    private readonly GenieSession genieSession;

    public GenieSubFeature(IWorldContext context, GenieFeature genie, GenieSession genieSession)
    {
        this.context = context;
        this.genie = genie;
        this.genieSession = genieSession;
    }

    public void Build()
    {
        genie.Changed += RefreshSub;
        subLayer = new CanvasLayer { Layer = genie.Layer.Layer };
        context.Root.AddChild(subLayer);
        context.Hud.PluginSeam(subLayer, LibreKO.Plugins.HudPart.GenieSub);
        Sub = new PanelContainer
        {
            CustomMinimumSize = new Vector2(SubWidth, 0),
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        Sub.AddThemeStyleboxOverride("panel", UiTheme.Panel(6, true));
        subLayer.AddChild(Sub);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 3);
        Sub.AddChild(col);

        var head = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        head.AddThemeConstantOverride("separation", 4);
        col.AddChild(head);
        var title = UiTheme.Text(ItemData.Text(TitleText, "Knight Genie"), 12, UiTheme.GoldBright);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.VerticalAlignment = VerticalAlignment.Center;
        title.MouseFilter = Control.MouseFilterEnum.Ignore;
        head.AddChild(title);
        subToggle = new Button { FocusMode = Control.FocusModeEnum.None };
        subToggle.AddThemeFontSizeOverride("font_size", 11);
        subToggle.Pressed += ToggleFromSub;
        head.AddChild(subToggle);
        var open = UiTheme.IconButton("≡", "Genie settings");
        open.Pressed += genie.Toggle;
        head.AddChild(open);

        var bar = new Control { CustomMinimumSize = new Vector2(0, 12) };
        col.AddChild(bar);
        subBar = new ProgressBar { ShowPercentage = false, MaxValue = PointsPerSpirit, MouseFilter = Control.MouseFilterEnum.Ignore };
        subBar.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bar.AddChild(subBar);
        subPoints = UiTheme.Text("", 10, Colors.White, HorizontalAlignment.Center);
        subPoints.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        subPoints.VerticalAlignment = VerticalAlignment.Center;
        subPoints.MouseFilter = Control.MouseFilterEnum.Ignore;
        bar.AddChild(subPoints);

        for (int i = 0; i < SubRows; i++)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);
            col.AddChild(row);
            int index = i;
            var on = new CheckBox { FocusMode = Control.FocusModeEnum.None, TooltipText = "Attack this monster" };
            on.Toggled += pressed => ToggleSubRow(index, pressed);
            row.AddChild(on);
            var name = UiTheme.Text("", 11, UiTheme.TextHi);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            name.ClipText = true;
            name.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(name);
            Button? action = null;
            if (index < SubLastRow)
            {
                action = new Button { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(22, 0) };
                action.AddThemeFontSizeOverride("font_size", 11);
                action.Pressed += () => SubRowAction(index);
                row.AddChild(action);
            }
            genieSubRows.Add(new SubRow(on, name, action));
        }

        HudLayout.Attach(Sub, SubId, head, SubDefaultPosition);
        RefreshSub();
    }

    public void Dispose()
    {
        genie.Changed -= RefreshSub;
    }

    private const string SubId = "genie_sub";
    internal const int SubRows = 3;
    private const int SubLastRow = SubRows - 1;
    internal const int PointsPerSpirit = 120;
    private const double ToggleCooldown = 10.0;
    private const float SubWidth = 189f;
    private const int WaitText = 31007;
    private const int StartText = 31008;
    private const int StopText = 31009;
    private const int TitleText = 31016;
    private const float SubMargin = 12f;

    private CanvasLayer subLayer = null!;
    internal PanelContainer? Sub { get; private set; }
    private Button subToggle = null!;
    private ProgressBar subBar = null!;
    private Label subPoints = null!;
    private readonly record struct SubRow(CheckBox On, Label Name, Button? Action);

    private readonly List<SubRow> genieSubRows = new();

    private double toggledAt = double.NegativeInfinity;

    private Vector2 SubDefaultPosition()
    {
        var viewport = Sub!.GetViewportRect().Size;
        return new Vector2(viewport.X - SubWidth - SubMargin, viewport.Y * 0.5f);
    }

    internal void ToggleFromSub()
    {
        double now = context.Now;
        if (now - toggledAt < ToggleCooldown)
        {
            int left = (int)Math.Ceiling(ToggleCooldown - (now - toggledAt));
            context.Log.Notice(ItemData.Text(WaitText, "Knight Genie will be available after %d seconds.").Replace("%d", left.ToString()));
            return;
        }
        toggledAt = now;
        genie.ToggleRun();
    }

    private void ToggleSubRow(int index, bool on)
    {
        var targets = genie.TargetsInOrder();
        if (index == SubLastRow)
        {
            if (on && targets.Count == SubLastRow) genie.AddTarget();
            else if (!on && index < targets.Count) genie.RemoveTarget(targets[index]);
            RefreshSub();
            return;
        }
        if (index < targets.Count) genie.SetTargetOn(targets[index], on);
    }

    private void SubRowAction(int index)
    {
        var targets = genie.TargetsInOrder();
        if (index < targets.Count) genie.RemoveTarget(targets[index]);
        else genie.AddTarget();
        RefreshSub();
    }

    internal void RefreshSub()
    {
        if (Sub == null || !GodotObject.IsInstanceValid(Sub)) return;
        context.Plugins.Genie();
        Sub.Visible = genie.Points > 0 || genieSession.GenieRunning;
        bool running = genieSession.GenieRunning;
        subToggle.Text = running ? "Stop" : "Start";
        subToggle.TooltipText = ItemData.Text(running ? StopText : StartText, running ? "Disable Knight Genie" : "Start Knight Genie");
        subBar.MaxValue = Math.Max(PointsPerSpirit, genie.Points);
        subBar.Value = genie.Points;
        subPoints.Text = genie.Points.ToString();
        var targets = genie.TargetsInOrder();
        for (int i = 0; i < genieSubRows.Count; i++)
        {
            var (on, name, action) = genieSubRows[i];
            bool filled = i < targets.Count;
            name.Text = filled ? targets[i] : "";
            if (action == null)
            {
                on.Disabled = targets.Count < SubLastRow;
                on.SetPressedNoSignal(filled);
                on.TooltipText = filled ? "Return" : "Add the selected monster";
                continue;
            }
            on.Disabled = !filled;
            on.SetPressedNoSignal(filled && !genie.MonstersOff.Contains(targets[i]));
            action.Text = filled ? "−" : "+";
            action.TooltipText = filled ? "Return" : "Add the selected monster";
        }
    }
}
