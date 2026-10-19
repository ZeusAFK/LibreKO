using Godot;

namespace LibreKO.Features.Entities;

internal sealed class ZoneAbilityFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly EntitiesSession entities;
    private readonly ZoneAbilitySession zoneAbility;
    private readonly HostilityRules hostility;
    private readonly SiegeSession siege;

    public ZoneAbilityFeature(IWorldContext context, EntitiesSession entities, ZoneAbilitySession zoneAbility, HostilityRules hostility, SiegeSession siege)
    {
        this.context = context;
        this.entities = entities;
        this.zoneAbility = zoneAbility;
        this.hostility = hostility;
        this.siege = siege;
    }

    private CanvasLayer zoneabilityLayer = null!;
    internal PanelContainer ZoneabilityChip { get; private set; } = null!;
    private StyleBoxFlat zoneabilityStyle = null!;
    private Label zoneabilityTypeLbl = null!;
    private bool zoneabilityHavePrev;
    private ZoneAbilityType zoneabilityPrevType;

    private const float TopGutter = MiniMap.SquareSize + 22f;

    public void Build()
    {
        siege.SiegeWarEvent += OnSiegeWar;
        BuildChip();
        zoneAbility.ZoneAbilityEvent += On;
        Apply(zoneAbility.CurrentZoneAbility, announce: false);
    }

    public void Dispose()
    {
        siege.SiegeWarEvent -= OnSiegeWar;
        zoneAbility.ZoneAbilityEvent -= On;
    }

    private void OnSiegeWar() => RefreshPlayerHostility(zoneAbility.CurrentZoneAbility);

    internal void BuildChip()
    {
        zoneabilityLayer = new CanvasLayer { Layer = UiLayers.Hud };
        context.Root.AddChild(zoneabilityLayer);

        ZoneabilityChip = new PanelContainer
        {
            AnchorLeft = 1f, AnchorRight = 1f,
            GrowHorizontal = Control.GrowDirection.Begin,
            OffsetRight = -16f, OffsetTop = TopGutter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        zoneabilityStyle = UiTheme.Chip();
        ZoneabilityChip.AddThemeStyleboxOverride("panel", zoneabilityStyle);
        zoneabilityLayer.AddChild(ZoneabilityChip);

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        ZoneabilityChip.AddChild(row);

        zoneabilityTypeLbl = UiTheme.Text("Safe Zone", 13, UiTheme.Good, HorizontalAlignment.Center);
        zoneabilityTypeLbl.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(zoneabilityTypeLbl);

        HudLayout.Attach(ZoneabilityChip, "hud_zone_ability", ZoneabilityChip, () => ZoneabilityChip.Position);
    }

    private void On(ZoneAbilityInfo info) => Apply(info, announce: true);

    internal void RefreshPlayerHostility(ZoneAbilityInfo info)
    {
        foreach (var kv in context.Entities.Views)
        {
            var e = kv.Value;
            bool hostile = e.IsNpc
                ? entities.IsKnownAttackable(kv.Key)
                : hostility.IsHostilePlayer(e.Nation, e.KnightsId, e.AllianceId)
                  || hostility.AtClanWarWith(e.KnightsId);

            if (hostile == e.Attackable) continue;

            e.Attackable = hostile;
            NpcGatesFeature.RefreshGateBlocker(e);
            if (context.Selection.SelectedId == kv.Key) context.Selection.Select(kv.Key);
        }
    }

    internal void Apply(ZoneAbilityInfo info, bool announce)
    {
        RefreshPlayerHostility(info);

        var col = info.ZoneType == ZoneAbilityType.Neutral
            ? UiTheme.Good
            : info.IsSiege ? UiTheme.Neutral : UiTheme.Bad;
        zoneabilityTypeLbl.Text = info.TypeLabel;
        zoneabilityTypeLbl.AddThemeColorOverride("font_color", col);
        ZoneabilityChip.Visible = info.IsPvp;

        zoneabilityStyle.BorderColor = new Color(col, 0.7f);

        bool arenaStep = info.IsArena
            || (zoneabilityPrevType == ZoneAbilityType.FreeForAll
                && info.ZoneType == ZoneAbilityType.Neutral);

        if (announce && !arenaStep && (!zoneabilityHavePrev || zoneabilityPrevType != info.ZoneType))
        {
            context.Log.Notice(info.ZoneType == ZoneAbilityType.Neutral
                ? "You have entered a safe zone. PK is disabled here."
                : info.IsSiege
                    ? "You have entered a siege zone."
                    : "You have entered a PK zone. Watch your back.");
        }
        zoneabilityHavePrev = true;
        zoneabilityPrevType = info.ZoneType;
    }
}
