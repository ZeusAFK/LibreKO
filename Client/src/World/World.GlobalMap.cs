using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _globalMapLayer = null!;
    private HudWindow _globalMapPanel = null!;
    private VBoxContainer _globalMapList = null!;
    private bool _globalMapShown;

    private void GlobalMapInit()
    {
        _globalMapLayer = new CanvasLayer { Layer = 74 };
        AddChild(_globalMapLayer);
        _globalMapPanel = new HudWindow("globalmap", "World Map", new Vector2(180, 120)) { Visible = false };
        _globalMapPanel.Closed += CloseGlobalMap;
        _globalMapLayer.AddChild(_globalMapPanel);

        var root = _globalMapPanel.Body;
        root.AddThemeConstantOverride("separation", 6);
        root.AddChild(UiTheme.SectionTitle("World Map"));

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        var hZone = UiTheme.Text("Zone", 12, UiTheme.TextLo); hZone.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var hPop = UiTheme.Text("Players", 12, UiTheme.TextLo); hPop.CustomMinimumSize = new Vector2(70, 0);
        var hOwner = UiTheme.Text("Nation", 12, UiTheme.TextLo); hOwner.CustomMinimumSize = new Vector2(90, 0);
        header.AddChild(hZone);
        header.AddChild(hPop);
        header.AddChild(hOwner);
        root.AddChild(header);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(380, 320), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        _globalMapList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _globalMapList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_globalMapList);

        Net.I.GlobalMapListEvent += OnGlobalMapList;
    }

    private void GlobalMapDispose()
    {
        Net.I.GlobalMapListEvent -= OnGlobalMapList;
    }

    private void ToggleGlobalMap()
    {
        if (_globalMapShown) { CloseGlobalMap(); return; }
        _globalMapPanel.Visible = true;
        _globalMapShown = true;
        Net.I.SendGlobalMapList();
    }

    private void CloseGlobalMap()
    {
        if (!_globalMapShown) return;
        _globalMapShown = false;
        _globalMapPanel.Visible = false;
    }

    private void OnGlobalMapList(List<GlobalMapZone> zones)
    {
        foreach (var c in _globalMapList.GetChildren()) c.QueueFree();

        int total = 0;
        foreach (var z in zones)
        {
            total += z.Population;
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);

            var name = UiTheme.Text(z.Name, 13, UiTheme.TextHi);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hb.AddChild(name);

            var pop = UiTheme.Text(z.Population.ToString(), 13, z.Population > 0 ? UiTheme.TextHi : UiTheme.TextLo);
            pop.CustomMinimumSize = new Vector2(70, 0);
            hb.AddChild(pop);

            var owner = UiTheme.Text(GlobalMapNationName(z.OwnerNation), 13, GlobalMapNationColor(z.OwnerNation));
            owner.CustomMinimumSize = new Vector2(90, 0);
            hb.AddChild(owner);

            _globalMapList.AddChild(row);
        }

        if (_globalMapList.GetChildCount() == 0)
        {
            var e = HudStyle.Label(13); e.Text = "No zones available.";
            _globalMapList.AddChild(e);
        }
        else
        {
            var footer = UiTheme.Text($"Total players online: {total}", 12, UiTheme.Gold);
            _globalMapList.AddChild(footer);
        }
    }

    private static string GlobalMapNationName(byte nation) => nation switch
    {
        1 => "Karus",
        2 => "El Morad",
        _ => "Neutral",
    };

    private Color GlobalMapNationColor(byte nation) => nation switch
    {
        1 => new Color("d08a6a"),
        2 => new Color("6a9ad0"),
        _ => UiTheme.TextLo,
    };
}
