using System.Globalization;
using Godot;

namespace LibreKO.Features.Windows;

internal sealed class EventsFeature : IFeature, ITickFeature, IEscapable
{
    private readonly IWorldContext context;
    private readonly EventScheduleSession eventSchedule;
    private readonly SelfSession self;
    private readonly IEventScheduleRequests eventScheduleRequests;
    private readonly BifrostFeature bifrost;
    private readonly LotteryFeature lottery;
    private readonly TopIconsFeature topIcons;

    public EventsFeature(IWorldContext context, EventScheduleSession eventSchedule, SelfSession self, IEventScheduleRequests eventScheduleRequests, BifrostFeature bifrost, LotteryFeature lottery, TopIconsFeature topIcons)
    {
        this.context = context;
        this.eventSchedule = eventSchedule;
        this.self = self;
        this.eventScheduleRequests = eventScheduleRequests;
        this.bifrost = bifrost;
        this.lottery = lottery;
        this.topIcons = topIcons;
    }

    public void Tick(double delta, double nowSec)
    {
        EventsTick(delta);
    }

    private const double RefreshSeconds = 300;
    private const double TickSeconds = 1;
    private const double RebuildSeconds = 60;
    private static readonly TimeSpan AlertLead = TimeSpan.FromMinutes(20);
    private const float PulseSpeed = 4f;
    private const float PulseLow = 0.35f;
    private const int ListWidth = 640;
    private const int ListHeight = 470;
    private const int CardGap = 6;
    private const int TileSize = 34;
    private const int IconSize = 18;
    private const int ChipIconSize = 12;
    private const int WeekdayCount = 7;
    private const int MinutesPerHourClock = 60;
    private const int HoursPerDayClock = 24;

    private static readonly string[] Weekdays = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

    private Button iconButton = null!;
    private TextureRect image = null!;
    private CanvasLayer layer = null!;
    internal HudWindow Window { get; private set; } = null!;
    private VBoxContainer cards = null!;
    private Label empty = null!;
    private Label shift = null!;
    private Button tab = null!;
    internal Button TabRaces { get; private set; } = null!;
    private Button myLevel = null!;
    internal ScrollContainer Scroll { get; private set; } = null!;

    private readonly List<(EventCard Card, Label Status)> eventsStatus = new();
    internal EventSchedule? Schedule { get; set; }
    private readonly ShownRebuild windowRebuild = new();
    private double refreshIn;
    private double tickIn;
    private double rebuildIn;
    private bool alert;

    public void Build()
    {
        iconButton = topIcons.Add(TopIconSlot.Events, "system/clock", "Events", Toggle, out image);
        iconButton.Resized += topIcons.PlaceAll;
        BuildWindow();
        eventSchedule.EventScheduleEvent += OnEventSchedule;
        refreshIn = 0;
    }

    bool IEscapable.CloseOnEscape()
    {
        if (Window?.Visible == true) Window.Visible = false;
        else return false;
        return true;
    }

    public void Dispose()
    {
        eventSchedule.EventScheduleEvent -= OnEventSchedule;
    }

    internal void BuildWindow()
    {
        layer = new CanvasLayer { Layer = UiLayers.EventPanels };
        context.Root.AddChild(layer);
        Window = new HudWindow("events", "Events", bodyMinWidth: ListWidth, titleIcon: UiIcons.Get("system/clock"))
        {
            Visible = false,
        };
        Window.Closed += () => Window.Visible = false;
        Window.VisibilityChanged += () =>
        {
            if (Window.Visible && windowRebuild.Shown()) RefreshWindow();
        };
        layer.AddChild(Window);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        Window.Body.AddChild(body);

        body.AddChild(BuildFilters());
        shift = UiTheme.Text("", 11, UiTheme.Warning);
        shift.Visible = false;
        body.AddChild(shift);

        var scroll = Scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(ListWidth, ListHeight),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        UiTheme.ThinScrollbar(scroll.GetVScrollBar());
        UiTheme.ThinScrollbarThumb(scroll.GetVScrollBar(), true);
        body.AddChild(scroll);
        var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        UiTheme.Margins(margin, 0, 0, 8, 4);
        scroll.AddChild(margin);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", CardGap);
        margin.AddChild(list);
        cards = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        cards.AddThemeConstantOverride("separation", CardGap);
        list.AddChild(cards);
        empty = UiTheme.Text("Loading the schedule...", 12, UiTheme.TextLo, HorizontalAlignment.Center);
        list.AddChild(empty);
        tab.ButtonPressed = true;
    }

