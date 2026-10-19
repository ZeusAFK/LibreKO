using Godot;

namespace LibreKO.Features.Hud;

internal sealed class StatusHudFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly SelfSession self;
    private readonly CombatSystem combat;

    public StatusHudFeature(IWorldContext context, SelfSession self, CombatSystem combat)
    {
        this.context = context;
        this.self = self;
        this.combat = combat;
    }

    private static readonly Vector2 OnlineTimeHudPosition = new(120f, 11f);
    private const double OnlineTimeTickSeconds = 1.0;
    private const ulong MsecPerSecond = 1000;
    private Label? onlineLabel;

    internal LevelOrb Orb { get; private set; } = null!;
    internal StatBar HpBar { get; private set; } = null!;
    internal StatBar MpBar { get; private set; } = null!;
    private StatBar vpBar = null!;

    private static readonly Vector2 Pos = new(HudAnchor.Edge, 10f);
    private static readonly Vector2 Size = new(362f, 104f);
    private static readonly Vector2 HpBarPos = new(84f, 28f);
    private static readonly Vector2 HpBarSize = new(274f, 25f);
    private static readonly Vector2 MpBarPos = new(93f, 55f);
    private static readonly Vector2 MpBarSize = new(265f, 17f);
    private const float VpBarGap = 3f;
    private const float VpBarInset = 5f;
    private static readonly Vector2 VpBarPos = new(MpBarPos.X + VpBarInset, MpBarPos.Y + MpBarSize.Y + VpBarGap);
    private static readonly Vector2 VpBarSize = new(MpBarSize.X - VpBarInset * 2f, 14f);
    private static readonly Vector2 TouchVpBarPos = new(0f, 50f);
    private static readonly Vector2 TouchVpBarSize = new(362f, 12f);
    private static readonly Color VpBarColor = Color.Color8(0, 157, 125);
    private static readonly Vector2 TouchSize = new(362f, 48f);
    internal static readonly Vector2 TouchHpBarPos = new(0f, 0f);
    private static readonly Vector2 TouchHpBarSize = new(362f, 26f);
    private static readonly Vector2 TouchMpBarPos = new(0f, 30f);
    private static readonly Vector2 TouchMpBarSize = new(362f, 18f);

    internal static Vector2 VitalsSize => Platform.TouchUi ? TouchSize : Size;

    internal Control Root { get; private set; } = null!;

    internal void RefreshVitalBars()
    {
        var vitals = self.Vitals;
        HpBar?.Set(vitals.Hp, vitals.MaxHp);
        MpBar?.Set(vitals.Mp, vitals.MaxMp);
    }

    internal void RefreshLevelOrb()
    {
        if (Orb == null || !GodotObject.IsInstanceValid(Orb)) return;
        Orb.Set(self.Sheet.Level, self.Sheet.ExpPercent);
        UpdateRebirthEmblem();
    }

    private const float RebirthEmblemSize = 30f;
    private static readonly Vector2 RebirthEmblemPos = new(72f, 4f);
    internal RebirthEmblem? RebirthEmblem { get; private set; }

    private void UpdateRebirthEmblem()
    {
        if (RebirthEmblem == null || !GodotObject.IsInstanceValid(RebirthEmblem)) return;
        bool shown = !Platform.TouchUi && RebirthProgress.Shown(self.Sheet.Level, self.Sheet.RebirthLevel);
        RebirthEmblem.Visible = shown;
        if (!shown) return;
        RebirthEmblem.Set(self.Sheet.RebirthLevel,
            RebirthProgress.Fraction(self.Sheet.Level, self.Sheet.ExpPercent, self.Sheet.RebirthLevel),
            RebirthProgress.Ready(self.Sheet.Level, self.Sheet.ExpPercent, self.Sheet.RebirthLevel),
            RebirthProgress.Tooltip(self.Sheet.Level, self.Sheet.ExpPercent, self.Sheet.RebirthLevel, self.Sheet.RebirthBonusAtRow));
    }

    public void Dispose()
    {
        combat.SelfMaximaApplied -= OnMaximaApplied;
        self.SelfHpEvent -= OnSelfHp;
        self.SelfMpEvent -= OnSelfMp;
        self.SelfStaminaEvent -= OnSelfStamina;
        self.SheetChangedEvent -= UpdateRebirthEmblem;
    }

    private void OnSelfHp(SelfHpChange change) => HpBar?.Set(change.Hp, change.MaxHp);

    private void OnSelfMp(VitalChange change) => MpBar?.Set(change.Value, change.Max);

    private void OnSelfStamina(VitalChange change) => ShowVpBar(change.Value, change.Max);

    private void OnMaximaApplied()
    {
        HpBar?.Set(self.Vitals.Hp, self.Vitals.MaxHp);
        MpBar?.Set(self.Vitals.Mp, self.Vitals.MaxMp);
    }

    public void Build()
    {
        combat.SelfMaximaApplied += OnMaximaApplied;
        self.SelfHpEvent += OnSelfHp;
        self.SelfMpEvent += OnSelfMp;
        self.SelfStaminaEvent += OnSelfStamina;
        self.SheetChangedEvent += UpdateRebirthEmblem;
        var layer = new CanvasLayer { Layer = UiLayers.Hud };
        context.Root.AddChild(layer);
        context.Hud.PluginSeam(layer, LibreKO.Plugins.HudPart.StatusBars);

        var status = new Control
        {
            Position = Pos,
            CustomMinimumSize = VitalsSize,
        };
        layer.AddChild(status);
        Root = status;

        bool touch = Platform.TouchUi;
        HpBar = new StatBar(new Color("d51f20"),
                             touch ? TouchHpBarSize : HpBarSize,
                             touch ? StatBar.RibbonKind.Plain : StatBar.RibbonKind.Upper)
        {
            Position = touch ? TouchHpBarPos : HpBarPos,
            Glide = true,
        };
        status.AddChild(HpBar);
        MpBar = new StatBar(new Color("246fd0"),
                             touch ? TouchMpBarSize : MpBarSize,
                             touch ? StatBar.RibbonKind.Plain : StatBar.RibbonKind.Lower)
        {
            Position = touch ? TouchMpBarPos : MpBarPos,
            Glide = true,
        };
        status.AddChild(MpBar);
        vpBar = new StatBar(VpBarColor, touch ? TouchVpBarSize : VpBarSize, StatBar.RibbonKind.Plain)
        {
            Position = touch ? TouchVpBarPos : VpBarPos,
            Visible = self.Vitals.MaxSp > 0,
            Glide = true,
        };
        status.AddChild(vpBar);

        if (!touch)
        {
            onlineLabel = HudStyle.Label(11, HorizontalAlignment.Left);
            onlineLabel.Position = OnlineTimeHudPosition;
            onlineLabel.TooltipText = "Time online since this character entered the game";
            onlineLabel.MouseFilter = Control.MouseFilterEnum.Pass;
            onlineLabel.AddThemeColorOverride("font_color", UiTheme.TextDim);
            onlineLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
            onlineLabel.AddThemeConstantOverride("outline_size", 2);
            status.AddChild(onlineLabel);
            var onlineTick = new Godot.Timer { WaitTime = OnlineTimeTickSeconds, Autostart = true };
            onlineTick.Timeout += RefreshOnlineTime;
            status.AddChild(onlineTick);
            RefreshOnlineTime();
        }

        Orb = new LevelOrb(92f) { Position = new Vector2(8f, 7f), Visible = !touch };
        status.AddChild(Orb);
        RebirthEmblem = new RebirthEmblem(RebirthEmblemSize) { Position = RebirthEmblemPos, Visible = false };
        status.AddChild(RebirthEmblem);

        Orb.Set(self.Sheet.Level, self.Sheet.ExpPercent);
        UpdateRebirthEmblem();
        HpBar.Set(self.Vitals.Hp, self.Vitals.MaxHp);
        MpBar.Set(self.Vitals.Mp, self.Vitals.MaxMp);
        context.Inventory.RefreshCash();

        HudLayout.Attach(status, "hud_status", status, () => Pos,
            anchor: HudPlacement.StatusAnchor, anchorMargin: HudPlacement.StatusMargin,
            anchorSize: VitalsSize);
    }

    private void RefreshOnlineTime()
    {
        if (onlineLabel == null) return;
        long seconds = (long)((Time.GetTicksMsec() - self.OnlineSinceMsec) / MsecPerSecond);
        onlineLabel.Text = $"Online {Domain.OnlineTime.Format(seconds)}";
    }

    internal void ShowVpBar(int sp, int maxSp)
    {
        if (vpBar == null || !GodotObject.IsInstanceValid(vpBar)) return;
        bool shown = maxSp > 0;
        vpBar.Set(sp, maxSp);
        if (vpBar.Visible == shown) return;
        vpBar.Visible = shown;
        LauncherAnchorChanged?.Invoke();
    }

    internal event Action? LauncherAnchorChanged;

    internal Vector2 LauncherAnchor
    {
        get
        {
            float vpDrop = VpBarShown && !Platform.TouchUi ? VpBarSize.Y + VpBarGap : 0f;
            return new Vector2(Pos.X + MpBarPos.X, Pos.Y + MpBarPos.Y + MpBarSize.Y + UiKit.StatusHudGap + vpDrop);
        }
    }

    private bool VpBarShown => vpBar != null && GodotObject.IsInstanceValid(vpBar) && vpBar.Visible;
}
