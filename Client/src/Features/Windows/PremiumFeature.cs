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
    internal VBoxContainer StatusColumn { get; private set; } = null!;
    internal PanelContainer Chip { get; private set; } = null!;
    private Panel dot = null!;
    private Label label = null!;

    public void Build()
    {
        BuildChip();
        BuildClanBonusChip();
        BuildSwitchWindow();
        premium.PremiumEvent += OnStatus;
        premium.SwitchReplied += OnSwitchReplied;
        progress.LevelChangeEvent += OnLevelChange;

        OnStatus(premium.PremiumAccountStatus, premium.PremiumType, premium.PremiumHours);
        requests.PremiumRequest();
    }

    private const int StatusGap = 4;
    internal PanelContainer ClanBonusChip { get; private set; } = null!;
    private Label clanBonusMarks = null!;

    internal void AttachBelow(Control part)
    {
        part.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        StatusColumn.AddChild(part);
    }

    internal void BuildClanBonusChip()
    {
        ClanBonusChip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        ClanBonusChip.AddThemeStyleboxOverride("panel", UiTheme.Chip());
        AttachBelow(ClanBonusChip);
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
    }

    public void Dispose()
    {
        clan.ClanBonusEvent -= OnClanBonus;
        premium.PremiumEvent -= OnStatus;
        premium.SwitchReplied -= OnSwitchReplied;
        progress.LevelChangeEvent -= OnLevelChange;
    }

    private const string SwitchWindowId = "premiumswitch";
    private const int AppliedText = 30980;
    private const string AppliedFallback = "%s Premium service has been applied.";
    private const int SwitchWaitText = 30982;
    private const string SwitchWaitFallback = "Due to previously having changed the Premium, you may only change after %d seconds.";
    private const int SwitchFailedText = 30989;
    private const string SwitchFailedFallback = "Failed to set Premium. Please try again.";
    private const int InUseText = 30993;
    private const string InUseFallback = "This is the Premium you are using currently";
    private const float SwitchWindowWidth = 300f;

    private CanvasLayer windowLayer = null!;
    internal HudWindow SwitchWindow { get; private set; } = null!;

    internal IClosableWindow SwitchPanel => new ClosablePanel(() => SwitchWindow.Visible, CloseSwitch);

    internal void BuildSwitchWindow()
    {
        windowLayer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(windowLayer);
        SwitchWindow = new HudWindow(SwitchWindowId, ItemData.Text(PremiumNames.PremiumText, PremiumNames.PremiumFallback)) { Visible = false };
        SwitchWindow.Closed += CloseSwitch;
        SwitchWindow.Body.CustomMinimumSize = new Vector2(SwitchWindowWidth, 0);
        SwitchWindow.Body.AddThemeConstantOverride("separation", 6);
        windowLayer.AddChild(SwitchWindow);

        Chip.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        Chip.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) ToggleSwitch();
        };
    }

    internal void ToggleSwitch()
    {
        if (SwitchWindow.Visible) { CloseSwitch(); return; }
        if (premium.Held.Count == 0) return;
        RefreshSwitch();
        SwitchWindow.Visible = true;
    }

    internal void CloseSwitch() => SwitchWindow.Visible = false;

    private void RefreshSwitch()
    {
        foreach (var child in SwitchWindow.Body.GetChildren()) child.QueueFree();
        foreach (var held in premium.Held)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var name = UiTheme.Text(PremiumTitle(held.Type), 13, UiTheme.GoldBright);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(name);
            row.AddChild(UiTheme.Text(TimeLine(held.Hours), 12, UiTheme.TextLo));
            bool inUse = held.Type == premium.PremiumType;
            var use = MerchantFeature.MenuButton(inUse ? "In use" : "Use", () => Use(held.Type));
            use.Disabled = inUse;
            row.AddChild(use);
            SwitchWindow.Body.AddChild(row);
        }
    }

    private void Use(int type)
    {
        if (type == premium.PremiumType)
        {
            context.Log.Notice(ItemData.Text(InUseText, InUseFallback));
            return;
        }
        requests.Switch(type);
    }

    private void OnSwitchReplied(PremiumSwitchReply reply)
    {
        string text = reply.Result switch
        {
            PremiumWire.SwitchDone => ItemData.Text(AppliedText, AppliedFallback).Replace("%s", TypeName(reply.Type)),
            PremiumWire.SwitchTooSoon => ItemData.Text(SwitchWaitText, SwitchWaitFallback).Replace("%d", reply.Seconds.ToString()),
            _ => ItemData.Text(SwitchFailedText, SwitchFailedFallback),
        };
        context.Log.Notice(text);
    }

    private static string TypeName(int type) =>
        PremiumNames.Name(type) is { } name ? ItemData.Text(name.Text, name.Fallback) : type.ToString();

    private static string TimeLine(int hours)
    {
        var (text, fallback, value) = PremiumNames.TimeLeft(hours);
        return ItemData.Text(text, fallback).Replace("%d", value.ToString());
    }

    private void OnLevelChange(LevelChange change)
    {
        if (premium.PremiumHours > 0) requests.PremiumRequest();
    }

    internal void BuildChip()
    {
        Layer = new CanvasLayer { Layer = UiLayers.HudButtons };
        context.Root.AddChild(Layer);

        StatusColumn = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        StatusColumn.AddThemeConstantOverride("separation", StatusGap);
        Layer.AddChild(StatusColumn);
        StatusColumn.MinimumSizeChanged += () => Callable.From(topIcons.PinAnchor).CallDeferred();

        Chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        Chip.AddThemeStyleboxOverride("panel", UiTheme.Chip());
        AttachBelow(Chip);
        topIcons.SetAnchor(StatusColumn);
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
        if (GodotObject.IsInstanceValid(SwitchWindow) && SwitchWindow.Visible) RefreshSwitch();
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
        string timeLine = TimeLine(remainingHours);
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