    private Control BuildFilters()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var group = new ButtonGroup();
        tab = UiTheme.UnderlineTabButton("Events", 12);
        TabRaces = UiTheme.UnderlineTabButton("Collection Races", 12);
        foreach (var tab in new[] { tab, TabRaces })
        {
            tab.ButtonGroup = group;
            tab.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            tab.Toggled += on => { if (on) RefreshWindow(); };
            row.AddChild(tab);
        }
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        myLevel = UiTheme.UnderlineTabButton("Only my level", 12);
        myLevel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        myLevel.Toggled += _ => RefreshWindow();
        row.AddChild(myLevel);
        return row;
    }

    private void Toggle()
    {
        bool open = !Window.Visible;
        if (open) windowRebuild.Rebuilt();
        Window.Visible = open;
        if (!open) return;
        RefreshWindow();
        eventScheduleRequests.EventScheduleRequest();
    }

    private void OnEventSchedule(EventSchedule schedule)
    {
        this.Schedule = schedule;
        if (windowRebuild.Changed(GodotObject.IsInstanceValid(Window) && Window.Visible)) RefreshWindow();
        UpdateAlert();
    }

    private void EventsTick(double delta)
    {
        if (Window == null || !GodotObject.IsInstanceValid(Window)) return;
        refreshIn -= delta;
        if (refreshIn <= 0)
        {
            refreshIn = RefreshSeconds;
            eventScheduleRequests.EventScheduleRequest();
        }

        tickIn -= delta;
        if (tickIn <= 0)
        {
            tickIn = TickSeconds;
            UpdateAlert();
            if (Window.Visible) TickEventClocks();
        }

        rebuildIn -= delta;
        if (rebuildIn <= 0)
        {
            rebuildIn = RebuildSeconds;
            if (Window.Visible) RefreshWindow();
        }

        if (!GodotObject.IsInstanceValid(image)) return;
        float alpha = alert
            ? Mathf.Lerp(PulseLow, 1f, 0.5f + 0.5f * Mathf.Sin((float)context.Now * PulseSpeed))
            : 1f;
        var tint = UiKit.TopIconColor(alert);
        image.SelfModulate = new Color(tint, alpha);
    }

    private void UpdateAlert() =>
        alert = Schedule?.AlertDue(self.Sheet.Level, DateTime.UtcNow, AlertLead) == true;

    internal void RefreshWindow()
    {
        if (!GodotObject.IsInstanceValid(Window)) return;
        windowRebuild.Rebuilt();
        foreach (Node child in cards.GetChildren()) child.QueueFree();
        eventsStatus.Clear();

        var schedule = this.Schedule;
        var utcNow = DateTime.UtcNow;
        int shown = 0;
        if (schedule != null)
        {
            bool races = TabRaces.ButtonPressed;
            foreach (var card in schedule.Cards(utcNow))
            {
                if ((card.Kind == EventKind.CollectionRace) != races) continue;
                if (myLevel.ButtonPressed && !EventSchedule.Fits(self.Sheet.Level, card.MinLevel, card.MaxLevel)) continue;
                cards.AddChild(BuildEventCard(card, utcNow));
                shown++;
            }
        }
        empty.Visible = shown == 0;
        shift.Visible = schedule is { ClockOffset.Ticks: not 0 };
        if (schedule != null)
            shift.Text = $"A game master moved the event clock ({ClockShift(schedule.ClockOffset)}).";
        empty.Text = schedule == null ? "Loading the schedule..." : "Nothing to show here.";
        rebuildIn = RebuildSeconds;
        TickEventClocks();
        Callable.From(() => { if (GodotObject.IsInstanceValid(Window)) Window.ResetSize(); }).CallDeferred();
    }

    private Control BuildEventCard(EventCard card, DateTime utcNow)
    {
        bool fits = EventSchedule.Fits(self.Sheet.Level, card.MinLevel, card.MaxLevel);
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", EventCardStyle(card.Live != null));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        panel.AddChild(column);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 10);
        column.AddChild(top);
        top.AddChild(IconTile(EventIcon(card.Kind)));

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
        eventsStatus.Add((card, status));

        if (card.Event is { Slots.Count: > 0 } scheduled)
        {
            var chips = new HFlowContainer();
            chips.AddThemeConstantOverride("h_separation", 5);
            chips.AddThemeConstantOverride("v_separation", 6);
            int offset = LocalOffsetMinutes(utcNow);
            var clockNow = Schedule!.ClockNow(utcNow);
            foreach (var slot in scheduled.Slots.OrderBy(s => LocalSortKey(s, offset)))
                chips.AddChild(EventTimeChip(slot, clockNow));
            column.AddChild(chips);
        }

        if (card.Event is { Weekly: true } weekly)
        {
            var footer = new HBoxContainer();
            footer.AddThemeConstantOverride("separation", 4);
            var days = EventSchedule.Weekdays(weekly, LocalOffsetMinutes(utcNow));
            for (int day = 0; day < WeekdayCount; day++)
                footer.AddChild(WeekdayPill(Weekdays[day], days.Contains(day)));
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
        string text = slot.Day == EventSchedule.EveryDay ? time : $"{Weekdays[(int)local.DayOfWeek]} {time}";

        var chip = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Pass,
            TooltipText = $"Next: {local.ToString("ddd d MMM, HH:mm", CultureInfo.InvariantCulture)} (your time)",
        };
        chip.AddThemeStyleboxOverride("panel", EventChipStyle());
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        chip.AddChild(row);
        row.AddChild(Icon("system/clock", ChipIconSize, UiTheme.TextLo));
        row.AddChild(UiTheme.Text(text, 12, UiTheme.TextHi));
        return chip;
    }

    private static Control WeekdayPill(string day, bool active)
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

    private static Control IconTile(string icon)
    {
        var tile = new PanelContainer
        {
            CustomMinimumSize = new Vector2(TileSize, TileSize),
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        tile.AddThemeStyleboxOverride("panel", UiTheme.Row());
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.AddChild(Icon(icon, IconSize, UiTheme.Gold));
        tile.AddChild(center);
        return tile;
    }

    private static TextureRect Icon(string icon, int size, Color tint) => new()
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
        EventKind.Bowl => "system/coins",
        _ => "system/clock",
    };

    private void TickEventClocks()
    {
        if (this.Schedule is not { } schedule) return;
        var utcNow = DateTime.UtcNow;
        foreach (var (card, status) in eventsStatus)
        {
            if (!GodotObject.IsInstanceValid(status)) continue;
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
        minLevel <= 1 && (maxLevel <= 0 || maxLevel >= CharacterSheet.MaxLevel)
        ? "All levels"
        : $"Lv {Math.Max(1, minLevel)}-{(maxLevel > 0 ? maxLevel : CharacterSheet.MaxLevel)}";

    private Button? NowAction(EventNow entry)
    {
        Action? act = entry switch
        {
            { Kind: EventKind.Temple, Phase: EventPhase.SignUp } => () => bifrost.ShowSignUp((TempleEventType)entry.Code),
            { Kind: EventKind.Lottery } => lottery.Toggle,
            _ => null,
        };
        if (act == null) return null;
        var button = UiTheme.SmallButton(entry.Kind == EventKind.Lottery ? "Open" : "Join", entry.Name);
        button.Pressed += () => act();
        return button;
    }
}
