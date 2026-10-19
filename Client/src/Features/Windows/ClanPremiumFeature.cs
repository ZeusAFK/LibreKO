using Godot;

namespace LibreKO.Features.Windows;

internal sealed class ClanPremiumFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly ClanSession clan;
    private readonly ClanPremiumSession clanPremium;
    private readonly PremiumSession premium;
    private readonly IClanPremiumRequests requests;

    public ClanPremiumFeature(IWorldContext context, ClanSession clan, ClanPremiumSession clanPremium, PremiumSession premium, IClanPremiumRequests requests)
    {
        this.context = context;
        this.clan = clan;
        this.clanPremium = clanPremium;
        this.premium = premium;
        this.requests = requests;
    }

    private CanvasLayer layer = null!;
    private PanelContainer chip = null!;
    private Panel dot = null!;
    private Label label = null!;
    private bool inClan;

    private const double RefreshSeconds = 60.0;

    public void Build()
    {
        BuildChip();
        clanPremium.ClanPremiumEvent += OnStatus;
        premium.PremiumEvent += OnPremiumStatus;
        clan.MyClanChangedEvent += OnClanInfo;

        RefreshChip();
        requests.Query();
    }

    public void Dispose()
    {
        clanPremium.ClanPremiumEvent -= OnStatus;
        premium.PremiumEvent -= OnPremiumStatus;
        clan.MyClanChangedEvent -= OnClanInfo;
    }

    private void BuildChip()
    {
        layer = new CanvasLayer { Layer = UiLayers.HudButtons };
        context.Root.AddChild(layer);

        chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        chip.AddThemeStyleboxOverride("panel", UiTheme.Chip());

        chip.AnchorLeft = 1; chip.AnchorRight = 1;
        chip.AnchorTop = 0; chip.AnchorBottom = 0;
        chip.GrowHorizontal = Control.GrowDirection.Begin;
        chip.OffsetRight = -14;
        chip.OffsetTop = 12 + (int)MiniMap.SquareSize + 8 + 30;
        chip.Visible = false;
        layer.AddChild(chip);

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 7);
        chip.AddChild(row);

        dot = new Panel
        {
            CustomMinimumSize = new Vector2(9, 9),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var dotWrap = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        dotWrap.AddChild(dot);
        row.AddChild(dotWrap);

        label = UiTheme.Text("Clan: No Premium", 12, UiTheme.TextLo);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(label);

        SetDot(UiTheme.TextDim);
        var refresh = new Godot.Timer { WaitTime = RefreshSeconds, Autostart = true };
        refresh.Timeout += RefreshChip;
        layer.AddChild(refresh);
        HudLayout.Attach(
            chip,
            "hud_clan_premium",
            chip,
            () => chip.Position);
    }

    private void SetDot(Color color)
    {
        var sb = new StyleBoxFlat { BgColor = color };
        sb.SetCornerRadiusAll(5);
        dot.AddThemeStyleboxOverride("panel", sb);
    }

    private void OnStatus(bool active)
    {
        RefreshChip();
        RefreshPremiumSeller();
    }

    private void OnPremiumStatus(int accountStatus, int premiumType, int remainingHours) => RefreshPremiumSeller();

    internal void RefreshPremiumSeller() =>
        ItemData.PremiumSeller = (premium.PremiumAccountStatus != 0 && premium.PremiumHours > 0) || clanPremium.ClanPremiumActive;

    private void OnClanInfo(MyClanInfo info)
    {
        inClan = info.InClan;
        RefreshChip();
    }

    private void RefreshChip()
    {
        chip.Visible = inClan;
        if (!inClan) return;

        long left = clanPremium.ClanPremiumEndsAt - System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        chip.TooltipText = clanPremium.ClanPremiumActive && clanPremium.ClanPremiumEndsAt > 0
            ? "Clan premium ends " + System.DateTimeOffset.FromUnixTimeSeconds(clanPremium.ClanPremiumEndsAt).LocalDateTime.ToString("yyyy-MM-dd HH:mm")
            : "";
        if (clanPremium.ClanPremiumActive)
        {
            label.Text = left > 0 ? $"Clan Premium · {DurationText.Short(left)}" : "Clan Premium";
            label.AddThemeColorOverride("font_color", UiTheme.GoldBright);
            SetDot(UiTheme.Gold);
        }
        else
        {
            label.Text = "Clan: No Premium";
            label.AddThemeColorOverride("font_color", UiTheme.TextLo);
            SetDot(UiTheme.TextDim);
        }
    }
}
