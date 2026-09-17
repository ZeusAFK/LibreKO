using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _zoncLayer = null!;
    private PanelContainer _zoncHeader = null!;
    private Label _zoncHeaderLbl = null!;
    private VBoxContainer _zoncRows = null!;
    private Godot.Timer _zoncTimer = null!;
    private bool _zoncExpanded = true;

    private const double ZoncPollSeconds = 20.0;

    private void ZoneConcurrentInit()
    {
        BuildZoneConcurrentPanel();
        Net.I.ZoneConcurrentEvent += OnZoneConcurrent;

        _zoncTimer = new Godot.Timer { WaitTime = ZoncPollSeconds, Autostart = true, OneShot = false };
        _zoncTimer.Timeout += RequestZoneConcurrent;
        AddChild(_zoncTimer);
        RequestZoneConcurrent();
    }

    private void ZoneConcurrentDispose()
    {
        Net.I.ZoneConcurrentEvent -= OnZoneConcurrent;
    }

    private void BuildZoneConcurrentPanel()
    {
        _zoncLayer = new CanvasLayer { Layer = 63 };
        AddChild(_zoncLayer);

        var root = new VBoxContainer();
        root.AnchorLeft = 0; root.AnchorRight = 0; root.AnchorTop = 0; root.AnchorBottom = 0;
        root.OffsetLeft = 12; root.OffsetTop = 12;
        root.AddThemeConstantOverride("separation", 0);
        _zoncLayer.AddChild(root);

        _zoncHeader = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(212, 0) };
        _zoncHeader.AddThemeStyleboxOverride("panel", UiTheme.HeaderBand(4));
        var headerBar = new ZoncHeaderBar(ToggleZoneConcurrentExpanded);
        headerBar.AddThemeConstantOverride("separation", 6);
        _zoncHeader.AddChild(headerBar);
        var marker = UiTheme.Text("⚔", 13, UiTheme.GoldBright, HorizontalAlignment.Center);
        marker.CustomMinimumSize = new Vector2(16, 20);
        marker.MouseFilter = Control.MouseFilterEnum.Ignore;
        headerBar.AddChild(marker);
        _zoncHeaderLbl = UiTheme.Text("Battle Zones", 13, UiTheme.TextHi);
        _zoncHeaderLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _zoncHeaderLbl.MouseFilter = Control.MouseFilterEnum.Ignore;
        headerBar.AddChild(_zoncHeaderLbl);
        root.AddChild(_zoncHeader);

        var body = new PanelContainer { CustomMinimumSize = new Vector2(212, 0) };
        body.AddThemeStyleboxOverride("panel", UiTheme.Inset(3));
        root.AddChild(body);
        _zoncRows = new VBoxContainer();
        _zoncRows.AddThemeConstantOverride("separation", 2);
        body.AddChild(_zoncRows);

        var loading = UiTheme.Text("Loading…", 12, UiTheme.TextLo);
        _zoncRows.AddChild(loading);

        HudLayout.Attach(
            root,
            "hud_zone_population",
            _zoncHeader,
            () => new Vector2(12f, 12f));
    }

    private void ToggleZoneConcurrentExpanded()
    {
        _zoncExpanded = !_zoncExpanded;
        ((PanelContainer)_zoncRows.GetParent()).Visible = _zoncExpanded;
        _zoncHeaderLbl.Text = _zoncExpanded ? "Battle Zones" : "Battle Zones  ▸";
    }

    private void RequestZoneConcurrent()
    {
        if (_worldReady) Net.I.SendZoneConcurrentRequest();
    }

    private void OnZoneConcurrent(IReadOnlyList<Net.ZoneConcurrentCount> zones)
    {
        foreach (var c in _zoncRows.GetChildren()) c.QueueFree();

        if (zones.Count == 0)
        {
            _zoncRows.AddChild(UiTheme.Text("No battle zones open.", 12, UiTheme.TextLo));
            return;
        }

        int total = 0;
        foreach (var z in zones)
        {
            total += z.Players;
            _zoncRows.AddChild(BuildZoncRow(ZoncZoneName(z.ZoneId), z.Players));
        }
        _zoncHeaderLbl.Text = _zoncExpanded ? $"Battle Zones  ({total})" : $"Battle Zones  ({total})  ▸";
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

    private static string ZoncZoneName(int zoneId)
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
        private readonly System.Action _onClick;
        public ZoncHeaderBar(System.Action onClick)
        {
            _onClick = onClick;
            MouseFilter = MouseFilterEnum.Stop;
            CustomMinimumSize = new Vector2(0, 20);
        }

        public override void _GuiInput(InputEvent ev)
        {
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                _onClick();
                AcceptEvent();
            }
        }
    }
}
