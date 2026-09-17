using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _duelLayer = null!;
    private HudWindow _duelPanel = null!;
    private VBoxContainer _duelList = null!;
    private LineEdit _duelStakeEdit = null!;
    private bool _duelShown;

    private int _duelMineId;

    private void DuelInit()
    {
        _duelLayer = new CanvasLayer { Layer = 60 };
        AddChild(_duelLayer);
        _duelPanel = new HudWindow("duel", "Duel Lobby", new Vector2(220, 130)) { Visible = false };
        _duelPanel.Closed += CloseDuel;
        _duelLayer.AddChild(_duelPanel);
        var root = _duelPanel.Body;
        root.AddThemeConstantOverride("separation", 6);

        root.AddChild(UiTheme.SectionTitle("Create a Duel"));
        var form = new HBoxContainer();
        form.AddThemeConstantOverride("separation", 6);
        root.AddChild(form);

        form.AddChild(UiTheme.Text("Stake", 12, UiTheme.TextLo));
        _duelStakeEdit = new LineEdit { CustomMinimumSize = new Vector2(100, 0), PlaceholderText = "gold (0 = honor)" };
        form.AddChild(_duelStakeEdit);

        var createBtn = new Button { Text = "Create", FocusMode = Control.FocusModeEnum.None };
        createBtn.Pressed += OnDuelCreatePressed;
        form.AddChild(createBtn);

        root.AddChild(UiTheme.SectionTitle("Open Duels"));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(380, 300), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        _duelList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _duelList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_duelList);

        Net.I.DuelListEvent += OnDuelList;
        Net.I.DuelCreateEvent += OnDuelCreate;
        Net.I.DuelJoinEvent += OnDuelJoin;
        Net.I.DuelLeaveEvent += OnDuelLeave;
    }

    private void DuelDispose()
    {
        Net.I.DuelListEvent -= OnDuelList;
        Net.I.DuelCreateEvent -= OnDuelCreate;
        Net.I.DuelJoinEvent -= OnDuelJoin;
        Net.I.DuelLeaveEvent -= OnDuelLeave;
    }

    private void ToggleDuel()
    {
        if (_duelShown) { CloseDuel(); return; }
        _duelPanel.Visible = true;
        _duelShown = true;
        Net.I.SendDuelList();
    }

    private void CloseDuel()
    {
        if (!_duelShown) return;
        _duelShown = false;
        _duelPanel.Visible = false;
    }

    private void OnDuelCreatePressed()
    {
        if (!int.TryParse(_duelStakeEdit.Text.Trim(), out int stake) || stake < 0) return;
        Net.I.SendDuelCreate(stake);
    }

    private void OnDuelList(List<DuelEntry> list)
    {
        string myName = Net.I.LastEnter.Name;
        foreach (var c in _duelList.GetChildren()) c.QueueFree();
        foreach (var d in list)
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);

            var creator = UiTheme.Text(d.Creator, 13, UiTheme.TextHi);
            creator.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hb.AddChild(creator);

            hb.AddChild(UiTheme.Text(d.Stake > 0 ? $"{d.Stake:N0}g" : "Honor", 12, UiTheme.Gold));
            hb.AddChild(UiTheme.Text(d.Full ? "Full" : "Open", 12, d.Full ? UiTheme.TextLo : UiTheme.TextHi));

            int id = d.Id;
            bool mine = id == _duelMineId || d.Creator == myName;
            if (mine)
            {
                var leave = new Button { Text = "Leave", FocusMode = Control.FocusModeEnum.None };
                leave.Pressed += () => Net.I.SendDuelLeave(id);
                hb.AddChild(leave);
            }
            else if (!d.Full)
            {
                var join = new Button { Text = "Join", FocusMode = Control.FocusModeEnum.None };
                join.Pressed += () => Net.I.SendDuelJoin(id);
                hb.AddChild(join);
            }
            else
            {
                hb.AddChild(UiTheme.Text("—", 12, UiTheme.TextLo));
            }

            _duelList.AddChild(row);
        }
        if (_duelList.GetChildCount() == 0)
        {
            var e = HudStyle.Label(13); e.Text = "No open duels.";
            _duelList.AddChild(e);
        }
    }

    private void OnDuelCreate(int duelId, bool ok)
    {
        if (ok)
        {
            _duelMineId = duelId;
            _duelStakeEdit.Text = "";
        }
    }

    private void OnDuelJoin(int duelId, bool ok)
    {
        if (ok) _duelMineId = duelId;
    }

    private void OnDuelLeave(int duelId, bool ok)
    {
        if (ok && duelId == _duelMineId) _duelMineId = 0;
    }
}
