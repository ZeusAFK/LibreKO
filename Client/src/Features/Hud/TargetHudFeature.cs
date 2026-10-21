using Godot;
using LibreKO.Features.PluginBridge;

namespace LibreKO.Features.Hud;

internal sealed class TargetHudFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly ICombatRequests combatRequests;
    private readonly SiegeTowerSystem siegeTower;
    private readonly EntityPlatesSystem entityPlates;
    private readonly EntitySystem entities;
    private readonly TargetEffectsFeature targetEffects;
    private readonly TargetQuestDropsFeature targetQuestDrops;
    private readonly LootFilterFeature lootFilter;
    private readonly ClanMarksFeature clanMarks;
    private readonly SelectionFeature selection;
    private readonly PluginBridgeFeature pluginBridge;

    public TargetHudFeature(IWorldContext context, ICombatRequests combatRequests, SiegeTowerSystem siegeTower, EntityPlatesSystem entityPlates, EntitySystem entities, TargetEffectsFeature targetEffects, TargetQuestDropsFeature targetQuestDrops, LootFilterFeature lootFilter, ClanMarksFeature clanMarks, SelectionFeature selection, PluginBridgeFeature pluginBridge)
    {
        this.context = context;
        this.combatRequests = combatRequests;
        this.siegeTower = siegeTower;
        this.entityPlates = entityPlates;
        this.entities = entities;
        this.targetEffects = targetEffects;
        this.targetQuestDrops = targetQuestDrops;
        this.lootFilter = lootFilter;
        this.clanMarks = clanMarks;
        this.selection = selection;
        this.pluginBridge = pluginBridge;
    }

    public void Build()
    {
        BuildTargetHud();
        entities.EntityHpChanged += OnEntityHpChanged;
    }

    public void Dispose() => entities.EntityHpChanged -= OnEntityHpChanged;

    private void OnEntityHpChanged(EntityView e)
    {
        if (e.IsNpc || e.Attackable) NoteHit(e.Id, context.Now);
    }

    internal VBoxContainer TargetBox { get; private set; } = null!;

    internal Label TargetName { get; private set; } = null!;

    internal StatBar TargetHp { get; private set; } = null!;

    private const float TargetHudBarHeight = 22f;

    private const float TargetCloseSize = 26f;

    internal void BuildTargetHud()
    {
        var layer = new CanvasLayer { Layer = UiLayers.Hud };
        context.Root.AddChild(layer);
        context.Hud.PluginSeam(layer, LibreKO.Plugins.HudPart.TargetFrame);
        TargetBox = new VBoxContainer { Visible = false };
        TargetBox.AddThemeConstantOverride("separation", 3);
        layer.AddChild(TargetBox);
        TargetName = HudStyle.Label(16, HorizontalAlignment.Center);
        if (Platform.TouchUi)
        {
            TargetBox.AddChild(BuildTargetHead());
        }
        else
        {
            TargetName.CustomMinimumSize = new Vector2(UiKit.TargetHudWidth, 0);
            TargetBox.AddChild(TargetName);
        }
        TargetBox.AddChild(BuildTargetClanRow());
        TargetHp = new StatBar(new Color("c0392b"),
                                new Vector2(UiKit.TargetHudWidth, TargetHudBarHeight)) { Glide = true };
        TargetBox.AddChild(TargetHp);
        targetEffects.BuildRow(TargetBox);
        targetQuestDrops.BuildRow(TargetBox);
        lootFilter.BuildDropListUi(TargetBox);
        siegeTower.BuildTowerButton(TargetBox);
        HudLayout.Attach(TargetBox, "hud_target", TargetName,
            () =>
            {
                float vw = context.Root.GetViewport().GetVisibleRect().Size.X;
                return new Vector2(Mathf.Max(0f, (vw - UiKit.TargetHudWidth) * 0.5f), 10f);
            },
            anchor: HudPlacement.TargetAnchor, anchorMargin: HudPlacement.TargetMargin,
            anchorSize: new Vector2(UiKit.TargetHudWidth, 0f));
        if (Platform.TouchUi)
        {
            TargetBox.Scale = Vector2.One * HudPlacement.TargetScale;
            TargetBox.PivotOffset = new Vector2(UiKit.TargetHudWidth * 0.5f, 0f);
        }
    }

    private const float TargetClanMarkSize = 16f;

    private HBoxContainer targetClanRow = null!;

    private TextureRect targetClanMark = null!;

    private Label targetClanText = null!;

    private Control BuildTargetClanRow()
    {
        targetClanRow = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            CustomMinimumSize = new Vector2(UiKit.TargetHudWidth, 0),
            Visible = false,
        };
        targetClanRow.AddThemeConstantOverride("separation", 4);
        targetClanMark = new TextureRect
        {
            CustomMinimumSize = new Vector2(TargetClanMarkSize, TargetClanMarkSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        targetClanRow.AddChild(targetClanMark);
        targetClanText = HudStyle.Label(12, HorizontalAlignment.Center);
        targetClanText.AddThemeColorOverride("font_color", NamePlate.ClanColor);
        targetClanRow.AddChild(targetClanText);
        return targetClanRow;
    }

    private void SyncTargetClanRow(EntityView e)
    {
        bool clan = !e.IsNpc && e.KnightsId > 0 && e.ClanName.Length > 0;
        if (targetClanRow.Visible != clan) targetClanRow.Visible = clan;
        if (!clan) return;
        string text = $"{e.ClanName}  ·  {ClanTypes.Standing(e.ClanFlag, (byte)e.ClanGrade)}";
        if (targetClanText.Text != text) targetClanText.Text = text;
        var mark = clanMarks.Mark(e.KnightsId, e.ClanMarkVersion);
        if (targetClanMark.Texture != mark) targetClanMark.Texture = mark;
        targetClanMark.Visible = mark != null;
    }

    private Control BuildTargetHead()
    {
        var head = new HBoxContainer { CustomMinimumSize = new Vector2(UiKit.TargetHudWidth, 0) };
        head.AddThemeConstantOverride("separation", 0);
        head.AddChild(new Control { CustomMinimumSize = new Vector2(TargetCloseSize, 0) });

        TargetName.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        TargetName.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(TargetName);

        var close = new Button
        {
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(TargetCloseSize, TargetCloseSize),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            TooltipText = "Clear target",
        };
        foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
            close.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        var glyph = new CloseGlyph { MouseFilter = Control.MouseFilterEnum.Ignore };
        glyph.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        close.AddChild(glyph);
        close.Pressed += selection.Deselect;
        head.AddChild(close);
        return head;
    }

    private int lastHitId = -1;

    private double lastHitAt;

    private void NoteHit(int id, double at)
    {
        lastHitId = id;
        lastHitAt = at;
    }

    private const double LastHitHudDur = 8.0;

    private const double TargetHpPollInterval = 0.5;

    private double targetHpPollAt;

    private int polledId = -1;

    internal void TargetHpPollTick(double now)
    {
        if (selection.SelectedId != polledId)
        {
            polledId = selection.SelectedId;
            targetHpPollAt = 0;
        }
        if (selection.SelectedId < 0 || now < targetHpPollAt || Game.Connection.Reconnect.InProgress) return;
        targetHpPollAt = now + TargetHpPollInterval;
        if (context.Entities.Views.TryGetValue(selection.SelectedId, out var e) && !e.Dead)
            combatRequests.TargetHpRequest(selection.SelectedId);
    }

    internal int TargetHudId { get; private set; } = -1;

    private int targetHudLevel;

    private string? targetHudName;

    private bool targetBoxShown;

    internal void UpdateTargetHud()
    {
        int show = selection.SelectedId >= 0 && context.Entities.Views.ContainsKey(selection.SelectedId)
            ? selection.SelectedId
            : (lastHitId >= 0 && context.Now <= lastHitAt + LastHitHudDur ? lastHitId : -1);
        if (show >= 0 && context.Entities.Views.TryGetValue(show, out var e))
        {
            string shownName = entityPlates.ShownName(e);
            if (show != TargetHudId || e.Level != targetHudLevel || shownName != targetHudName)
            {
                if (show != TargetHudId) TargetHp.SnapNext();
                TargetHudId = show;
                targetHudLevel = e.Level;
                targetHudName = shownName;
                TargetName.Text = e.Level > 0 ? $"{shownName}   Lv {e.Level}" : shownName;
            }
            SyncTargetClanRow(e);
            if (e.MaxHp > 0) TargetHp.Set(e.Hp, e.MaxHp); else TargetHp.SetFull();
            if (!targetBoxShown) { targetBoxShown = true; TargetBox.Visible = true; }
        }
        else if (targetBoxShown)
        {
            targetBoxShown = false;
            TargetHudId = -1;
            TargetBox.Visible = false;
        }
        lootFilter.SyncDropListButton(TargetHudId);
        targetQuestDrops.Sync(selection.SelectedId);
        siegeTower.SyncTowerButton(TargetHudId);
        pluginBridge.NotifyTarget();
    }
}
