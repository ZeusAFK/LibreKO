using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _eventQuestLayer = null!;
    private HudWindow _eventQuestPanel = null!;
    private VBoxContainer _eventQuestList = null!;
    private bool _eventQuestShown;

    private void EventQuestInit()
    {
        _eventQuestLayer = new CanvasLayer { Layer = 62 };
        AddChild(_eventQuestLayer);
        _eventQuestPanel = new HudWindow("eventquests", Localization.Loc.Tr("Event Quests"), new Vector2(200, 130)) { Visible = false };
        _eventQuestPanel.Closed += CloseEventQuests;
        _eventQuestLayer.AddChild(_eventQuestPanel);
        var root = _eventQuestPanel.Body;
        root.AddThemeConstantOverride("separation", 6);
        root.AddChild(UiTheme.SectionTitle(Localization.Loc.Tr("Limited-Time Event Quests")));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(360, 320), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        _eventQuestList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _eventQuestList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_eventQuestList);

        Net.I.EventQuestListEvent += OnEventQuestList;
        Net.I.EventQuestAcceptEvent += OnEventQuestAccept;
        Net.I.EventQuestClaimEvent += OnEventQuestClaim;
    }

    private void EventQuestDispose()
    {
        Net.I.EventQuestListEvent -= OnEventQuestList;
        Net.I.EventQuestAcceptEvent -= OnEventQuestAccept;
        Net.I.EventQuestClaimEvent -= OnEventQuestClaim;
    }

    private void ToggleEventQuests()
    {
        if (_eventQuestShown) { CloseEventQuests(); return; }
        _eventQuestPanel.Visible = true;
        _eventQuestShown = true;
        Net.I.SendEventQuestList();
    }

    private void CloseEventQuests()
    {
        if (!_eventQuestShown) return;
        _eventQuestShown = false;
        _eventQuestPanel.Visible = false;
    }

    private void OnEventQuestList(List<EventQuestEntry> list)
    {
        foreach (var c in _eventQuestList.GetChildren()) c.QueueFree();
        foreach (var q in list)
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);
            var name = UiTheme.Text(q.Title, 13, q.Accepted ? UiTheme.TextHi : UiTheme.TextLo);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hb.AddChild(name);

            if (q.Claimable)
            {
                int id = q.Id;
                var btn = new Button { Text = Localization.Loc.Tr("Claim"), FocusMode = Control.FocusModeEnum.None };
                btn.Pressed += () => Net.I.SendEventQuestClaim(id);
                hb.AddChild(btn);
            }
            else if (q.Accepted)
            {
                hb.AddChild(UiTheme.Text(Localization.Loc.Tr("Accepted"), 12, UiTheme.Gold));
            }
            else
            {
                int id = q.Id;
                var btn = new Button { Text = Localization.Loc.Tr("Accept"), FocusMode = Control.FocusModeEnum.None };
                btn.Pressed += () => Net.I.SendEventQuestAccept(id);
                hb.AddChild(btn);
            }
            _eventQuestList.AddChild(row);
        }
        if (_eventQuestList.GetChildCount() == 0)
        {
            var e = HudStyle.Label(13); e.Text = Localization.Loc.Tr("No event quests are active.");
            _eventQuestList.AddChild(e);
        }
    }

    private void OnEventQuestAccept(int questId, bool ok)
    {
        if (ok) Net.I.SendEventQuestList();
    }

    private void OnEventQuestClaim(int questId, bool ok)
    {
        if (ok) Net.I.SendEventQuestList();
    }
}
