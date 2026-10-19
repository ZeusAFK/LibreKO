using Godot;

namespace LibreKO.Features.Windows;

internal sealed class PremiumFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly ClanSession clan;
    private readonly PremiumSession premium;
    private readonly ProgressSession progress;
    private readonly IPremiumRequests requests;
    private readonly TopIconsFeature topIcons;

    public PremiumFeature(IWorldContext context, ClanSession clan, PremiumSession premium, ProgressSession progress, IPremiumRequests requests, TopIconsFeature topIcons)
    {
        this.context = context;
        this.clan = clan;
        this.premium = premium;
        this.progress = progress;
        this.requests = requests;
        this.topIcons = topIcons;
    }

    internal CanvasLayer Layer { get; private set; } = null!;
    internal PanelContainer Chip { get; private set; } = null!;
    private Panel dot = null!;
    private Label label = null!;

    public void Build()
    {
        BuildChip();
        BuildClanBonusChip();
        premium.PremiumEvent += OnStatus;
        progress.LevelChangeEvent += OnLevelChange;

        OnStatus(premium.PremiumAccountStatus, premium.PremiumType, premium.PremiumHours);
        requests.PremiumRequest();
    }

    private const float ClanBonusChipGap = 4f;
    internal PanelContainer ClanBonusChip { get; private set; } = null!;
    private Label clanBonusMarks = null!;

    internal void BuildClanBonusChip()
    {
        ClanBonusChip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        ClanBonusChip.AddThemeStyleboxOverride("panel", UiTheme.Chip());
        Layer.AddChild(ClanBonusChip);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 7);
        ClanBonusChip.AddChild(row);
        var title = UiTheme.Text("Clan", 12, UiTheme.TextLo);
        title.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(title);
        clanBonusMarks = UiTheme.Text("", 12, UiTheme.GoldBright);
        clanBonusMarks.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(clanBonusMarks);
        clan.ClanBonusEvent += OnClanBonus;
        OnClanBonus(clan.ClanOnlineCount);
    }

    internal void OnClanBonus(int online)
    {
        if (!GodotObject.IsInstanceValid(ClanBonusChip)) return;
        bool shown = online != ClanSession.NoClanBonus && clan.MyClan.InClan;
        ClanBonusChip.Visible = shown;
        if (!shown) return;
        clanBonusMarks.Text = ClanBonusCard.Marks(online,
            ItemData.Text(ClanBonusCard.OnText, ClanBonusCard.OnFallback),
            ItemData.Text(ClanBonusCard.OffText, ClanBonusCard.OffFallback));
        ClanBonusChip.TooltipText = ClanBonusCard.Tooltip(online,
            ItemData.Text(ClanBonusCard.NeedText, ClanBonusCard.NeedFallback),
            ItemData.Text(ClanBonusCard.BonusText, ClanBonusCard.BonusFallback));
        PinClanBonusChip();
    }

    private void PinClanBonusChip()
    {
        if (!GodotObject.IsInstanceValid(ClanBonusChip) || !GodotObject.IsInstanceValid(Chip)) return;
        float below = Chip.Size.Y + ClanBonusChipGap;
        HudAnchor.Pin(ClanBonusChip, HudAnchor.Spot.TopRight,
            new Vector2(topIcons.MiniMapRightSpan() + UiKit.StatusHudGap, HudAnchor.Edge + below));
    }

    public void Dispose()
    {
        clan.ClanBonusEvent -= OnClanBonus;
        premium.PremiumEvent -= OnStatus;
        progress.LevelChangeEvent -= OnLevelChange;
    }

    private void OnLevelChange(LevelChange change)
    {
        if (premium.PremiumHours > 0) requests.PremiumRequest();
    }

    internal void BuildChip()
    {
        Layer = new CanvasLayer { Layer = UiLayers.HudButtons };
        context.Root.AddChild(Layer);

        Chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        Chip.AddThemeStyleboxOverride("panel", UiTheme.Chip());
        Layer.AddChild(Chip);
        topIcons.SetAnchor(Chip);
        topIcons.PinAnchor();

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 7);
        Chip.AddChild(row);

        dot = new Panel
        {
            CustomMinimumSize = new Vector2(9, 9),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var dotWrap = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        dotWrap.AddChild(dot);
        row.AddChild(dotWrap);

        label = UiTheme.Text("No Premium", 12, UiTheme.TextLo);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(label);

        SetDot(UiTheme.TextDim);
    }

    private void SetDot(Color color)
    {
        var sb = new StyleBoxFlat { BgColor = color };
        sb.SetCornerRadiusAll(5);
        dot.AddThemeStyleboxOverride("panel", sb);
    }

    private const int PcRoomStatus = 2;

    internal void OnStatus(int accountStatus, int premiumType, int remainingHours)
    {
        bool active = accountStatus != 0 && remainingHours > 0;
        if (!active)
        {
            label.Text = "No Premium";
            label.AddThemeColorOverride("font_color", UiTheme.TextLo);
            Chip.TooltipText = "";
            SetDot(UiTheme.TextDim);
            return;
        }

        string name = accountStatus == PcRoomStatus ? "PC Room" : PremiumTitle(premiumType);
        var (timeText, timeFallback, timeValue) = PremiumNames.TimeLeft(remainingHours);
        string timeLine = ItemData.Text(timeText, timeFallback).Replace("%d", timeValue.ToString());
        label.Text = name;
        Chip.TooltipText = PremiumBonus.Tooltip(name, timeLine, premium.PremiumBonuses);
        label.AddThemeColorOverride("font_color", UiTheme.GoldBright);
        SetDot(UiTheme.Gold);
    }

    private static string PremiumTitle(int type)
    {
        string premiumName = ItemData.Text(PremiumNames.PremiumText, PremiumNames.PremiumFallback);
        return PremiumNames.Name(type) is { } name ? $"{ItemData.Text(name.Text, name.Fallback)} {premiumName}" : premiumName;
    }
}
