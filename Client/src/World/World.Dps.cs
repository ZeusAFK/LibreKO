using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

// DPS damage-statistics panel, built with the same HUD components as the rest of
// the UI (HudWindow / UiTheme / HudStyle). It lists the party + yourself with a
// class marker per row.
//
// Damage sources:
//  - Self row: the client's own outgoing damage, accumulated locally from OnEntityHp
//    (the server's HP-delta report for the target you hit). Counting starts
//    automatically on your first hit.
//  - Teammate rows: the server broadcasts the party's accumulated damage table
//    (throttled ~2/s) over the GS_PARTY_DPS packet; the client fills teammate
//    rows from that table so party DPS is real, not empty.
public partial class World
{
    private sealed class DpsEntry
    {
        public int CharId;
        public string Name = "";
        public int Class;
        public long Total;        // local accumulated damage (self)
        public long ServerDamage; // damage reported by the server (teammates)
    }

    private const int DpsPanelWidth = 400;
    private const double DpsRefreshSeconds = 0.5;

    private const int DpsColClass = 40;
    private const int DpsColName = 118;
    private const int DpsColDmg = 92;
    private const int DpsColDps = 78;
    private const int DpsColPct = 96;

    private CanvasLayer _dpsLayer = null!;
    private HudWindow _dpsPanel = null!;
    private VBoxContainer _dpsRows = null!;
    private VBoxContainer _dpsTotals = null!;
    private Label _dpsStateLbl = null!;
    private Button _dpsResetBtn = null!;
    private readonly Dictionary<int, DpsEntry> _dps = new();
    private bool _dpsRunning;
    private double _dpsStartAt;
    private double _dpsWindowStart; // game-relative second when the party window began
    private Godot.Timer _dpsTimer = null!;
    private bool _dpsShown;

    private void DpsInit()
    {
        Net.I.PartyDpsEvent += OnPartyDps;

        _dpsLayer = new CanvasLayer { Layer = 75 };
        AddChild(_dpsLayer);

        _dpsPanel = new HudWindow("dps", Localization.Loc.Tr("DPS Damage"), new Vector2(560, 150),
            bodyMinWidth: DpsPanelWidth)
        { Visible = false };
        _dpsPanel.Closed += CloseDps;
        _dpsLayer.AddChild(_dpsPanel);

        var root = _dpsPanel.Body;
        root.AddThemeConstantOverride("separation", 5);

        // --- status row: state label + reset ---
        var stateRow = new HBoxContainer();
        stateRow.AddThemeConstantOverride("separation", 8);
        root.AddChild(stateRow);

        _dpsStateLbl = HudStyle.Label(12);
        _dpsStateLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        stateRow.AddChild(_dpsStateLbl);

        _dpsResetBtn = UiTheme.SmallButton(Localization.Loc.Tr("Reset"), Localization.Loc.Tr("Clear all counters"));
        _dpsResetBtn.Pressed += () => ResetDps(true);
        stateRow.AddChild(_dpsResetBtn);

        // --- column header ---
        root.AddChild(BuildDpsHeader());

        // --- rows ---
        _dpsRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _dpsRows.AddThemeConstantOverride("separation", 3);
        root.AddChild(_dpsRows);

        // --- totals ---
        _dpsTotals = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _dpsTotals.AddThemeConstantOverride("separation", 4);
        root.AddChild(_dpsTotals);

        _dpsTimer = new Godot.Timer { WaitTime = DpsRefreshSeconds, Autostart = true };
        _dpsTimer.Timeout += RefreshDpsUi;
        AddChild(_dpsTimer);

        ResetDps(false);
        RefreshDpsUi();
    }

    private void DpsDispose()
    {
        Net.I.PartyDpsEvent -= OnPartyDps;
        if (_dpsTimer != null && GodotObject.IsInstanceValid(_dpsTimer)) _dpsTimer.QueueFree();
        _dpsTimer = null!;
    }

    // Server broadcasts the party's accumulated damage table.
    private void OnPartyDps(long windowStartUnix, List<(int CharId, long Damage)> entries)
    {
        // The server's windowStart is a wall-clock unix second; convert it to the
        // game-relative clock used by Now() so elapsed windows stay comparable.
        double nowUnix = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _dpsWindowStart = Now() - (nowUnix - windowStartUnix);

        foreach (var (charId, damage) in entries)
        {
            if (charId == _myId)
                continue; // self row is fed locally for real-time accuracy
            EnsureDpsEntry(charId).ServerDamage = damage;
        }
        RefreshDpsUi();
    }

    private void ToggleDps()
    {
        if (_dpsShown) { CloseDps(); return; }
        _dpsPanel.Visible = true;
        _dpsShown = true;
        RefreshDpsUi();
    }

