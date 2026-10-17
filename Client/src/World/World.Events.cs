using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const double EventsRefreshSeconds = 300;
    private const double EventsTickSeconds = 1;
    private const double EventsRebuildSeconds = 60;
    private static readonly TimeSpan EventsAlertLead = TimeSpan.FromMinutes(20);
    private const float EventsPulseSpeed = 4f;
    private const float EventsPulseLow = 0.35f;
    private const int EventsListWidth = 640;
    private const int EventsListHeight = 470;
    private const int EventsCardGap = 6;
    private const int EventsTileSize = 34;
    private const int EventsIconSize = 18;
    private const int EventsChipIconSize = 12;
    private const int EventsTopLevel = 83;
    private const int EventsWeekdayCount = 7;
    private const int MinutesPerHourClock = 60;
    private const int HoursPerDayClock = 24;
    private static readonly string[] EventsWeekdays = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

    private Button _eventsIcon = null!;
    private TextureRect _eventsImage = null!;
    private CanvasLayer _eventsLayer = null!;
    private HudWindow _eventsPanel = null!;
    private VBoxContainer _eventsCards = null!;
    private Label _eventsEmpty = null!, _eventsShift = null!;
    private Button _eventsTabEvents = null!, _eventsTabRaces = null!;
    private Button _eventsMyLevel = null!;
    private ScrollContainer _eventsScroll = null!;
    private readonly List<(EventCard Card, Label Status)> _eventsStatus = new();
    private EventSchedule? _eventSchedule;
    private readonly ShownRebuild _eventsRebuild = new();
    private double _eventsRefreshIn, _eventsTickIn, _eventsRebuildIn;
    private bool _eventsAlert;

    private void EventsInit()
    {
        _eventsIcon = TopIconButton(_topIconLayer, "system/clock", "Events", ToggleEvents, out _eventsImage);
        _eventsIcon.Resized += PlaceTopIcons;
        BuildEventsWindow();
        Net.I.EventScheduleEvent += OnEventSchedule;
        _eventsRefreshIn = 0;
    }

    private void EventsDispose()
    {
        Net.I.EventScheduleEvent -= OnEventSchedule;
    }

    private void BuildEventsWindow()
    {
        _eventsLayer = new CanvasLayer { Layer = 62 };
        AddChild(_eventsLayer);
        _eventsPanel = new HudWindow("events", "Events", bodyMinWidth: EventsListWidth, titleIcon: UiIcons.Get("system/clock"))
        {
            Visible = false,
        };
        _eventsPanel.Closed += () => _eventsPanel.Visible = false;
        _eventsPanel.VisibilityChanged += () =>
        {
            if (_eventsPanel.Visible && _eventsRebuild.Shown()) RefreshEventsWindow();
        };
        _eventsLayer.AddChild(_eventsPanel);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        _eventsPanel.Body.AddChild(body);

        body.AddChild(BuildEventsFilters());
        _eventsShift = UiTheme.Text("", 11, UiTheme.Warning);
        _eventsShift.Visible = false;
        body.AddChild(_eventsShift);

        var scroll = _eventsScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(EventsListWidth, EventsListHeight),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        UiTheme.ThinScrollbar(scroll.GetVScrollBar());
        UiTheme.ThinScrollbarThumb(scroll.GetVScrollBar(), true);
        body.AddChild(scroll);
        var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        UiTheme.Margins(margin, 0, 0, 8, 4);
        scroll.AddChild(margin);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", EventsCardGap);
        margin.AddChild(list);
        _eventsCards = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _eventsCards.AddThemeConstantOverride("separation", EventsCardGap);
        list.AddChild(_eventsCards);
        _eventsEmpty = UiTheme.Text("Loading the schedule...", 12, UiTheme.TextLo, HorizontalAlignment.Center);
        list.AddChild(_eventsEmpty);
        _eventsTabEvents.ButtonPressed = true;
    }

    private Control BuildEventsFilters()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var group = new ButtonGroup();
        _eventsTabEvents = UiTheme.UnderlineTabButton("Events", 12);
        _eventsTabRaces = UiTheme.UnderlineTabButton("Collection Races", 12);
        foreach (var tab in new[] { _eventsTabEvents, _eventsTabRaces })
        {
            tab.ButtonGroup = group;
            tab.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            tab.Toggled += on => { if (on) RefreshEventsWindow(); };
            row.AddChild(tab);
        }
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _eventsMyLevel = UiTheme.UnderlineTabButton("Only my level", 12);
        _eventsMyLevel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        _eventsMyLevel.Toggled += _ => RefreshEventsWindow();
        row.AddChild(_eventsMyLevel);
        return row;
    }

    private void ToggleEvents()
    {
        bool open = !_eventsPanel.Visible;
        if (open) _eventsRebuild.Rebuilt();
        _eventsPanel.Visible = open;
        if (!open) return;
        RefreshEventsWindow();
        Net.I.SendEventScheduleRequest();
    }

    private void OnEventSchedule(EventSchedule schedule)
    {
        _eventSchedule = schedule;
        if (_eventsRebuild.Changed(IsInstanceValid(_eventsPanel) && _eventsPanel.Visible)) RefreshEventsWindow();
        UpdateEventsAlert();
    }

    private void EventsTick(double delta)
    {
        if (_eventsPanel == null || !IsInstanceValid(_eventsPanel)) return;
        _eventsRefreshIn -= delta;
        if (_eventsRefreshIn <= 0)
        {
            _eventsRefreshIn = EventsRefreshSeconds;
            Net.I.SendEventScheduleRequest();
        }

        _eventsTickIn -= delta;
        if (_eventsTickIn <= 0)
        {
            _eventsTickIn = EventsTickSeconds;
            UpdateEventsAlert();
            if (_eventsPanel.Visible) TickEventClocks();
        }

        _eventsRebuildIn -= delta;
        if (_eventsRebuildIn <= 0)
        {
            _eventsRebuildIn = EventsRebuildSeconds;
            if (_eventsPanel.Visible) RefreshEventsWindow();
        }

        if (!IsInstanceValid(_eventsImage)) return;
        float alpha = _eventsAlert
            ? Mathf.Lerp(EventsPulseLow, 1f, 0.5f + 0.5f * Mathf.Sin((float)Now() * EventsPulseSpeed))
            : 1f;
        var tint = TopIconColor(_eventsAlert);
        _eventsImage.SelfModulate = new Color(tint, alpha);
    }

    private void UpdateEventsAlert() =>
        _eventsAlert = _eventSchedule?.AlertDue(Sheet.Level, DateTime.UtcNow, EventsAlertLead) == true;

    private void RefreshEventsWindow()
    {
        if (!IsInstanceValid(_eventsPanel)) return;
        _eventsRebuild.Rebuilt();
        foreach (Node child in _eventsCards.GetChildren()) child.QueueFree();
        _eventsStatus.Clear();

        var schedule = _eventSchedule;
        var utcNow = DateTime.UtcNow;
        int shown = 0;
        if (schedule != null)
        {
            bool races = _eventsTabRaces.ButtonPressed;
            foreach (var card in schedule.Cards(utcNow))
            {
                if ((card.Kind == EventKind.CollectionRace) != races) continue;
                if (_eventsMyLevel.ButtonPressed && !EventSchedule.Fits(Sheet.Level, card.MinLevel, card.MaxLevel)) continue;
                _eventsCards.AddChild(BuildEventCard(card, utcNow));
                shown++;
            }
        }
        _eventsEmpty.Visible = shown == 0;
        _eventsShift.Visible = schedule is { ClockOffset.Ticks: not 0 };
        if (schedule != null)
            _eventsShift.Text = $"A game master moved the event clock ({ClockShift(schedule.ClockOffset)}).";
        _eventsEmpty.Text = schedule == null ? "Loading the schedule..." : "Nothing to show here.";
        _eventsRebuildIn = EventsRebuildSeconds;
        TickEventClocks();
        Callable.From(() => { if (IsInstanceValid(_eventsPanel)) _eventsPanel.ResetSize(); }).CallDeferred();
    }

    private Control BuildEventCard(EventCard card, DateTime utcNow)
    {
        bool fits = EventSchedule.Fits(Sheet.Level, card.MinLevel, card.MaxLevel);
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", EventCardStyle(card.Live != null));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        panel.AddChild(column);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 10);
        column.AddChild(top);
        top.AddChild(EventsIconTile(EventIcon(card.Kind)));

        var titles = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 1);
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 8);
        var name = UiTheme.Text(card.Name, 14, fits ? UiTheme.TextHi : UiTheme.TextLo);
        name.AddThemeFontOverride("font", UiTheme.Strong);
        nameRow.AddChild(name);
        var band = UiTheme.Pill(LevelBand(card.MinLevel, card.MaxLevel), fits ? UiTheme.GoldBright : UiTheme.Bad, 11);
        band.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        nameRow.AddChild(band);
        titles.AddChild(nameRow);
        if (card.Description.Length > 0)
            titles.AddChild(UiTheme.Text(card.Description, 11, UiTheme.TextLo));
        top.AddChild(titles);

        var side = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        side.AddThemeConstantOverride("separation", 4);
        var status = UiTheme.Text("", 12, UiTheme.GoldBright, HorizontalAlignment.Right);
        side.AddChild(status);
        if (card.Live is { } live && NowAction(live) is { } action)
        {
            action.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
            side.AddChild(action);
        }
        top.AddChild(side);
        _eventsStatus.Add((card, status));

        if (card.Event is { Slots.Count: > 0 } scheduled)
        {
            var chips = new HFlowContainer();
            chips.AddThemeConstantOverride("h_separation", 5);
            chips.AddThemeConstantOverride("v_separation", 6);
            int offset = LocalOffsetMinutes(utcNow);
            var clockNow = _eventSchedule!.ClockNow(utcNow);
            foreach (var slot in scheduled.Slots.OrderBy(s => LocalSortKey(s, offset)))
                chips.AddChild(EventTimeChip(slot, clockNow));
            column.AddChild(chips);
        }

        if (card.Event is { Weekly: true } weekly)
        {
            var footer = new HBoxContainer();
            footer.AddThemeConstantOverride("separation", 4);
            var days = EventSchedule.Weekdays(weekly, LocalOffsetMinutes(utcNow));
            for (int day = 0; day < EventsWeekdayCount; day++)
                footer.AddChild(EventsWeekdayPill(EventsWeekdays[day], days.Contains(day)));
            column.AddChild(footer);
        }
        return panel;
    }

    private static int LocalOffsetMinutes(DateTime utcNow) =>
        (int)Math.Round(TimeZoneInfo.Local.GetUtcOffset(utcNow).TotalMinutes);

    private static int LocalSortKey(EventSlot slot, int offsetMinutes)
    {
        var clock = EventSchedule.Shift(slot, offsetMinutes);
        int day = clock.Daily ? -1 : clock.Day;
        return (day * HoursPerDayClock + clock.Hour) * MinutesPerHourClock + clock.Minute;
    }

    private static Control EventTimeChip(EventSlot slot, DateTime clockNow)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(EventSchedule.NextStartUtc(slot, clockNow), TimeZoneInfo.Local);
        string time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        string text = slot.Day == EventSchedule.EveryDay ? time : $"{EventsWeekdays[(int)local.DayOfWeek]} {time}";

        var chip = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Pass,
            TooltipText = $"Next: {local.ToString("ddd d MMM, HH:mm", CultureInfo.InvariantCulture)} (your time)",
        };
        chip.AddThemeStyleboxOverride("panel", EventChipStyle());
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        chip.AddChild(row);
        row.AddChild(EventsIcon("system/clock", EventsChipIconSize, UiTheme.TextLo));
        row.AddChild(UiTheme.Text(text, 12, UiTheme.TextHi));
        return chip;
    }

    private static Control EventsWeekdayPill(string day, bool active)
    {
        var pill = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = UiTheme.Chip();
        if (active)
        {
            style.BgColor = new Color(UiTheme.GoldDark, 0.55f);
            style.BorderColor = UiTheme.Gold;
        }
        style.ContentMarginLeft = style.ContentMarginRight = 8;
        pill.AddThemeStyleboxOverride("panel", style);
        pill.AddChild(UiTheme.Text(day, 11, active ? UiTheme.GoldBright : UiTheme.TextDim, HorizontalAlignment.Center));
        return pill;
    }

    private static StyleBoxFlat EventCardStyle(bool live)
    {
        var style = UiTheme.Inset(4);
        style.SetContentMarginAll(10);
        if (live) style.BorderColor = new Color(UiTheme.Gold, 0.8f);
        return style;
    }

    private static StyleBoxFlat EventChipStyle()
    {
        var style = UiTheme.Row();
        style.SetCornerRadiusAll(4);
        style.ContentMarginLeft = style.ContentMarginRight = 7;
        style.ContentMarginTop = style.ContentMarginBottom = 3;
        return style;
    }

    private static Control EventsIconTile(string icon)
    {
        var tile = new PanelContainer
        {
            CustomMinimumSize = new Vector2(EventsTileSize, EventsTileSize),
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        tile.AddThemeStyleboxOverride("panel", UiTheme.Row());
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.AddChild(EventsIcon(icon, EventsIconSize, UiTheme.Gold));
        tile.AddChild(center);
        return tile;
    }

    private static TextureRect EventsIcon(string icon, int size, Color tint) => new()
    {
        Texture = UiIcons.Get(icon),
        CustomMinimumSize = new Vector2(size, size),
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        SelfModulate = tint,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    private static string EventIcon(EventKind kind) => kind switch
    {
        EventKind.Temple => "system/combat-attack",
        EventKind.CollectionRace => "system/trophy",
        EventKind.Lottery => "system/ticket",
        EventKind.Krowaz => "system/key",
        EventKind.Bifrost => "system/sparkle",
        EventKind.CastleSiege => "system/combat-defence",
        _ => "system/clock",
    };

    private void TickEventClocks()
    {
        if (_eventSchedule is not { } schedule) return;
        var utcNow = DateTime.UtcNow;
        foreach (var (card, status) in _eventsStatus)
        {
            if (!IsInstanceValid(status)) continue;
            if (card.Live is { } live)
            {
                string left = Countdown(TimeSpan.FromSeconds(schedule.SecondsLeft(live, utcNow)));
                bool signUp = live.Phase == EventPhase.SignUp;
                status.Text = signUp ? $"Sign-up · {left}" : $"Running · {left} left";
                status.AddThemeColorOverride("font_color", signUp ? UiTheme.Good : UiTheme.Neutral);
            }
            else if (card.Event is { } scheduled)
            {
                status.Text = $"Starts in {StartsIn(schedule.UntilStart(scheduled, utcNow))}";
                status.AddThemeColorOverride("font_color", UiTheme.GoldBright);
            }
        }
    }

    private static string Countdown(TimeSpan left) =>
        left.TotalHours >= 1 ? left.ToString(@"h\:mm\:ss") : left.ToString(@"mm\:ss");

    private static string ClockShift(TimeSpan offset)
    {
        string sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        return $"{sign}{(int)abs.TotalHours}h {abs.Minutes:00}m";
    }

    private static string StartsIn(TimeSpan span)
    {
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
        return $"{Math.Max(1, (int)Math.Ceiling(span.TotalMinutes))}m";
    }

    private static string LevelBand(int minLevel, int maxLevel) =>
        minLevel <= 1 && (maxLevel <= 0 || maxLevel >= EventsTopLevel)
            ? "All levels"
            : $"Lv {Math.Max(1, minLevel)}-{(maxLevel > 0 ? maxLevel : EventsTopLevel)}";

    private Button? NowAction(EventNow entry)
    {
        Action? act = entry switch
        {
            { Kind: EventKind.Temple, Phase: EventPhase.SignUp } => () => ShowSignUp((TempleEventType)entry.Code),
            { Kind: EventKind.Lottery } => ToggleLottery,
            _ => null,
        };
        if (act == null) return null;
        var button = UiTheme.SmallButton(entry.Kind == EventKind.Lottery ? "Open" : "Join", entry.Name);
        button.Pressed += () => act();
        return button;
    }
}
