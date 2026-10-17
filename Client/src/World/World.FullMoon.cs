using System;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const string FullMoonCaption = "res://assets/ui/fullmoon/caption.png";
    private static readonly Vector2 FullMoonCaptionSize = new(800f, 605f);
    private static readonly Rect2 FullMoonHideToday = new(481f, 576f, 22f, 21f);
    private static readonly Rect2 FullMoonClose = new(723f, 578f, 49f, 17f);
    private static readonly Color FullMoonLineColour = new(1f, 1f, 0f);
    private static readonly Color FullMoonRefusalColour = new(1f, 0f, 0f);
    private static readonly int[] FullMoonNoAutoBoardZones = { 37, 38, 81, 82, 83, 84, 87 };

    private CanvasLayer? _fullMoonCaptionLayer;
    private Control? _fullMoonCaption;
    private double _fullMoonShoutAt = double.NegativeInfinity;
    private bool _fullMoonAutoEntry;
    private double _fullMoonAutoAt = double.NegativeInfinity;

    private void FullMoonInit()
    {
        Net.I.FullMoonEntryEvent += OnFullMoonEntry;
        Net.I.FullMoonTowerEvent += OnFullMoonTower;
        Net.I.FullMoonStageEvent += OnFullMoonStage;
        Net.I.PartyMemberEvent += OnFullMoonPartyMember;
    }

    private void FullMoonDispose()
    {
        Net.I.FullMoonEntryEvent -= OnFullMoonEntry;
        Net.I.FullMoonTowerEvent -= OnFullMoonTower;
        Net.I.FullMoonStageEvent -= OnFullMoonStage;
        Net.I.PartyMemberEvent -= OnFullMoonPartyMember;
    }

    private int PartySize => Net.I.Party.Count;

    private void OpenFullMoonBoard()
    {
        if (FullMoon.OpenRefusal(InParty, PartySize, Net.I.PartyType, _seekBoard) is { } refusal)
        {
            CombatNotice(ItemData.Text(refusal.Id, refusal.Fallback), FullMoonLineColour);
            return;
        }
        _seekBoard = FullMoon.FullMoonBoard;
        _seekPage = 0;
        _seekShown = true;
        _seekPanel.Visible = true;
        ApplySeekBoard();
        UpdateSeekStatus();
        Net.I.SendPartyBbsList(0, FullMoon.FullMoonBoard);
    }

    private void OnFullMoonPartyMember(PartyMember member)
    {
        if (member.CharId == _myId || !AmLeader || PartySize != FullMoon.PartySize
            || Net.I.PartyType != FullMoon.FullMoonBoard || Array.IndexOf(FullMoonNoAutoBoardZones, _zone) >= 0)
            return;
        OpenFullMoonBoard();
    }

    private int FullMoonAutoPick() =>
        PartyAutoEntry.Pick(_seekEntries, _zone, Net.I.LastEnter.Class, Sheet.Level);

    private void StartFullMoonAutoEntry()
    {
        if (InParty || _fullMoonAutoEntry) return;
        if (FullMoonAutoPick() < 0)
        {
            CombatNotice(ItemData.Text(PartyAutoEntry.NoneText, "There are no parties you can enter automatically."), FullMoonLineColour);
            return;
        }
        _fullMoonAutoEntry = true;
        _fullMoonAutoAt = double.NegativeInfinity;
    }

    private void FullMoonAutoTick(double now)
    {
        if (!_fullMoonAutoEntry) return;
        if (InParty || !_seekShown)
        {
            _fullMoonAutoEntry = false;
            return;
        }
        if (now - _fullMoonAutoAt < PartyAutoEntry.RetrySeconds) return;
        _fullMoonAutoAt = now;
        CombatNotice(ItemData.Text(PartyAutoEntry.SearchingText, "Searching for a party"), FullMoonLineColour);
        int pick = FullMoonAutoPick();
        if (pick < 0) return;
        string leader = _seekEntries[pick].Name;
        Net.I.SendPartyBbsInvite(leader, SeekBoard);
        CombatNotice(ItemData.Text(PartyAutoEntry.AppliedText, "Sent invitation to join the party to %s.").Replace("%s", leader), FullMoonLineColour);
    }

    private void EnterFullMoon()
    {
        if (InParty && !AmLeader) return;
        Net.I.SendFullMoonEnter();
    }

    private void ShoutFullMoonParty()
    {
        if (!InParty || !AmLeader || PartySize < FullMoon.PartySize) return;
        double left = _fullMoonShoutAt + FullMoon.ShoutCooldownSeconds - Now();
        if (left > 0)
        {
            CombatNotice(TextTemplate.Fill(ItemData.Text(FullMoon.CooldownText, "It is available in %d seconds"), (int)left + 1),
                FullMoonLineColour);
            return;
        }
        _fullMoonShoutAt = Now();
        Net.I.SendFullMoonShout(ItemData.Text(FullMoon.ShoutText, "%s is searching for Full Moon Rift Party")
            .Replace("%s", Net.I.LastEnter.Name));
    }

    private void OnFullMoonEntry(byte code)
    {
        var text = FullMoon.EntryText(code);
        CombatNotice(ItemData.Text(text.Id, text.Fallback), FullMoonRefusalColour);
        _seekShown = false;
        _seekPanel.Visible = false;
    }

    private void OnFullMoonTower(byte remaining)
    {
        var line = FullMoon.TowerText(remaining);
        CombatNotice(TextTemplate.Fill(ItemData.Text(line.Id, line.Fallback), Array.ConvertAll(line.Args, a => (object)a)),
            FullMoonLineColour);
    }

    private void OnFullMoonStage(byte total, byte current)
    {
        var line = FullMoon.StageLine(total, current);
        CombatNotice(TextTemplate.Fill(ItemData.Text(line.Id, line.Fallback), Array.ConvertAll(line.Args, a => (object)a)),
            FullMoonLineColour);
    }

    private void ShowFullMoonCaptionOnEntry()
    {
        if (_zone != FullMoon.Zone || !ResourceLoader.Exists(FullMoonCaption) || TimedItems.HiddenToday(Config.FullMoonCaptionHiddenDay, DateTime.Now)) return;
        QueueEntryPopup(EntryPopup.ZoneCaption, () =>
        {
            _fullMoonCaption ??= BuildFullMoonCaption();
            if (_fullMoonCaption == null) return null;
            _fullMoonCaption.Visible = true;
            return _fullMoonCaption;
        });
    }

    private Control? BuildFullMoonCaption()
    {
        if (!ResourceLoader.Exists(FullMoonCaption)) return null;

        _fullMoonCaptionLayer = new CanvasLayer { Layer = 74 };
        AddChild(_fullMoonCaptionLayer);
        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _fullMoonCaptionLayer.AddChild(centre);
        var picture = new TextureRect
        {
            Texture = GD.Load<Texture2D>(FullMoonCaption),
            CustomMinimumSize = FullMoonCaptionSize,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
        };
        centre.AddChild(picture);
        picture.AddChild(FullMoonHotspot(FullMoonHideToday, "Do not show for a day", () =>
        {
            Config.SetFullMoonCaptionHiddenDay(TimedItems.DayKey(DateTime.Now));
            centre.Visible = false;
        }));
        picture.AddChild(FullMoonHotspot(FullMoonClose, "Close", () => centre.Visible = false));
        return centre;
    }

    private static Button FullMoonHotspot(Rect2 area, string tip, Action pressed)
    {
        var button = new Button { Flat = true, Position = area.Position, Size = area.Size, TooltipText = tip, FocusMode = Control.FocusModeEnum.None };
        foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
            button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        button.Pressed += pressed;
        return button;
    }
}
