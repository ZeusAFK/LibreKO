using Godot;

namespace LibreKO.Features.Windows;

internal sealed class DeathDialogFeature : IFeature, ILateTickFeature
{
    private readonly IWorldContext context;
    private readonly ProgressSession progress;
    private readonly SelfSession self;
    private readonly ICombatRequests combatRequests;
    private readonly KnightRoyaleFeature knightRoyale;
    private readonly CombatSystem combat;

    public DeathDialogFeature(IWorldContext context, ProgressSession progress, SelfSession self, ICombatRequests combatRequests, KnightRoyaleFeature knightRoyale, CombatSystem combat)
    {
        this.context = context;
        this.progress = progress;
        this.self = self;
        this.combatRequests = combatRequests;
        this.knightRoyale = knightRoyale;
        this.combat = combat;
    }

    public void LateTick(double delta, double nowSec)
    {
        DeathDialogTick();
    }

    private const double RespawnRetrySeconds = 3.0;
    internal const string RespawnHint = "Press OK to teleport back to the re-spawn point.";
    private const string RespawnSentHint = "Returning to the re-spawn point…";
    private const string RespawnRetryHint = "No answer from the server — press OK to try again.";
    private const string ScrollExplanation = "EXP recovery item exists in the inventory.";
    internal const string ScrollConfirmHint = "Please confirm the availability.";
    private const int TextReturnToTown = 7653;
    private const int TextRespawnWith = 7652;
    private const int TextTownWithoutScroll = 42160;
    private const int TextAutoRespawn = 30610;
    private const double LifeCrystalCountdownSeconds = 15.0;
    private const string SpecialTypeKey = "Type";
    private const string ExpRecoverKey = "ExpRecover";
    private const float PanelWidth = 286f;
    private const float RowHeight = 25f;
    private const int RowFont = 13;
    private const int RowsMax = ResurrectionScrolls.MaxRows + 1;
    private const int TownRow = 0;
    private static readonly Color RowPicked = new(1f, 1f, 0f);
    private static readonly Color RowIdle = new(1f, 1f, 1f);

    internal CanvasLayer Layer { get; private set; } = null!;
    internal PanelContainer Panel { get; private set; } = null!;
    private Label expLbl = null!;
    private Label hintLbl = null!;
    private VBoxContainer rowBox = null!;
    private readonly List<Button> rows = new();
    private Button okBtn = null!;
    internal long ExpLost { get; set; }
    private int respawnToken;
    private bool respawnPending;
    private List<ScrollSkill> scrolls = new();
    private int pick;
    private double autoRespawnAt = double.PositiveInfinity;

    public void Build()
    {
        combat.SelfDied += OnSelfDied;
        combat.SelfResurrected += Hide;
        combat.SelfRespawned += Hide;
        BuildDeathDialog();
        progress.DeathExpLossEvent += OnExpLoss;
    }

    public void Dispose()
    {
        combat.SelfDied -= OnSelfDied;
        combat.SelfResurrected -= Hide;
        combat.SelfRespawned -= Hide;
        progress.DeathExpLossEvent -= OnExpLoss;
    }

    private void OnSelfDied()
    {
        if (!knightRoyale.RoyaleDeath()) Show();
    }

