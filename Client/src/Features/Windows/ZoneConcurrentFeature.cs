using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class ZoneConcurrentFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly ZoneConcurrentSession zoneConcurrent;
    private readonly IZoneConcurrentRequests requests;

    public ZoneConcurrentFeature(IWorldContext context, ZoneConcurrentSession zoneConcurrent, IZoneConcurrentRequests requests)
    {
        this.context = context;
        this.zoneConcurrent = zoneConcurrent;
        this.requests = requests;
    }

    private CanvasLayer zoncLayer = null!;
    private PanelContainer zoncHeader = null!;
    private Label zoncHeaderLbl = null!;
    private VBoxContainer zoncRows = null!;
    private Godot.Timer zoncTimer = null!;
    private bool zoncExpanded = true;

    private const double ZoncPollSeconds = 20.0;

    public void Build()
    {
        BuildPanel();
        zoneConcurrent.ZoneConcurrentEvent += On;

        zoncTimer = new Godot.Timer { WaitTime = ZoncPollSeconds, Autostart = true, OneShot = false };
        zoncTimer.Timeout += Request;
        context.Root.AddChild(zoncTimer);
        Request();
    }

    public void Dispose()
    {
        zoneConcurrent.ZoneConcurrentEvent -= On;
    }

    private void BuildPanel()
    {
        zoncLayer = new CanvasLayer { Layer = UiLayers.SideWindows };
        context.Root.AddChild(zoncLayer);

        var root = new VBoxContainer();
        root.AnchorLeft = 0; root.AnchorRight = 0; root.AnchorTop = 0; root.AnchorBottom = 0;
        root.OffsetLeft = 12; root.OffsetTop = 12;
        root.AddThemeConstantOverride("separation", 0);
        zoncLayer.AddChild(root);

        zoncHeader = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(212, 0) };
        zoncHeader.AddThemeStyleboxOverride("panel", UiTheme.HeaderBand(4));
        var headerBar = new ZoncHeaderBar(ToggleExpanded);
        headerBar.AddThemeConstantOverride("separation", 6);
        zoncHeader.AddChild(headerBar);
        var marker = UiTheme.Text("⚔", 13, UiTheme.GoldBright, HorizontalAlignment.Center);
        marker.CustomMinimumSize = new Vector2(16, 20);
        marker.MouseFilter = Control.MouseFilterEnum.Ignore;
        headerBar.AddChild(marker);
        zoncHeaderLbl = UiTheme.Text("Battle Zones", 13, UiTheme.TextHi);
        zoncHeaderLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        zoncHeaderLbl.MouseFilter = Control.MouseFilterEnum.Ignore;
        headerBar.AddChild(zoncHeaderLbl);
        root.AddChild(zoncHeader);

        var body = new PanelContainer { CustomMinimumSize = new Vector2(212, 0) };
        body.AddThemeStyleboxOverride("panel", UiTheme.Inset(3));
        root.AddChild(body);
        zoncRows = new VBoxContainer();
        zoncRows.AddThemeConstantOverride("separation", 2);
        body.AddChild(zoncRows);

        var loading = UiTheme.Text("Loading…", 12, UiTheme.TextLo);
        zoncRows.AddChild(loading);

        HudLayout.Attach(
            root,
            "hud_zone_population",
            zoncHeader,
            () => new Vector2(12f, 12f));
    }

    private void ToggleExpanded()
    {
        zoncExpanded = !zoncExpanded;
        ((PanelContainer)zoncRows.GetParent()).Visible = zoncExpanded;
        zoncHeaderLbl.Text = zoncExpanded ? "Battle Zones" : "Battle Zones  ▸";
    }

    private void Request()
    {
        if (context.Ready) requests.ZoneConcurrentRequest();
    }

    private void On(IReadOnlyList<ZoneConcurrentCount> zones)
    {
        foreach (var c in zoncRows.GetChildren()) c.QueueFree();

        if (zones.Count == 0)
        {
            zoncRows.AddChild(UiTheme.Text("No battle zones open.", 12, UiTheme.TextLo));
            return;
        }

        int total = 0;
        foreach (var z in zones)
        {
            total += z.Players;
            zoncRows.AddChild(BuildZoncRow(ZoncName(z.ZoneId), z.Players));
        }
        zoncHeaderLbl.Text = zoncExpanded ? $"Battle Zones  ({total})" : $"Battle Zones  ({total})  ▸";
    }

    private static Control BuildZoncRow(string name, int players)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);

        var nameLbl = UiTheme.Text(name, 12, UiTheme.TextLo);
        nameLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        nameLbl.ClipText = true;
        row.AddChild(nameLbl);

        Color col = players == 0 ? UiTheme.TextDim : players >= 20 ? UiTheme.GoldBright : UiTheme.Gold;
        var countLbl = UiTheme.Text(players.ToString(), 12, col, HorizontalAlignment.Right);
        countLbl.CustomMinimumSize = new Vector2(34, 0);
        row.AddChild(countLbl);
        return row;
    }

    private static string ZoncName(int zoneId)
    {
        foreach (var z in ZoneCatalog.All)
            if (z.Id == zoneId) return z.Name;
        return zoneId switch
        {
            65 => "Battle 5",
            _  => $"Zone {zoneId}",
        };
    }

    private sealed partial class ZoncHeaderBar : HBoxContainer
    {
        private readonly System.Action onClick;
        public ZoncHeaderBar(System.Action onClick)
        {
            this.onClick = onClick;
            MouseFilter = MouseFilterEnum.Stop;
            CustomMinimumSize = new Vector2(0, 20);
        }

        public override void _GuiInput(InputEvent ev)
        {
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                onClick();
                AcceptEvent();
            }
        }
    }
}
