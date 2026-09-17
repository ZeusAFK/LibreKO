using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _seekLayer = null!;
    private Control _seekPanel = null!;
    private VBoxContainer _seekListBox = null!;
    private Label _seekPageLbl = null!, _seekStatusLbl = null!;
    private Button _seekRegisterBtn = null!;
    private OptionButton _wantedClassOpt = null!;
    private LineEdit _wantedMsgInput = null!;

    private bool _seekShown;
    private bool _seeking;
    private int _seekPage;
    private int _seekTotal;

    private static readonly (string Label, int Code)[] WantedClasses =
    {
        ("Any", 0), ("Warrior", 1), ("Rogue", 2), ("Mage", 3), ("Priest", 4),
    };

    private void BuildSeekPartyPanel()
    {
        _seekLayer = new CanvasLayer { Layer = 71 };
        AddChild(_seekLayer);

        var win = new HudWindow("seek_party", "Seek Party", new Vector2(300, 110), 420) { Visible = false };
        win.Closed += () => _seekShown = false;
        _seekLayer.AddChild(win);
        _seekPanel = win;

        var root = win.Body;
        root.AddThemeConstantOverride("separation", 6);

        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 5); root.AddChild(top);
        _seekRegisterBtn = SmallButton("Look for a party", top);
        _seekRegisterBtn.Pressed += ToggleSeeking;
        SmallButton("Refresh", top).Pressed += () => Net.I.SendPartyBbsList(_seekPage);

        _seekStatusLbl = HudStyle.Label(12);
        _seekStatusLbl.AddThemeColorOverride("font_color", new Color("b9c0c8"));
        root.AddChild(_seekStatusLbl);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(420, 230) };
        root.AddChild(scroll);
        _seekListBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _seekListBox.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_seekListBox);

        var nav = new HBoxContainer(); nav.AddThemeConstantOverride("separation", 6); root.AddChild(nav);
        SmallButton("◄ Prev", nav).Pressed += () => { if (_seekPage > 0) { _seekPage--; Net.I.SendPartyBbsList(_seekPage); } };
        _seekPageLbl = HudStyle.Label(12); _seekPageLbl.Text = "Page 1"; nav.AddChild(_seekPageLbl);
        SmallButton("Next ►", nav).Pressed += () =>
        {
            if ((_seekPage + 1) * 10 < _seekTotal) { _seekPage++; Net.I.SendPartyBbsList(_seekPage); }
        };

        root.AddChild(new HSeparator());

        var wlbl = HudStyle.Label(13); wlbl.Text = "Recruit (party leader only):"; root.AddChild(wlbl);
        var wrow = new HBoxContainer(); wrow.AddThemeConstantOverride("separation", 5); root.AddChild(wrow);
        _wantedClassOpt = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        _wantedClassOpt.AddThemeFontSizeOverride("font_size", 12);
        for (int i = 0; i < WantedClasses.Length; i++) _wantedClassOpt.AddItem(WantedClasses[i].Label, i);
        wrow.AddChild(_wantedClassOpt);
        _wantedMsgInput = new LineEdit { MaxLength = 60, PlaceholderText = "Recruiting message",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _wantedMsgInput.TextSubmitted += _ => PostWanted();
        wrow.AddChild(_wantedMsgInput);
        SmallButton("Post", wrow).Pressed += PostWanted;
    }

    private void ToggleSeekParty()
    {
        _seekShown = !_seekShown;
        _seekPanel.Visible = _seekShown;
        if (_seekShown) { Net.I.SendPartyBbsList(_seekPage); UpdateSeekStatus(); }
    }

    private void ToggleSeeking()
    {
        if (_seeking) Net.I.SendPartyBbsDelete();
        else Net.I.SendPartyBbsRegister();
    }

    private void PostWanted()
    {
        int code = WantedClasses[Mathf.Clamp(_wantedClassOpt.Selected, 0, WantedClasses.Length - 1)].Code;
        Net.I.SendPartyBbsWanted(code, _seekPage, _wantedMsgInput.Text.Trim());
    }

    private void UpdateSeekStatus()
    {
        _seekRegisterBtn.Text = _seeking ? "Stop looking" : "Look for a party";
        _seekStatusLbl.Text = _seeking
            ? "You are listed as looking for a party."
            : "Browse players seeking a party, or list yourself.";
    }

    private void OnBbsRegister(bool ok)
    {
        if (ok) { _seeking = true; Chat.Info("You are now listed as looking for a party."); }
        else Chat.Info("Can't seek a party while you're already in one.");
        UpdateSeekStatus();
        if (_seekShown) Net.I.SendPartyBbsList(_seekPage);
    }

    private void OnBbsDelete()
    {
        _seeking = false;
        Chat.Info("Removed your seek-party listing.");
        UpdateSeekStatus();
        if (_seekShown) Net.I.SendPartyBbsList(_seekPage);
    }

    private void OnBbsWantedFail() => Chat.Info("Only the party leader can post a recruiting message.");

    private void OnBbsList(int page, int total, List<PartyBbsEntry> entries)
    {
        _seekPage = page;
        _seekTotal = total;
        if (!_seekShown) return;

        foreach (var c in _seekListBox.GetChildren()) c.QueueFree();

        int pages = Mathf.Max(1, (total + 9) / 10);
        _seekPageLbl.Text = $"Page {page + 1} / {pages}";

        if (entries.Count == 0)
        {
            var empty = HudStyle.Label(13); empty.Text = "Nobody is seeking a party right now.";
            empty.AddThemeColorOverride("font_color", new Color("b9c0c8"));
            _seekListBox.AddChild(empty);
            return;
        }

        foreach (var e in entries)
            _seekListBox.AddChild(BuildSeekRow(e));
    }

    private Control BuildSeekRow(PartyBbsEntry e)
    {
        var panel = new PanelContainer();
        var sb = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.05f) };
        foreach (var s in new[] { "left", "right", "top", "bottom" }) sb.Set($"content_margin_{s}", 6f);
        sb.CornerRadiusTopLeft = sb.CornerRadiusTopRight = sb.CornerRadiusBottomLeft = sb.CornerRadiusBottomRight = 3;
        panel.AddThemeStyleboxOverride("panel", sb);

        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 8);
        panel.AddChild(row);

        var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        info.AddThemeConstantOverride("separation", 1);
        row.AddChild(info);

        var head = HudStyle.Label(14);
        head.Text = e.IsLeaderRecruiting
            ? $"{e.Name}  ·  party {e.MemberCount}/8  ·  wants {ClassName(e.ClassOrWanted)}"
            : $"{e.Name}  ·  Lv {e.Level}  {ClassName(e.ClassOrWanted)}";
        info.AddChild(head);

        var sub = HudStyle.Label(11);
        string zone = SeekZoneName(e.ZoneId);
        sub.Text = e.Message.Length > 0 ? $"[{zone}]  {e.Message}" : $"[{zone}]";
        sub.AddThemeColorOverride("font_color", new Color("b9c0c8"));
        info.AddChild(sub);

        string name = e.Name;
        var inviteBtn = SmallButton("Invite", row);
        inviteBtn.Pressed += () =>
        {
            if (InParty) Net.I.SendPartyInvite(name); else Net.I.SendPartyCreate(name);
            Chat.Info($"Inviting {name} to your party…");
        };
        return panel;
    }

    private static string SeekZoneName(int zoneId)
    {
        foreach (var z in ZoneCatalog.All) if (z.Id == zoneId) return z.Name;
        return $"Zone {zoneId}";
    }
}