    internal void BuildDeathDialog()
    {
        Layer = new CanvasLayer { Layer = UiLayers.Menus, Visible = false };
        context.Root.AddChild(Layer);

        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.45f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dim.MouseFilter = Control.MouseFilterEnum.Stop;
        Layer.AddChild(dim);

        Panel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
        };
        Panel.AddThemeStyleboxOverride("panel", UiTheme.WindowPanel());
        Layer.AddChild(Panel);

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 8, 22, 8, 20);
        Panel.AddChild(margin);

        var col = new VBoxContainer { CustomMinimumSize = new Vector2(PanelWidth, 0) };
        col.AddThemeConstantOverride("separation", 8);
        margin.AddChild(col);

        expLbl = UiTheme.Text("", 14, UiTheme.Bad, HorizontalAlignment.Center);
        expLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(expLbl);

        hintLbl = UiTheme.Text(RespawnHint, 13, UiTheme.TextLo, HorizontalAlignment.Center);
        hintLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(hintLbl);

        rowBox = new VBoxContainer { Visible = false };
        rowBox.AddThemeConstantOverride("separation", 6);
        col.AddChild(rowBox);
        for (int i = 0; i < RowsMax; i++)
        {
            int row = i;
            var button = new Button
            {
                FocusMode = Control.FocusModeEnum.None,
                Flat = true,
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(PanelWidth, RowHeight),
            };
            button.AddThemeFontSizeOverride("font_size", RowFont);
            button.GuiInput += e => OnRowInput(row, e);
            rows.Add(button);
            rowBox.AddChild(button);
        }

        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

        okBtn = new Button
        {
            Text = "OK",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(120, 30),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        okBtn.Pressed += ConfirmChoice;
        col.AddChild(okBtn);
    }

    private void OnExpLoss(long lost)
    {
        ExpLost = lost;
        RefreshText();
    }

    internal bool OffersScrolls => scrolls.Count > 0;

    private void RefreshText()
    {
        if (OffersScrolls)
        {
            expLbl.Text = ScrollExplanation;
            return;
        }
        expLbl.Text = ExpLost > 0
            ? $"Lost {ExpLost:n0} experience points."
            : "You have been defeated.";
    }

    internal void Show()
    {
        scrolls = ResurrectionScrolls.Offer(CarriedScrollSkills(), context.Zone, self.Sheet.Level);
        int best = ResurrectionScrolls.BestIndex(scrolls);
        pick = best >= 0 ? best + 1 : TownRow;
        autoRespawnAt = OffersScrolls && context.Zone == ResurrectionScrolls.LifeCrystalZone
            ? context.Now + LifeCrystalCountdownSeconds
            : double.PositiveInfinity;
        RefreshRows();
        RefreshText();
        ArmRespawnButton(OffersScrolls ? ScrollConfirmHint : RespawnHint);
        Layer.Visible = true;
    }

    private IEnumerable<ScrollSkill> CarriedScrollSkills()
    {
        foreach (int abs in ScrollSearchSlots())
        {
            if (abs >= self.Inventory.Length || self.Inventory[abs].IsEmpty) continue;
            if (ItemData.Get(self.Inventory[abs].ItemId) is not { Effect1: not 0 } item || SkillData.Get(item.Effect1) is not { } s) continue;
            yield return new ScrollSkill(s.Id, s.Name, s.Moral, EffectInt(s, SpecialTypeKey), EffectInt(s, ExpRecoverKey));
        }
    }

    private static IEnumerable<int> ScrollSearchSlots()
    {
        for (int i = 0; i < InventoryConstants.HaveMax; i++) yield return InventoryConstants.InventoryStart + i;
        for (int i = 0; i < InventoryConstants.MagicBagTotal; i++) yield return InventoryConstants.MagicBagStart + i;
    }

    private static int EffectInt(SkillData.Skill s, string key) =>
        s.Effect.TryGetValue(key, out var value) ? value : 0;

    private void RefreshRows()
    {
        rowBox.Visible = OffersScrolls;
        for (int i = 0; i < rows.Count; i++)
        {
            var button = rows[i];
            button.Visible = OffersScrolls && i <= scrolls.Count;
            if (!button.Visible) continue;
            button.Text = i == TownRow
                ? GameText.System(TextReturnToTown, "Returning to Town or a designated re-spawn point.")
                : GameText.System(TextRespawnWith, "Re-spawning with %s.", scrolls[i - 1].Name);
            var colour = i == pick ? RowPicked : RowIdle;
            button.AddThemeColorOverride("font_color", colour);
            button.AddThemeColorOverride("font_hover_color", colour);
            button.AddThemeColorOverride("font_pressed_color", colour);
        }
    }

    private void OnRowInput(int row, InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click) return;
        pick = row;
        RefreshRows();
        if (click.DoubleClick) ConfirmChoice();
    }

    private void DeathDialogTick()
    {
        if (!Layer.Visible || double.IsPositiveInfinity(autoRespawnAt)) return;
        double left = autoRespawnAt - context.Now;
        hintLbl.Text = GameText.System(TextAutoRespawn, "You will be resurrected in the village %0.0f seconds after.")
            .Replace("%0.0f", Mathf.Max(0, Mathf.CeilToInt((float)left)).ToString());
        if (left > 0) return;
        autoRespawnAt = double.PositiveInfinity;
        ConfirmRespawn();
    }

    internal void Hide()
    {
        ExpLost = 0;
        respawnPending = false;
        respawnToken++;
        scrolls.Clear();
        autoRespawnAt = double.PositiveInfinity;
        Layer.Visible = false;
    }

    internal void ArmRespawnButton(string hint)
    {
        respawnPending = false;
        okBtn.Disabled = false;
        hintLbl.Text = hint;
    }

    internal void MarkRespawnPending()
    {
        respawnPending = true;
        okBtn.Disabled = true;
        hintLbl.Text = RespawnSentHint;
    }

    private void ConfirmChoice()
    {
        if (!context.Self.Dead) { Hide(); return; }
        if (respawnPending) return;
        if (!OffersScrolls)
        {
            ConfirmRespawn();
            return;
        }
        if (pick == TownRow)
        {
            Notice.Confirm(context.Root, GameText.System(TextTownWithoutScroll, "Do you wish to respawn at the town without using resurrection scroll?"),
                "Yes", "No", ConfirmRespawn);
            return;
        }
        UseResurrectionScroll(scrolls[pick - 1]);
    }

    private void UseResurrectionScroll(ScrollSkill scroll)
    {
        MarkRespawnPending();
        int token = ++respawnToken;
        combatRequests.Magic(MagicSub.Effecting, scroll.SkillId, self.MyCharId);
        context.Root.After(RespawnRetrySeconds, () =>
        {
            if (token != respawnToken || !context.Self.Dead) return;
            ArmRespawnButton(RespawnRetryHint);
        });
    }

    private void ConfirmRespawn()
    {
        if (!context.Self.Dead) { Hide(); return; }
        if (respawnPending) return;

        MarkRespawnPending();

        int token = ++respawnToken;
        combatRequests.Regene();

        context.Root.After(RespawnRetrySeconds, () =>
        {
            if (token != respawnToken || !context.Self.Dead) return;
            ArmRespawnButton(RespawnRetryHint);
        });
    }
}
