using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _fishHallLayer = null!;
    private HudWindow _fishHallPanel = null!;
    private VBoxContainer _fishHallList = null!;
    private bool _fishHallShown;

    private void FishingHallInit()
    {
        _fishHallLayer = new CanvasLayer { Layer = 74 };
        AddChild(_fishHallLayer);
        _fishHallPanel = new HudWindow("fishinghall", "Fishing Hall of Fame", new Vector2(190, 130)) { Visible = false };
        _fishHallPanel.Closed += CloseFishingHall;
        _fishHallLayer.AddChild(_fishHallPanel);
        var root = _fishHallPanel.Body;
        root.AddThemeConstantOverride("separation", 6);
        root.AddChild(UiTheme.SectionTitle("Top Anglers"));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(320, 320), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        _fishHallList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _fishHallList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_fishHallList);

        Net.I.FishingHallListEvent += OnFishingHallList;
    }

    private void FishingHallDispose()
    {
        Net.I.FishingHallListEvent -= OnFishingHallList;
    }

    private void ToggleFishingHall()
    {
        if (_fishHallShown) { CloseFishingHall(); return; }
        _fishHallPanel.Visible = true;
        _fishHallShown = true;
        Net.I.SendFishingHallList();
    }

    private void CloseFishingHall()
    {
        if (!_fishHallShown) return;
        _fishHallShown = false;
        _fishHallPanel.Visible = false;
    }

    private void OnFishingHallList(List<FishingHallEntry> list)
    {
        foreach (var c in _fishHallList.GetChildren()) c.QueueFree();
        string myName = Net.I.LastEnter.Name ?? "";
        foreach (var e in list)
        {
            bool isMe = e.Name == myName;
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row(selected: isMe));
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);

            var rank = UiTheme.Text("#" + e.Rank, 13, e.Rank <= 3 ? UiTheme.Gold : UiTheme.TextLo);
            rank.CustomMinimumSize = new Vector2(38, 0);
            hb.AddChild(rank);

            var name = UiTheme.Text(e.Name, 13, isMe ? UiTheme.GoldBright : UiTheme.TextHi);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hb.AddChild(name);

            hb.AddChild(UiTheme.Text(e.Score.ToString("N0"), 13, UiTheme.Gold));

            _fishHallList.AddChild(row);
        }
        if (_fishHallList.GetChildCount() == 0)
        {
            var none = HudStyle.Label(13); none.Text = "No anglers ranked yet.";
            _fishHallList.AddChild(none);
        }
    }
}
