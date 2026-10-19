using Godot;

namespace LibreKO.Features.Windows;

internal sealed class BifrostFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly BifrostSession bifrost;
    private readonly SelfSession self;
    private readonly IBifrostRequests requests;
    private readonly EventPlatesFeature eventPlates;
    private readonly MsgOutFeature msgOut;

    public BifrostFeature(IWorldContext context, BifrostSession bifrost, SelfSession self, IBifrostRequests requests, EventPlatesFeature eventPlates, MsgOutFeature msgOut)
    {
        this.context = context;
        this.bifrost = bifrost;
        this.self = self;
        this.requests = requests;
        this.eventPlates = eventPlates;
        this.msgOut = msgOut;
    }

    private CanvasLayer layer = null!;
    private PanelContainer banner = null!;
    private Label titleLbl = null!;
    private Label timerLbl = null!;
    private ProgressBar bar = null!;
    private StyleBoxFlat barFill = null!;
    private Godot.Timer tick = null!;
    private Control joinModal = null!;
    private Label joinModalTitle = null!;
    private Label joinModalTimer = null!;
    private ProgressBar joinModalBar = null!;
    private StyleBoxFlat joinModalBarFill = null!;
    private Label joinModalStatus = null!;
    private Label joinModalCount = null!;
    private Button joinModalBtn = null!;
    private Button joinModalCloseBtn = null!;
    private PanelContainer? inZoneLeaveBanner;
    private Label? inZoneLeaveTitleLbl;
    private Button? inZoneLeaveBtn;
    private ConfirmationDialog? inZoneLeaveAsk;
    private DateTime endsUtc;
    private int bannerMaxSeconds;
    private TempleEventType bannerShows;
    private TempleEventType pendingJoin;
    private readonly HashSet<TempleEventType> signUpPrompted = new();

    private readonly Dictionary<int, (int Karus, int ElMorad)> signUpCounts = new();

    private const string Title = "Bifrost";
    private const string JuraidSignUpText = "Sign up with the [Reserve Knight Captain] in your town.";
    private static readonly Color MapOfChaosLogColour = new("ff7f50");
    private const int UrgentSecs = 30;
    private static readonly Color CalmCol   = new("c8a45a");
    private static readonly Color UrgentCol = new("e0574a");

    public void Build()
    {
        BuildUi();
        BuildInZoneLeaveUi();
        RefreshInZoneLeaveUi();

        bifrost.BifrostTimeEvent    += OnTime;
        bifrost.BifrostJoinEvent    += OnJoinResult;
        bifrost.BifrostDisbandEvent += OnDisband;
        bifrost.TempleSignUpsEvent  += OnTempleSignUps;

        if (context.Ready) requests.TimeRequest();
    }

    public void Dispose()
    {
        bifrost.BifrostTimeEvent    -= OnTime;
        bifrost.BifrostJoinEvent    -= OnJoinResult;
        bifrost.BifrostDisbandEvent -= OnDisband;
        bifrost.TempleSignUpsEvent  -= OnTempleSignUps;

        if (layer != null && GodotObject.IsInstanceValid(layer))
            layer.QueueFree();
        layer = null!;
        inZoneLeaveBanner = null;
        inZoneLeaveAsk = null;
    }

    internal void RequestTime() => requests.TimeRequest();

    private void BuildUi()
    {
        layer = new CanvasLayer { Layer = UiLayers.GameMenu };
        context.Root.AddChild(layer);

        BuildBanner();
        BuildJoinDialog();

        tick = new Godot.Timer { WaitTime = 1.0, Autostart = false, OneShot = false };
        tick.Timeout += OnTick;
        layer.AddChild(tick);
    }

    private void BuildBanner()
    {
        banner = new PanelContainer
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = "Click to open registration window",
        };
        banner.AddThemeStyleboxOverride("panel", UiTheme.Panel(5, true));
        layer.AddChild(banner);
        const string bifrostBannerLayoutId = "hud_bifrost";
        var bifrostLayout = HudLayout.Attach(
            banner,
            bifrostBannerLayoutId,
            banner,
            () => eventPlates.PlateSpot(banner));
        bifrostLayout.Clicked += ShowJoinPrompt;
        eventPlates.AddPlate(banner, bifrostLayout, bifrostBannerLayoutId);

        var m = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        UiTheme.Margins(m, 10, 4, 10, 5);
        banner.AddChild(m);

        var col = new VBoxContainer { CustomMinimumSize = new Vector2(160, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 2);
        m.AddChild(col);

        var head = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", 8);
        col.AddChild(head);

        titleLbl = UiTheme.Text(Title, 12, UiTheme.GoldBright);
        titleLbl.AddThemeConstantOverride("outline_size", 2);
        titleLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        titleLbl.MouseFilter = Control.MouseFilterEnum.Ignore;
        head.AddChild(titleLbl);

        timerLbl = UiTheme.Text("--:--", 12, UiTheme.TextHi, HorizontalAlignment.Right);
        timerLbl.AddThemeConstantOverride("outline_size", 2);
        timerLbl.MouseFilter = Control.MouseFilterEnum.Ignore;
        head.AddChild(timerLbl);

        bar = new ProgressBar
        {
            MinValue = 0, MaxValue = 1, Value = 1,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 3),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var track = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.45f) };
        track.SetCornerRadiusAll(2);
        barFill = new StyleBoxFlat { BgColor = CalmCol };
        barFill.SetCornerRadiusAll(2);
        bar.AddThemeStyleboxOverride("background", track);
        bar.AddThemeStyleboxOverride("fill", barFill);
        col.AddChild(bar);
    }

    private void BuildJoinDialog()
    {
        joinModal = new Control
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        joinModal.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(joinModal);

        var center = new CenterContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        joinModal.AddChild(center);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(220, 0),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Panel(6, true));
        center.AddChild(panel);

        var m = new MarginContainer();
        UiTheme.Margins(m, 10, 6, 10, 8);
        panel.AddChild(m);

        var vb = new VBoxContainer();
        vb.AddThemeConstantOverride("separation", 4);
        m.AddChild(vb);

        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 4);
        vb.AddChild(headerRow);

        joinModalTitle = UiTheme.Text(FormatModalTitle(Title), 11, UiTheme.GoldBright, HorizontalAlignment.Center);
        joinModalTitle.AddThemeConstantOverride("outline_size", 2);
        joinModalTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        headerRow.AddChild(joinModalTitle);

        var xBtn = new Button
        {
            Text = "✕",
            CustomMinimumSize = new Vector2(16, 16),
            FocusMode = Control.FocusModeEnum.None,
        };
        xBtn.AddThemeColorOverride("font_color", UiTheme.TextDim);
        xBtn.AddThemeColorOverride("font_hover_color", UiTheme.GoldBright);
        xBtn.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        xBtn.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
        xBtn.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
        xBtn.Pressed += CloseJoinModal;
        headerRow.AddChild(xBtn);

        var timerBox = new VBoxContainer();
        timerBox.AddThemeConstantOverride("separation", 3);
        vb.AddChild(timerBox);

        joinModalTimer = UiTheme.Text("--:--", 16, UiTheme.GoldBright, HorizontalAlignment.Center);
        joinModalTimer.AddThemeConstantOverride("outline_size", 2);
        timerBox.AddChild(joinModalTimer);

        joinModalBar = new ProgressBar
        {
            MinValue = 0, MaxValue = 1, Value = 1,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 3),
        };
        var modalTrack = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.45f) };
        modalTrack.SetCornerRadiusAll(2);
        joinModalBarFill = new StyleBoxFlat { BgColor = CalmCol };
        joinModalBarFill.SetCornerRadiusAll(2);
        joinModalBar.AddThemeStyleboxOverride("background", modalTrack);
        joinModalBar.AddThemeStyleboxOverride("fill", joinModalBarFill);
        timerBox.AddChild(joinModalBar);

        joinModalStatus = UiTheme.Text("Registration is OPEN! Click [Join] to participate.", 10, UiTheme.TextHi, HorizontalAlignment.Center);
        joinModalStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vb.AddChild(joinModalStatus);

        joinModalCount = UiTheme.Text("", 10, UiTheme.TextDim, HorizontalAlignment.Center);
        joinModalCount.Visible = false;
        vb.AddChild(joinModalCount);

        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 6);
        vb.AddChild(btnRow);

        joinModalBtn = Ui.MenuButton("Join", height: 24, fontSize: 10);
        joinModalBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        joinModalBtn.Pressed += OnJoinModalToggle;
        btnRow.AddChild(joinModalBtn);

        joinModalCloseBtn = Ui.MenuButton("Close", height: 24, fontSize: 10);
        joinModalCloseBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        joinModalCloseBtn.Pressed += CloseJoinModal;
        btnRow.AddChild(joinModalCloseBtn);
    }

    private TempleSignUpBoard SignUps => bifrost.SignUps;

    internal static string EventTitle(TempleEventType type) => type switch
    {
        TempleEventType.Chaos => "Chaos Dungeon",
        TempleEventType.BorderDefenseWar => "Border Defense War",
        TempleEventType.JuraidMountain => "Juraid Mountain",
        TempleEventType.UnderTheCastle => "Under The Castle",
        TempleEventType.ForgottenTemple => "Forgotten Temple",
        _ => Title,
    };

    private static TempleEventType TypeForJoinReply(int id) => id switch
    {
        TempleJoin.ChaosSignUpEventId or TempleZones.ChaosDungeon => TempleEventType.Chaos,
        TempleZones.BorderDefenseWar => TempleEventType.BorderDefenseWar,
        TempleZones.JuraidMountain => TempleEventType.JuraidMountain,
        TempleZones.UnderTheCastle => TempleEventType.UnderTheCastle,
        TempleZones.ForgottenTemple => TempleEventType.ForgottenTemple,
        _ => TempleEventType.None,
    };

    private void CloseJoinModal()
    {
        if (joinModal != null && GodotObject.IsInstanceValid(joinModal))
            joinModal.Visible = false;

        if (SignUps.AnyOpen)
            context.Log.Notice($"[{EventTitle(SignUps.Current)}] Window closed. Click the top timer banner anytime to reopen.");
    }

    private static string FormatModalTitle(string title) => $"#  {title.ToUpper()}  #";

    private void OnTime(int remaining, TempleEventType eventType)
    {
        var utcNow = DateTime.UtcNow;
        if (eventType == TempleEventType.None)
            endsUtc = remaining > 0 ? utcNow.AddSeconds(remaining) : DateTime.MinValue;
        else if (remaining <= 0)
            CloseSignUp(eventType);
        else if (SignUps.OpenSignUp(eventType, remaining, utcNow))
        {
            context.Log.Notice($"[{EventTitle(eventType)}] Registration is open ({remaining} seconds)!");
            if (SignUps.Current == eventType) OfferSignUp(eventType);
        }
        RefreshBanner();
    }

    private void CloseSignUp(TempleEventType type)
    {
        if (!SignUps.IsOpen(type)) return;
        SignUps.Close(type);
        SignUpEnded(type);
    }

    private void SignUpEnded(TempleEventType type)
    {
        signUpPrompted.Remove(type);
        signUpCounts.Remove(TempleJoin.SignUpEventId(type));
        context.Log.Notice($"[{EventTitle(type)}] Event registration has ended.");
    }

    private int SecondsLeft(DateTime utcNow) =>
        endsUtc > utcNow ? (int)Math.Ceiling((endsUtc - utcNow).TotalSeconds) : 0;

    private void RefreshBanner()
    {
        if (banner == null || !GodotObject.IsInstanceValid(banner)) return;
        var utcNow = DateTime.UtcNow;
        var shows = SignUps.Current;
        int seconds = shows != TempleEventType.None ? SignUps.SecondsLeft(shows, utcNow) : SecondsLeft(utcNow);
        if (shows != bannerShows)
        {
            bannerShows = shows;
            bannerMaxSeconds = 0;
        }

        if (seconds <= 0)
        {
            banner.Visible = false;
            if (joinModal != null && GodotObject.IsInstanceValid(joinModal)) joinModal.Visible = false;
            tick.Stop();
            return;
        }

        if (shows == TempleEventType.None && joinModal != null && GodotObject.IsInstanceValid(joinModal))
            joinModal.Visible = false;
        bannerMaxSeconds = Math.Max(bannerMaxSeconds, seconds);
        string title = EventTitle(shows);
        titleLbl.Text = title;
        if (joinModalTitle != null && GodotObject.IsInstanceValid(joinModalTitle))
            joinModalTitle.Text = FormatModalTitle(title);
        banner.Visible = true;
        if (tick.IsStopped()) tick.Start();
        UpdateBanner(seconds);
        UpdateJoinModalState();
    }

    private static readonly Color ChaosPickaxeColour = new("ff7f50");

    private bool HoldsPickaxe() =>
        ChaosBattle.IsPickaxe(ItemData.Get(self.Inventory[InventoryConstants.RightHand].ItemId)?.Kind ?? 0)
    || ChaosBattle.IsPickaxe(ItemData.Get(self.Inventory[InventoryConstants.LeftHand].ItemId)?.Kind ?? 0);

    private void OnJoinModalToggle()
    {
        var type = SignUps.Current;
        if (type == TempleEventType.None) return;
        if (SignUps.Registered == type)
        {
            requests.Disband();
            return;
        }
        if (SignUps.Registered != TempleEventType.None || type == TempleEventType.JuraidMountain) return;
        if (type == TempleEventType.Chaos && HoldsPickaxe())
        {
            msgOut.ShowSkill(ItemData.Text(ChaosBattle.PickaxeJoinText, "Disarm your Pickaxe and resubmit your request to participate."));
            context.Log.Notice(ItemData.Text(ChaosBattle.PickaxeArmedText, ""), ChaosPickaxeColour);
            return;
        }
        pendingJoin = type;
        requests.Join(type);
    }

    private void UpdateJoinModalState()
    {
        if (joinModalStatus == null || !GodotObject.IsInstanceValid(joinModalStatus)) return;
        if (joinModalBtn == null || !GodotObject.IsInstanceValid(joinModalBtn)) return;

        var type = SignUps.Current;
        bool registeredHere = type != TempleEventType.None && SignUps.Registered == type;
        bool registeredElsewhere = SignUps.Registered != TempleEventType.None && !registeredHere;
        bool npcSignUp = type == TempleEventType.JuraidMountain;
        joinModalBtn.Visible = registeredHere || (!npcSignUp && !registeredElsewhere);
        if (registeredElsewhere)
        {
            joinModalStatus.Text = $"You are registered for {EventTitle(SignUps.Registered)}.";
            joinModalStatus.AddThemeColorOverride("font_color", UiTheme.TextLo);
        }
        else if (npcSignUp && !registeredHere)
        {
            joinModalStatus.Text = JuraidSignUpText;
            joinModalStatus.AddThemeColorOverride("font_color", UiTheme.TextHi);
        }
        else if (registeredHere)
        {
            joinModalStatus.Text = "✓ Registered! You will be teleported automatically.";
            joinModalStatus.AddThemeColorOverride("font_color", UiTheme.Good);
            joinModalBtn.Text = "Cancel";
            joinModalBtn.AddThemeColorOverride("font_color", UiTheme.Bad);
        }
        else
        {
            joinModalStatus.Text = "Registration is OPEN! Click [Join] to participate.";
            joinModalStatus.AddThemeColorOverride("font_color", UiTheme.TextHi);
            joinModalBtn.Text = "Join";
            joinModalBtn.AddThemeColorOverride("font_color", UiTheme.TextHi);
        }

        if (joinModalCount == null || !GodotObject.IsInstanceValid(joinModalCount)) return;
        int eventId = TempleJoin.SignUpEventId(type);
        joinModalCount.Visible = signUpCounts.TryGetValue(eventId, out var count);
        if (joinModalCount.Visible)
            joinModalCount.Text = TempleJoin.CountLine(eventId == TempleJoin.ChaosSignUpEventId, count.Karus, count.ElMorad);
    }

    private void OnJoinResult(TempleJoinResult result, int zone)
    {
        var type = TypeForJoinReply(zone);
        if (type == TempleEventType.None)
            type = pendingJoin != TempleEventType.None ? pendingJoin : SignUps.Current;
        pendingJoin = TempleEventType.None;
        if (TempleJoin.SignedUp(result)) SignUps.SetRegistered(type);
        UpdateJoinModalState();
        if (result == TempleJoinResult.Joined)
        {
            context.Log.Notice($"[{EventTitle(type)}] You have registered! Prepare for battle.");
            return;
        }
        if (result == TempleJoinResult.NeedsMapOfChaos)
        {
            msgOut.ShowSkill(ItemData.Text(TempleJoin.MapOfChaosCentreText, ""));
            context.Log.Add(ItemData.Text(TempleJoin.TextId(result), ""), CombatLogKind.Status, MapOfChaosLogColour);
            return;
        }
        string text = result == TempleJoinResult.LevelTooHigh
            ? TempleJoin.LevelTooHighText
            : ItemData.Text(TempleJoin.TextId(result), "");
        if (text.Length > 0) context.Log.Notice(text);
    }

    private void OnTempleSignUps(int eventId, int karus, int elMorad)
    {
        signUpCounts[eventId] = (karus, elMorad);
        UpdateJoinModalState();
    }

    private void OnDisband()
    {
        var left = SignUps.Registered;
        SignUps.ClearRegistered();
        UpdateJoinModalState();
        if (TempleZones.IsTempleEventZone(context.Zone))
            context.Log.Notice($"[{ZoneCatalog.Name(context.Zone)}] You left the event.");
        else
            context.Log.Notice($"[{EventTitle(left != TempleEventType.None ? left : SignUps.Current)}] You cancelled your event registration.");
        RefreshInZoneLeaveUi();
    }

    private void OnTick()
    {
        foreach (var type in SignUps.Expire(DateTime.UtcNow)) SignUpEnded(type);
        RefreshBanner();
    }

    private void UpdateBanner(int seconds)
    {
        string timeStr = $"{seconds / 60:00}:{seconds % 60:00}";
        timerLbl.Text = timeStr;
        bool urgent = seconds <= UrgentSecs;
        if (joinModalTimer != null && GodotObject.IsInstanceValid(joinModalTimer))
        {
            joinModalTimer.Text = timeStr;
            joinModalTimer.AddThemeColorOverride("font_color", urgent ? UrgentCol : UiTheme.GoldBright);
        }

        float frac = bannerMaxSeconds > 0 ? Mathf.Clamp((float)seconds / bannerMaxSeconds, 0f, 1f) : 0f;
        bar.Value = frac;

        var col = urgent ? UrgentCol : CalmCol;
        if (urgent)
        {
            float pulse = 0.6f + 0.4f * Mathf.Sin((float)Time.GetTicksMsec() * 0.012f);
            col = new Color(UrgentCol, pulse);
            timerLbl.AddThemeColorOverride("font_color", new Color(UrgentCol, 0.6f + 0.4f * pulse));
        }
        else
        {
            timerLbl.AddThemeColorOverride("font_color", UiTheme.TextHi);
        }
        barFill.BgColor = col;
        if (joinModalBar != null && GodotObject.IsInstanceValid(joinModalBar))
        {
            joinModalBar.Value = frac;
            if (joinModalBarFill != null) joinModalBarFill.BgColor = col;
        }
    }

    private void OfferSignUp(TempleEventType type)
    {
        if (!signUpPrompted.Add(type)) return;
        ShowSignUp(type);
    }

    internal void ShowSignUp(TempleEventType type)
    {
        if (!SignUps.Select(type)) return;
        RefreshBanner();
        ShowJoinPrompt();
    }

    private void ShowJoinPrompt()
    {
        if (!SignUps.AnyOpen) return;
        if (joinModalTitle != null && GodotObject.IsInstanceValid(joinModalTitle))
            joinModalTitle.Text = FormatModalTitle(EventTitle(SignUps.Current));
        titleLbl.Text = EventTitle(SignUps.Current);
        UpdateJoinModalState();
        joinModal.Visible = true;
    }

    private void BuildInZoneLeaveUi()
    {
        if (inZoneLeaveBanner != null) return;

        const string inZoneLeaveLayoutId = "hud_event_inzone_leave";
        inZoneLeaveBanner = new PanelContainer
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.Move,
            TooltipText = "Drag to move",
        };
        inZoneLeaveBanner.AddThemeStyleboxOverride("panel", UiTheme.Panel(5, true));
        layer.AddChild(inZoneLeaveBanner);

        var layout = HudLayout.Attach(
            inZoneLeaveBanner,
            inZoneLeaveLayoutId,
            inZoneLeaveBanner,
            () => eventPlates.PlateSpot(inZoneLeaveBanner));
        eventPlates.AddPlate(inZoneLeaveBanner, layout, inZoneLeaveLayoutId);

        var m = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        UiTheme.Margins(m, 10, 4, 10, 5);
        inZoneLeaveBanner.AddChild(m);

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        m.AddChild(row);

        var grip = UiTheme.Text("⠿", 13, UiTheme.TextDim);
        grip.MouseFilter = Control.MouseFilterEnum.Ignore;
        grip.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(grip);

        inZoneLeaveTitleLbl = UiTheme.Text("", 12, UiTheme.GoldBright);
        inZoneLeaveTitleLbl.AddThemeConstantOverride("outline_size", 2);
        inZoneLeaveTitleLbl.VerticalAlignment = VerticalAlignment.Center;
        inZoneLeaveTitleLbl.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(inZoneLeaveTitleLbl);

        inZoneLeaveBtn = Ui.MenuButton("Leave", height: 22, fontSize: 10);
        inZoneLeaveBtn.CustomMinimumSize = new Vector2(55, 22);
        inZoneLeaveBtn.AddThemeColorOverride("font_color", UiTheme.Bad);
        inZoneLeaveBtn.TooltipText = "Leave event and return to Moradon";
        inZoneLeaveBtn.Pressed += OnInZoneLeavePressed;
        row.AddChild(inZoneLeaveBtn);

        inZoneLeaveAsk = new ConfirmationDialog
        {
            Title = "Leave Event",
            Exclusive = false,
        };
        inZoneLeaveAsk.GetOkButton().Text = "Leave";
        inZoneLeaveAsk.GetCancelButton().Text = "Cancel";
        inZoneLeaveAsk.Confirmed += OnInZoneLeaveConfirmed;
        layer.AddChild(inZoneLeaveAsk);
    }

    internal void OnInZoneLeavePressed()
    {
        if (inZoneLeaveAsk == null || !GodotObject.IsInstanceValid(inZoneLeaveAsk)) return;
        string eventName = ZoneCatalog.Name(context.Zone);
        inZoneLeaveAsk.DialogText = $"Do you want to leave {eventName} and return to Moradon?";
        inZoneLeaveAsk.PopupCentered();
    }

    private void OnInZoneLeaveConfirmed()
    {
        requests.Disband();
        context.Log.Notice($"[{ZoneCatalog.Name(context.Zone)}] Leaving event and returning to Moradon...");
    }

    internal void RefreshInZoneLeaveUi()
    {
        if (inZoneLeaveBanner == null || !GodotObject.IsInstanceValid(inZoneLeaveBanner)) return;
        if (TempleZones.IsTempleEventZone(context.Zone))
        {
            if (inZoneLeaveTitleLbl != null && GodotObject.IsInstanceValid(inZoneLeaveTitleLbl))
                inZoneLeaveTitleLbl.Text = ZoneCatalog.Name(context.Zone);
            inZoneLeaveBanner.Visible = true;
            eventPlates.Queue();
        }
        else
        {
            inZoneLeaveBanner.Visible = false;
            if (inZoneLeaveAsk != null && GodotObject.IsInstanceValid(inZoneLeaveAsk))
                inZoneLeaveAsk.Visible = false;
            eventPlates.Queue();
        }
    }
}