    private void CloseDps()
    {
        if (!_dpsShown) return;
        _dpsShown = false;
        _dpsPanel.Visible = false;
    }

    private void ResetDps(bool announce)
    {
        _dpsRunning = false;
        _dpsStartAt = Now();
        _dpsWindowStart = 0;
        _dps.Clear();
        if (announce) CombatNotice(Localization.Loc.Tr("Damage statistics reset."));
        RefreshDpsUi();
    }

    // Hooks into OnEntityHp: the server reports the HP delta of the target you hit,
    // which is the client's only reliable outgoing-damage stream. Counting starts
    // automatically on your first hit.
    private void DpsNoteSelf(long amount)
    {
        if (amount <= 0) return;
        if (!_dpsRunning) { _dpsRunning = true; _dpsStartAt = Now(); }
        EnsureDpsEntry(_myId).Total += amount;
    }

    private DpsEntry EnsureDpsEntry(int charId)
    {
        if (!_dps.TryGetValue(charId, out var e))
        {
            e = new DpsEntry { CharId = charId };
            if (charId == _myId)
            {
                e.Name = Net.I.LastEnter.Name;
                e.Class = _selfClass;
            }
            _dps[charId] = e;
        }
        return e;
    }

    private static long DisplayDamage(DpsEntry e, bool isSelf) =>
        isSelf ? e.Total : e.ServerDamage;

    private void RefreshDpsUi()
    {
        if (_dpsPanel == null || !GodotObject.IsInstanceValid(_dpsPanel)) return;

        // keep the roster in sync with the party + self
        EnsureDpsEntry(_myId);
        foreach (var m in PartyMembers)
        {
            var e = EnsureDpsEntry(m.CharId);
            if (e.Name.Length == 0) e.Name = m.Name;
            if (e.Class == 0) e.Class = m.Class;
        }

        double elapsed = _dpsRunning ? Mathf.Max(0.0, Now() - _dpsStartAt) : 0.0;
        _dpsStateLbl.Text = _dpsRunning
            ? string.Format("{0} · {1}", Localization.Loc.Tr("Counting"), FormatDpsTime(elapsed))
            : Localization.Loc.Tr("Not counting — auto-starts on your first hit.");

        long totalAll = 0;
        foreach (var e in _dps.Values)
            totalAll += DisplayDamage(e, e.CharId == _myId);

        foreach (Node child in _dpsRows.GetChildren())
        {
            _dpsRows.RemoveChild(child);
            child.QueueFree();
        }

        var order = new List<DpsEntry>(_dps.Values);
        order.Sort((a, b) =>
        {
            if (a.CharId == _myId) return -1;
            if (b.CharId == _myId) return 1;
            return DisplayDamage(b, false).CompareTo(DisplayDamage(a, false));
        });

        foreach (var e in order)
            _dpsRows.AddChild(BuildDpsRow(e, totalAll, isSelf: e.CharId == _myId));

        foreach (Node child in _dpsTotals.GetChildren())
        {
            _dpsTotals.RemoveChild(child);
            child.QueueFree();
        }
        _dpsTotals.AddChild(BuildDpsTotals(totalAll, elapsed));
    }

    private Control BuildDpsHeader()
    {
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 6);

        var classCol = UiTheme.Text(Localization.Loc.Tr("Class"), 11, UiTheme.Gold, HorizontalAlignment.Center);
        classCol.CustomMinimumSize = new Vector2(DpsColClass, 0);
        head.AddChild(classCol);

        var nameCol = UiTheme.Text(Localization.Loc.Tr("Player"), 11, UiTheme.Gold, HorizontalAlignment.Left);
        nameCol.CustomMinimumSize = new Vector2(DpsColName, 0);
        head.AddChild(nameCol);

        var dmgCol = UiTheme.Text(Localization.Loc.Tr("Damage"), 11, UiTheme.Gold, HorizontalAlignment.Right);
        dmgCol.CustomMinimumSize = new Vector2(DpsColDmg, 0);
        head.AddChild(dmgCol);

        var dpsCol = UiTheme.Text(Localization.Loc.Tr("DPS"), 11, UiTheme.Gold, HorizontalAlignment.Right);
        dpsCol.CustomMinimumSize = new Vector2(DpsColDps, 0);
        head.AddChild(dpsCol);

