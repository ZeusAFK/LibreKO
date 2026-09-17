using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _dqLayer = null!;
    private HudWindow _dqPanel = null!;
    private VBoxContainer _dqList = null!;
    private bool _dqShown;

    private void DailyQuestInit()
    {
        _dqLayer = new CanvasLayer { Layer = 73 };
        AddChild(_dqLayer);
        _dqPanel = new HudWindow("dailyquest", "Daily Quests", new Vector2(170, 120)) { Visible = false };
        _dqPanel.Closed += CloseDailyQuest;
        _dqLayer.AddChild(_dqPanel);
        var root = _dqPanel.Body;
        root.AddThemeConstantOverride("separation", 6);
        root.AddChild(UiTheme.SectionTitle("Daily Quests"));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(340, 220), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        _dqList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _dqList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_dqList);

        Net.I.DailyQuestListEvent += OnDailyQuestList;
        Net.I.DailyQuestClaimEvent += OnDailyQuestClaim;
    }

    private void DailyQuestDispose()
    {
        Net.I.DailyQuestListEvent -= OnDailyQuestList;
        Net.I.DailyQuestClaimEvent -= OnDailyQuestClaim;
    }

    private void ToggleDailyQuest()
    {
        if (_dqShown) { CloseDailyQuest(); return; }
        _dqPanel.Visible = true;
        _dqShown = true;
        Net.I.SendDailyQuestList();
    }

    private void CloseDailyQuest()
    {
        if (!_dqShown) return;
        _dqShown = false;
        _dqPanel.Visible = false;
    }

    private void OnDailyQuestList(List<DailyQuestEntry> list)
    {
        foreach (var c in _dqList.GetChildren()) c.QueueFree();
        foreach (var q in list)
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);
            var name = UiTheme.Text(q.Title, 13, q.Available ? UiTheme.TextHi : UiTheme.TextLo);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hb.AddChild(name);
            if (q.Claimed) hb.AddChild(UiTheme.Text("Done", 12, UiTheme.Gold));
            else if (q.Available)
            {
                int id = q.Id;
                var btn = new Button { Text = "Claim", FocusMode = Control.FocusModeEnum.None };
                btn.Pressed += () => Net.I.SendDailyQuestClaim(id);
                hb.AddChild(btn);
            }
            else hb.AddChild(UiTheme.Text("Locked", 12, UiTheme.TextLo));
            _dqList.AddChild(row);
        }
        if (_dqList.GetChildCount() == 0)
        {
            var e = HudStyle.Label(13); e.Text = "No daily quests.";
            _dqList.AddChild(e);
        }
    }

    private void OnDailyQuestClaim(int id, bool ok)
    {
        if (ok) Net.I.SendDailyQuestList();
    }
}
