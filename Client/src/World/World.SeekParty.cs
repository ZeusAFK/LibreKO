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

        var win = new HudWindow("seek_party", Localization.Loc.Tr("Seek Party"), new Vector2(300, 110), 420) { Visible = false };
        win.Closed += () => _seekShown = false;
        _seekLayer.AddChild(win);
        _seekPanel = win;

        var root = win.Body;
        root.AddThemeConstantOverride("separation", 6);

        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 5); root.AddChild(top);
        _seekRegisterBtn = SmallButton(Localization.Loc.Tr("Look for a party"), top);
        _seekRegisterBtn.Pressed += ToggleSeeking;
        SmallButton(Localization.Loc.Tr("Refresh"), top).Pressed += () => Net.I.SendPartyBbsList(_seekPage);

        _seekStatusLbl = HudStyle.Label(12);
        _seekStatusLbl.AddThemeColorOverride("font_color", new Color("b9c0c8"));
        root.AddChild(_seekStatusLbl);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(420, 230) };
        root.AddChild(scroll);
        _seekListBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _seekListBox.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_seekListBox);

        var nav = new HBoxContainer(); nav.AddThemeConstantOverride("separation", 6); root.AddChild(nav);
        SmallButton(Localization.Loc.Tr("◄ Prev"), nav).Pressed += () => { if (_seekPage > 0) { _seekPage--; Net.I.SendPartyBbsList(_seekPage); } };
        _seekPageLbl = HudStyle.Label(12); _seekPageLbl.Text = Localization.Loc.Tr("Page 1"); nav.AddChild(_seekPageLbl);
        SmallButton(Localization.Loc.Tr("Next ►"), nav).Pressed += () =>
        {
            if ((_seekPage + 1) * 10 < _seekTotal) { _seekPage++; Net.I.SendPartyBbsList(_seekPage); }
        };

        root.AddChild(new HSeparator());

        var wlbl = HudStyle.Label(13); wlbl.Text = Localization.Loc.Tr("Recruit (party leader only):"); root.AddChild(wlbl);
        var wrow = new HBoxContainer(); wrow.AddThemeConstantOverride("separation", 5); root.AddChild(wrow);
        _wantedClassOpt = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        _wantedClassOpt.AddThemeFontSizeOverride("font_size", 12);
        for (int i = 0; i < WantedClasses.Length; i++) _wantedClassOpt.AddItem(Localization.Loc.Tr(WantedClasses[i].Label), i);
        wrow.AddChild(_wantedClassOpt);
        _wantedMsgInput = new LineEdit { MaxLength = 60, PlaceholderText = Localization.Loc.Tr("Recruiting message"),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _wantedMsgInput.TextSubmitted += _ => PostWanted();
        wrow.AddChild(_wantedMsgInput);
        SmallButton(Localization.Loc.Tr("Post"), wrow).Pressed += PostWanted;
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
        _seekRegisterBtn.Text = _seeking ? Localization.Loc.Tr("Stop looking") : Localization.Loc.Tr("Look for a party");
        _seekStatusLbl.Text = _seeking
            ? Localization.Loc.Tr("You are listed as looking for a party.")
            : Localization.Loc.Tr("Browse players seeking a party, or list yourself.");
    }

    private void OnBbsRegister(bool ok)
    {
        if (ok) { _seeking = true; Chat.Info(Localization.Loc.Tr("You are now listed as looking for a party.")); }
        else Chat.Info(Localization.Loc.Tr("Can't seek a party while you're already in one."));
        UpdateSeekStatus();
        if (_seekShown) Net.I.SendPartyBbsList(_seekPage);
    }

    private void OnBbsDelete()
    {
        _seeking = false;
        Chat.Info(Localization.Loc.Tr("Removed your seek-party listing."));
        UpdateSeekStatus();
        if (_seekShown) Net.I.SendPartyBbsList(_seekPage);
    }

    private void OnBbsWantedFail() => Chat.Info(Localization.Loc.Tr("Only the party leader can post a recruiting message."));

    private void OnBbsList(int page, int total, List<PartyBbsEntry> entries)
    {
        _seekPage = page;
        _seekTotal = total;
        if (!_seekShown) return;

        foreach (var c in _seekListBox.GetChildren()) c.QueueFree();

        int pages = Mathf.Max(1, (total + 9) / 10);
        _seekPageLbl.Text = $"{Localization.Loc.Tr("Page")} {page + 1} / {pages}";

        if (entries.Count == 0)
        {
            var empty = HudStyle.Label(13); empty.Text = Localization.Loc.Tr("Nobody is seeking a party right now.");
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
            ? $"{e.Name}  ·  {Localization.Loc.Tr("party")} {e.MemberCount}/8  ·  {Localization.Loc.Tr("wants")} {ClassName(e.ClassOrWanted)}"
            : $"{e.Name}  ·  {Localization.Loc.Tr("Lv")} {e.Level}  {ClassName(e.ClassOrWanted)}";
        info.AddChild(head);

        var sub = HudStyle.Label(11);
        string zone = SeekZoneName(e.ZoneId);
        sub.Text = e.Message.Length > 0 ? $"[{zone}]  {e.Message}" : $"[{zone}]";
        sub.AddThemeColorOverride("font_color", new Color("b9c0c8"));
        info.AddChild(sub);

        string name = e.Name;
        var inviteBtn = SmallButton(Localization.Loc.Tr("Invite"), row);
        inviteBtn.Pressed += () =>
        {
            if (InParty) Net.I.SendPartyInvite(name); else Net.I.SendPartyCreate(name);
            Chat.Info($"{Localization.Loc.Tr("Inviting")} {name} {Localization.Loc.Tr("to your party…")}");
        };
        return panel;
    }

    private static string SeekZoneName(int zoneId)
    {
        foreach (var z in ZoneCatalog.All) if (z.Id == zoneId) return z.Name;
        return $"{Localization.Loc.Tr("Zone")} {zoneId}";
    }
}