        var shareCol = UiTheme.Text(Localization.Loc.Tr("Share"), 11, UiTheme.Gold, HorizontalAlignment.Right);
        shareCol.CustomMinimumSize = new Vector2(DpsColPct, 0);
        head.AddChild(shareCol);
        return head;
    }

    private Control BuildDpsRow(DpsEntry e, long totalAll, bool isSelf)
    {
        long damage = DisplayDamage(e, isSelf);
        double rowElapsed = isSelf
            ? (_dpsRunning ? Mathf.Max(0.0, Now() - _dpsStartAt) : 0.0)
            : (_dpsWindowStart > 0 ? Mathf.Max(0.0, Now() - _dpsWindowStart) : 0.0);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        // class marker badge (coloured round tile with a one-character class mark)
        var badge = UiTheme.IconTile(ClassMark(e.Class), ClassName(e.Class), new Vector2(DpsColClass, 30));
        badge.AddThemeStyleboxOverride("panel", DpsBadgeStyle(e.Class, isSelf));
        foreach (var child in badge.GetChildren())
            if (child is Label l) l.AddThemeColorOverride("font_color", ClassTint(e.Class));
        row.AddChild(badge);

        // player name
        string label = e.Name.Length > 0 ? e.Name : Localization.Loc.Tr("—");
        if (isSelf) label += Localization.Loc.Tr("  (you)");
        var name = UiTheme.Text(label, 12, isSelf ? UiTheme.Self : UiTheme.TextHi);
        name.CustomMinimumSize = new Vector2(DpsColName, 0);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(name);

        // damage
        var dmg = UiTheme.Text($"{damage:N0}", 12, isSelf ? UiTheme.GoldBright : UiTheme.TextHi, HorizontalAlignment.Right);
        dmg.CustomMinimumSize = new Vector2(DpsColDmg, 0);
        row.AddChild(dmg);

        // dps
        double dps = rowElapsed > 0.1 ? damage / rowElapsed : 0.0;
        var dpsLbl = UiTheme.Text($"{dps:N0}", 12, UiTheme.TextHi, HorizontalAlignment.Right);
        dpsLbl.CustomMinimumSize = new Vector2(DpsColDps, 0);
        row.AddChild(dpsLbl);

        // share bar + percentage
        row.AddChild(BuildDpsShare(damage, totalAll, DpsColPct));
        return row;
    }

    private Control BuildDpsShare(long value, long total, float width)
    {
        var cell = new HBoxContainer { CustomMinimumSize = new Vector2(width, 0) };
        cell.AddThemeConstantOverride("separation", 5);
        cell.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        float pct = total > 0 ? (float)value / total * 100f : 0f;
        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = pct,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 14),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        var track = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.45f) };
        track.SetCornerRadiusAll(3);
        var fill = new StyleBoxFlat { BgColor = UiTheme.Gold };
        fill.SetCornerRadiusAll(3);
        bar.AddThemeStyleboxOverride("background", track);
        bar.AddThemeStyleboxOverride("fill", fill);
        cell.AddChild(bar);

        var pctLbl = UiTheme.Text($"{pct:0.#}%", 11, UiTheme.TextLo, HorizontalAlignment.Right);
        pctLbl.CustomMinimumSize = new Vector2(44, 0);
        cell.AddChild(pctLbl);
        return cell;
    }

    private Control BuildDpsTotals(long totalAll, double elapsed)
    {
        var section = UiTheme.Section();
        section.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 8);
        section.AddChild(row);

        double totalDps = elapsed > 0.1 ? totalAll / elapsed : 0.0;
        var left = UiTheme.Text($"{Localization.Loc.Tr("Total damage")}  {totalAll:N0}", 12, UiTheme.GoldBright);
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(left);
        row.AddChild(UiTheme.Text($"{Localization.Loc.Tr("Total DPS")}  {totalDps:N0}", 12, UiTheme.GoldBright, HorizontalAlignment.Right));
        return section;
    }

    private static string FormatDpsTime(double seconds)
    {
        int s = (int)seconds;
        return $"{(s / 60):00}:{s % 60:00}";
    }

    private static string ClassMark(int cls) => CharacterClassCatalog.Family(cls) switch
    {
        1 => "浪", 2 => "弓", 3 => "法", 4 => "祭", 5 => "刺",
        _ => "?",
    };

    private static Color ClassTint(int cls) => CharacterClassCatalog.Family(cls) switch
    {
        1 => new Color("c85a4a"), // Warrior
        2 => new Color("6fb55a"), // Rogue / Archer
        3 => new Color("5a9fd8"), // Mage
        4 => new Color("e0c34a"), // Priest
        5 => new Color("a86bd0"), // Kurian / Assassin
        _ => new Color("a8a298"),
    };

    private static StyleBoxFlat DpsBadgeStyle(int cls, bool isSelf)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = isSelf ? new Color(0.13f, 0.13f, 0.18f, 0.92f) : new Color(0.07f, 0.07f, 0.09f, 0.90f),
            BorderColor = new Color(ClassTint(cls), 0.9f),
            ShadowColor = new Color(0, 0, 0, 0.30f),
            ShadowSize = 2,
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(4);
        sb.SetContentMarginAll(2);
        return sb;
    }
}
