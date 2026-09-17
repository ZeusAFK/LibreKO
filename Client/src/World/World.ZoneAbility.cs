using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _zoneabilityLayer = null!;
    private PanelContainer _zoneabilityChip = null!;
    private StyleBoxFlat _zoneabilityStyle = null!;
    private Label _zoneabilityTypeLbl = null!;
    private bool _zoneabilityHavePrev;
    private byte _zoneabilityPrevType;

    private const float ZoneAbilityTopGutter = MiniMap.SquareSize + 22f;

    private void ZoneAbilityInit()
    {
        BuildZoneAbilityChip();
        Net.I.ZoneAbilityEvent += OnZoneAbility;
        ApplyZoneAbility(Net.I.CurrentZoneAbility, announce: false);
    }

    private void ZoneAbilityDispose()
    {
        Net.I.ZoneAbilityEvent -= OnZoneAbility;
    }

    private void BuildZoneAbilityChip()
    {
        _zoneabilityLayer = new CanvasLayer { Layer = 64 };
        AddChild(_zoneabilityLayer);

        _zoneabilityChip = new PanelContainer
        {
            AnchorLeft = 1f, AnchorRight = 1f,
            GrowHorizontal = Control.GrowDirection.Begin,
            OffsetRight = -16f, OffsetTop = ZoneAbilityTopGutter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _zoneabilityStyle = UiTheme.Chip();
        _zoneabilityChip.AddThemeStyleboxOverride("panel", _zoneabilityStyle);
        _zoneabilityLayer.AddChild(_zoneabilityChip);

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _zoneabilityChip.AddChild(row);

        _zoneabilityTypeLbl = UiTheme.Text("Safe Zone", 13, UiTheme.Good, HorizontalAlignment.Center);
        _zoneabilityTypeLbl.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(_zoneabilityTypeLbl);

        HudLayout.Attach(_zoneabilityChip, "hud_zone_ability", _zoneabilityChip, () => _zoneabilityChip.Position);
    }

    private void OnZoneAbility(ZoneAbilityInfo info) => ApplyZoneAbility(info, announce: true);

    private void RefreshPlayerHostility(ZoneAbilityInfo info)
    {
        foreach (var kv in _ents)
        {
            var e = kv.Value;
            bool hostile = e.IsNpc
                ? Net.I.IsKnownAttackable(kv.Key)
                : info.IsHostilePlayer(Net.I.Nation, e.Nation);

            if (hostile == e.Attackable) continue;

            e.Attackable = hostile;
            RefreshEntityCollision(e);
            if (_selectedId == kv.Key) Select(kv.Key, e);
        }
    }

    private void ApplyZoneAbility(ZoneAbilityInfo info, bool announce)
    {
        RefreshPlayerHostility(info);

        var col = info.ZoneType == ZoneAbilityInfo.Neutral
            ? UiTheme.Good
            : info.IsSiege ? UiTheme.Neutral : UiTheme.Bad;
        _zoneabilityTypeLbl.Text = info.TypeLabel;
        _zoneabilityTypeLbl.AddThemeColorOverride("font_color", col);
        _zoneabilityChip.Visible = info.IsPvp;

        _zoneabilityStyle.BorderColor = new Color(col, 0.7f);

        bool arenaStep = info.IsArena
            || (_zoneabilityPrevType == ZoneAbilityInfo.FreeForAll
                && info.ZoneType == ZoneAbilityInfo.Neutral);

        if (announce && !arenaStep && (!_zoneabilityHavePrev || _zoneabilityPrevType != info.ZoneType))
        {
            CombatNotice(info.ZoneType == ZoneAbilityInfo.Neutral
                ? "You have entered a safe zone. PK is disabled here."
                : info.IsSiege
                    ? "You have entered a siege zone."
                    : "You have entered a PK zone. Watch your back.");
        }
        _zoneabilityHavePrev = true;
        _zoneabilityPrevType = info.ZoneType;
    }
}
