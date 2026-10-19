using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class KnightRoyaleFeature : IFeature, ITickFeature
{
    private readonly IWorldContext context;
    private readonly KnightRoyaleSession knightRoyale;
    private readonly IKnightRoyaleRequests requests;
    private readonly MapFeature map;

    public KnightRoyaleFeature(IWorldContext context, KnightRoyaleSession knightRoyale, IKnightRoyaleRequests requests, MapFeature map)
    {
        this.context = context;
        this.knightRoyale = knightRoyale;
        this.requests = requests;
        this.map = map;
    }

    public void Tick(double delta, double nowSec)
    {
        AreaBannerTick();
        KnightRoyaleTick();
    }

    private const int RoyaleBoardFontSize = 13;
    private const int RoyaleScoreFontSize = 12;
    private const float RoyalePingRadius = 6f;

    private CanvasLayer? royaleLayer;
    internal PanelContainer? RoyaleBoard { get; private set; }
    private Control? royaleBoardBody;
    private Label? royaleBoardTitle;
    internal Label? RoyaleBoardTime { get; private set; }
    private Label? royaleBoardCount;
    private Button? royaleApply;
    private Button? royaleCancel;
    internal HudWindow? RoyaleScore { get; private set; }
    internal Label? RoyaleSurvivors { get; private set; }
    internal Label? RoyaleNextGas { get; private set; }
    internal Label? RoyaleGasTime { get; private set; }
    private double royaleBoardEndsAt;
    private bool royaleBoardOpen;
    private bool royaleBoardCollapsed;
    private readonly List<(float X, float Z, double Until, bool Delegate)> royalePings = new();
    private double royaleReturnBoxAt = double.PositiveInfinity;

    private bool InRoyaleZone => KnightRoyale.IsZone(context.Zone);

    public void Build()
    {
        map.AddMiniSource(AddRoyalePings);
        knightRoyale.RoyaleSignUpEvent += OnRoyaleSignUp;
        knightRoyale.RoyaleEntryEvent += OnRoyaleEntry;
        knightRoyale.RoyaleAbandonedEvent += OnRoyaleAbandoned;
        knightRoyale.RoyaleGasEvent += OnRoyaleGas;
        knightRoyale.RoyalePingEvent += OnRoyalePing;
        knightRoyale.RoyaleRewardEvent += OnRoyaleReward;
        knightRoyale.RoyaleWinnerEvent += OnRoyaleWinner;
        knightRoyale.RoyaleRestoreEvent += OnRoyaleRestore;
    }

    internal string? ChatRefusal() => InRoyaleZone ? ItemData.Text(KnightRoyale.ChatBlockedText, "Cannot be used while in chat restriction status.") : null;

    public void Dispose()
    {
        knightRoyale.RoyaleSignUpEvent -= OnRoyaleSignUp;
        knightRoyale.RoyaleEntryEvent -= OnRoyaleEntry;
        knightRoyale.RoyaleAbandonedEvent -= OnRoyaleAbandoned;
        knightRoyale.RoyaleGasEvent -= OnRoyaleGas;
        knightRoyale.RoyalePingEvent -= OnRoyalePing;
        knightRoyale.RoyaleRewardEvent -= OnRoyaleReward;
        knightRoyale.RoyaleWinnerEvent -= OnRoyaleWinner;
        knightRoyale.RoyaleRestoreEvent -= OnRoyaleRestore;
    }

    private void OnRoyaleSignUp(RoyaleSignUp signUp)
    {
        switch (signUp.Result)
        {
            case KnightRoyaleSignUpResult.Open:
                OpenRoyaleBoard(signUp.Mode, signUp.Participants, signUp.Seconds);
                return;
            case KnightRoyaleSignUpResult.Count:
                if (royaleBoardCount != null) royaleBoardCount.Text = signUp.Participants.ToString();
                if (royaleBoardOpen && RoyaleBoard is { Visible: false }) RoyaleBoard.Visible = true;
                return;
        }
        int text = KnightRoyale.SignUpText(signUp.Result);
        if (text > 0) context.Log.Notice(ItemData.Text(text, ""), KnightRoyale.Yellow);
        SetRoyaleApplied(KnightRoyale.ShowsCancel(signUp.Result));
    }

    internal void OpenRoyaleBoard(int mode, int participants, int seconds)
    {
        EnsureRoyaleBoard();
        royaleBoardTitle!.Text = ItemData.Text(KnightRoyale.TitleFor(mode), "Knight Royale");
        royaleBoardCount!.Text = participants.ToString();
        royaleBoardEndsAt = context.Now + seconds;
        royaleBoardOpen = true;
        SetRoyaleApplied(false);
        RoyaleBoard!.Visible = true;
        Audio.PlayUi(Sfx.InventoryOpen);
    }

    private void SetRoyaleApplied(bool applied)
    {
        if (royaleApply == null || royaleCancel == null) return;
        royaleApply.Visible = !applied;
        royaleCancel.Visible = applied;
    }

    private void EnsureRoyaleBoard()
    {
        if (RoyaleBoard != null && GodotObject.IsInstanceValid(RoyaleBoard)) return;
        EnsureRoyaleLayer();
        var anchor = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        anchor.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        royaleLayer!.AddChild(anchor);
        RoyaleBoard = new PanelContainer { Visible = false };
        RoyaleBoard.AddThemeStyleboxOverride("panel", QuestProgressToastFeature.ToastStyle());
        anchor.AddChild(RoyaleBoard);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        RoyaleBoard.AddChild(column);

        var strip = new Button { Flat = true, FocusMode = Control.FocusModeEnum.None };
        strip.Pressed += () =>
        {
            royaleBoardCollapsed = !royaleBoardCollapsed;
            royaleBoardBody!.Visible = !royaleBoardCollapsed;
            RoyaleBoard.ResetSize();
        };
        var head = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", 24);
        head.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        royaleBoardTitle = UiTheme.Text("", RoyaleBoardFontSize, UiTheme.GoldBright);
        royaleBoardTitle.MouseFilter = Control.MouseFilterEnum.Ignore;
        RoyaleBoardTime = UiTheme.Text("", RoyaleBoardFontSize, UiTheme.TextHi);
        RoyaleBoardTime.MouseFilter = Control.MouseFilterEnum.Ignore;
        head.AddChild(royaleBoardTitle);
        head.AddChild(RoyaleBoardTime);
        strip.CustomMinimumSize = new Vector2(280, 30);
        strip.AddChild(head);
        column.AddChild(strip);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);
        royaleBoardBody = body;
        var count = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        count.AddThemeConstantOverride("separation", 20);
        count.AddChild(UiTheme.Text("Participants", RoyaleBoardFontSize, UiTheme.TextLo));
        royaleBoardCount = UiTheme.Text("0", RoyaleBoardFontSize, UiTheme.Gold);
        count.AddChild(royaleBoardCount);
        body.AddChild(count);
        royaleApply = GenderChangeFeature.LookActionButton("Apply", () =>
        {
            SetRoyaleApplied(true);
            requests.RoyaleSignUp(apply: true);
        });
        royaleCancel = GenderChangeFeature.LookActionButton("Cancel", () =>
        {
            SetRoyaleApplied(false);
            requests.RoyaleSignUp(apply: false);
        });
        body.AddChild(royaleApply);
        body.AddChild(royaleCancel);
        column.AddChild(body);
    }

    private void EnsureRoyaleLayer()
    {
        if (royaleLayer != null && GodotObject.IsInstanceValid(royaleLayer)) return;
        royaleLayer = new CanvasLayer { Layer = UiLayers.TopPrompts };
        context.Root.AddChild(royaleLayer);
    }

    internal void EnsureRoyaleScore()
    {
        if (RoyaleScore != null && GodotObject.IsInstanceValid(RoyaleScore)) return;
        EnsureRoyaleLayer();
        RoyaleScore = new HudWindow("royale_score", ItemData.Text(KnightRoyale.TitleText, "Knight Royale"), closable: false) { Visible = false };
        royaleLayer!.AddChild(RoyaleScore);
        RoyaleSurvivors = UiTheme.Text("", RoyaleScoreFontSize, UiTheme.TextHi, HorizontalAlignment.Center);
        RoyaleNextGas = UiTheme.Text("", RoyaleScoreFontSize, UiTheme.TextLo, HorizontalAlignment.Center);
        RoyaleNextGas.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        RoyaleNextGas.CustomMinimumSize = new Vector2(140, 0);
        RoyaleGasTime = UiTheme.Text("", RoyaleScoreFontSize + 2, UiTheme.GoldBright, HorizontalAlignment.Center);
        RoyaleScore.Body.AddChild(RoyaleSurvivors);
        RoyaleScore.Body.AddChild(RoyaleNextGas);
        RoyaleScore.Body.AddChild(RoyaleGasTime);
    }

    private void OnRoyaleEntry(RoyaleEntry entry)
    {
        royaleBoardOpen = false;
        if (RoyaleBoard != null && GodotObject.IsInstanceValid(RoyaleBoard)) RoyaleBoard.Visible = false;
        EnsureRoyaleScore();
        RoyaleScore!.Visible = true;
    }

    private void OnRoyaleAbandoned(string name, int survivors)
    {
        if (name.Length > 0)
            context.Log.Notice(TextTemplate.Fill(ItemData.Text(KnightRoyale.AbandonedText, "%s has abandoned the Battlegrounds"), name), KnightRoyale.Yellow);
    }

    private void OnRoyaleGas(int released, int seconds)
    {
        if (released != 0)
            context.Log.Notice(TextTemplate.Fill(ItemData.Text(KnightRoyale.GasReleasedText, "Poison Gas has been released in area %d"), released), KnightRoyale.Red);
        int next = released + 1;
        if (!KnightRoyale.GasEnds(next))
            context.Log.Notice(TextTemplate.Fill(ItemData.Text(KnightRoyale.GasCountdownText, "In %d seconds, Poison Gas will be released in area %d"), seconds, next), KnightRoyale.Yellow);
    }

    private void OnRoyalePing(int x, int z, bool isDelegate)
    {
        int text = isDelegate ? KnightRoyale.DelegateText : KnightRoyale.ChestText;
        string fallback = isDelegate ? "Current Delegate's Position:(%d, %d)" : "Treasure Chest has appeared. Location: (%d, %d)";
        context.Log.Notice(TextTemplate.Fill(ItemData.Text(text, fallback), x, z), isDelegate ? KnightRoyale.Crimson : KnightRoyale.GreenYellow);
        royalePings.Add((x, z, context.Now + KnightRoyale.PingSeconds, isDelegate));
    }

    private void OnRoyaleReward(KnightRoyale.Reward reward)
    {
        foreach (string line in KnightRoyale.RewardLines(reward, ItemData.Text))
            context.Log.Notice(line, KnightRoyale.GreenYellow);
    }

    private void OnRoyaleWinner(string name, int area)
    {
        string text = TextTemplate.Fill(ItemData.Text(KnightRoyale.WinnerText, "%s has won in Knight Royale area number %d."), name, area + 1);
        context.Chat?.ShowNoticePreview(text);
        context.Log.Notice(text, KnightRoyale.GreenYellow);
    }

    private void OnRoyaleRestore()
    {
        if (RoyaleScore != null && GodotObject.IsInstanceValid(RoyaleScore)) RoyaleScore.Visible = false;
    }

    private void KnightRoyaleTick()
    {
        if (royaleBoardOpen && RoyaleBoardTime != null)
        {
            int left = (int)Mathf.Max(0, (float)(royaleBoardEndsAt - context.Now));
            RoyaleBoardTime.Text = KnightRoyale.Clock(left);
            if (left <= 0)
            {
                royaleBoardOpen = false;
                RoyaleBoard!.Visible = false;
            }
        }
        if (RoyaleScore is { Visible: true })
        {
            if (knightRoyale.RoyaleSurvivors > 0)
                RoyaleSurvivors!.Text = TextTemplate.Fill(ItemData.Text(KnightRoyale.SurvivorsText, "Survivor(s): %d"), knightRoyale.RoyaleSurvivors);
            bool gas = knightRoyale.RoyaleCountdown && !KnightRoyale.GasEnds(knightRoyale.RoyaleNextArea);
            RoyaleNextGas!.Text = gas ? TextTemplate.Fill(ItemData.Text(KnightRoyale.NextGasText, "Remaining time until # %d Poison Gas:"), knightRoyale.RoyaleNextArea) : "";
            RoyaleGasTime!.Text = gas ? KnightRoyale.Clock(knightRoyale.NextGasSeconds(Time.GetTicksMsec())) : "";
        }
        if (context.Now >= royaleReturnBoxAt)
        {
            royaleReturnBoxAt = double.PositiveInfinity;
            ShowRoyaleReturnBox();
        }
        royalePings.RemoveAll(p => context.Now >= p.Until);
    }

    internal bool RoyaleDeath()
    {
        if (!InRoyaleZone) return false;
        royaleReturnBoxAt = context.Now + KnightRoyale.ReturnBoxDelaySeconds;
        return true;
    }

    private void ShowRoyaleReturnBox()
    {
        int left = KnightRoyale.ReturnBoxSeconds;
        string Line(int s) => TextTemplate.Fill(ItemData.Text(KnightRoyale.ReturnText, "you will be teleported back in %d seconds"), s);
        Notice.Show(context.Root, Line(left));
        for (int s = 1; s < KnightRoyale.ReturnBoxSeconds; s++)
        {
            int shown = left - s;
            context.Root.After(s, () => context.Log.Notice(Line(shown), KnightRoyale.Yellow));
        }
    }

    private bool ShowRoyaleAreaCaption(int region)
    {
        if (!InRoyaleZone) return false;
        int area = KnightRoyale.AreaOf(region);
        if (area == 0) return false;
        int text = KnightRoyale.AreaCaption(area, knightRoyale.RoyaleReleasedArea);
        string caption = text == KnightRoyale.AreaText
            ? TextTemplate.Fill(ItemData.Text(text, "Area %d"), area)
            : ItemData.Text(text, "This area is filled with poison gas and will cause damage.");
        ShowAreaBanner(caption, KnightRoyale.Red, AreaNameHold, AreaNameFadeIn, AreaBannerTop);
        return true;
    }

    private void AddRoyalePings(List<MiniMap.Blip> blips)
    {
        foreach (var ping in royalePings)
            blips.Add(new MiniMap.Blip(ping.X, ping.Z, ping.Delegate ? KnightRoyale.Crimson : KnightRoyale.GreenYellow, RoyalePingRadius, true));
    }


    private const int AreaTitleFontSize = 26;
    private const int AreaSubtitleFontSize = 20;
    private const float AreaBannerTop = 100f;
    private const float AreaSubtitleDrop = 50f;
    private const char AreaSubtitleSeparator = '/';
    private const double AreaNameHold = 1.0;
    private const double AreaNoticeHold = 3.0;
    private const double AreaNameFadeIn = 0.5;
    private const double AreaNoticeFadeIn = 1.5;
    private const double AreaFadeOut = 1.0;
    private const double AreaHintHold = 5.0;
    private const float AreaHintHeight = 0.4f;
    private static readonly Color AreaBannerColour = new(1f, 1f, 0f);
    private static readonly Color AreaWarningColour = new(1f, 0f, 0f);

    private readonly List<int> areasHere = new();
    private readonly HashSet<int> areasBefore = new();
    private int areasZone = -1;
    private Control? areaBanner;
    private Label areaTitle = null!;
    private Label areaSubtitle = null!;
    private Tween? areaTween;

    internal void AreaBannerTick()
    {
        if (context.Zone != areasZone)
        {
            areasZone = context.Zone;
            areasBefore.Clear();
        }
        var areas = ZoneAreas.For(context.Zone);
        if (areas.Count == 0) return;

        areasHere.Clear();
        foreach (var area in areas)
            if (area.Contains(context.Self.MapXZ.X, context.Self.MapXZ.Z) && !areasHere.Contains(area.Id)) areasHere.Add(area.Id);
        int entered = AreaNames.Entered(areasHere, areasBefore);
        if (entered is AreaNames.Unchanged or AreaNames.Left) return;
        if (ShowRoyaleAreaCaption(entered)) return;

        var notice = AreaNames.NoticeFor(context.Zone, entered);
        string text = notice.TextId > 0 ? ItemData.Text(notice.TextId, "") : "";
        if (text.Length == 0) return;
        switch (notice.Kind)
        {
            case AreaNoticeKind.Log:
                context.Log.Add(text, CombatLogKind.Status, AreaBannerColour);
                break;
            case AreaNoticeKind.Banner:
            case AreaNoticeKind.WarningBanner:
                ShowAreaBanner(text, notice.Kind == AreaNoticeKind.WarningBanner ? AreaWarningColour : AreaBannerColour,
                    notice.LongHold ? AreaNoticeHold : AreaNameHold, notice.LongHold ? AreaNoticeFadeIn : AreaNameFadeIn,
                    AreaBannerTop);
                break;
            case AreaNoticeKind.Hint:
                ShowAreaBanner(text, AreaBannerColour, AreaHintHold, AreaNoticeFadeIn,
                    context.Root.GetViewport().GetVisibleRect().Size.Y * AreaHintHeight);
                break;
        }
    }


    private void ShowAreaBanner(string text, Color colour, double hold, double fadeIn, float top)
    {
        EnsureAreaBanner();
        areaTitle.OffsetTop = top;
        areaSubtitle.OffsetTop = top + AreaSubtitleDrop;
        int split = text.IndexOf(AreaSubtitleSeparator);
        areaTitle.Text = split < 0 ? text : text[..split];
        areaSubtitle.Text = split < 0 ? "" : text[(split + 1)..];
        areaSubtitle.Visible = areaSubtitle.Text.Length > 0;
        areaTitle.AddThemeColorOverride("font_color", colour);
        areaSubtitle.AddThemeColorOverride("font_color", colour);

        var banner = areaBanner!;
        areaTween?.Kill();
        banner.Modulate = new Color(1f, 1f, 1f, 0f);
        banner.Visible = true;
        areaTween = context.Root.CreateTween();
        areaTween.TweenProperty(banner, "modulate:a", 1f, fadeIn);
        areaTween.TweenInterval(hold);
        areaTween.TweenProperty(banner, "modulate:a", 0f, AreaFadeOut);
        areaTween.TweenCallback(Callable.From(() => banner.Visible = false));
    }

    private void EnsureAreaBanner()
    {
        if (areaBanner != null && GodotObject.IsInstanceValid(areaBanner)) return;
        var layer = new CanvasLayer { Layer = UiLayers.Notices };
        context.Root.AddChild(layer);
        areaBanner = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        areaBanner.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        layer.AddChild(areaBanner);
        areaTitle = AreaBannerLine(AreaTitleFontSize, AreaBannerTop);
        areaSubtitle = AreaBannerLine(AreaSubtitleFontSize, AreaBannerTop + AreaSubtitleDrop);
        areaBanner.AddChild(areaTitle);
        areaBanner.AddChild(areaSubtitle);
    }

    private static Label AreaBannerLine(int fontSize, float top)
    {
        var label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        label.OffsetTop = top;
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 6);
        return label;
    }
}
