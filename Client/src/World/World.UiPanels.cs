using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private static readonly Vector2 KnightCashHudPosition = new(228f, 11f);
    private PanelContainer _infoPanel = null!;
    private HFlowContainer _infoTabButtons = null!;
    private MarginContainer _infoTabContent = null!;
    private ButtonGroup _infoTabGroup = null!;
    private readonly Dictionary<string, Control> _infoTabPanels = new();
    private readonly Dictionary<string, Button> _infoTabBtns = new();
    private RichTextLabel _infoText = null!;
    private Label _pickCandLabel = null!;
    private bool _infoShown;
    private ulong _infoNextRebuild;

    private LoadingScreen? _loadingLayer;
    private CanvasLayer _escLayer = null!;
    private bool _escShown;
    private CanvasLayer _hudEditLayer = null!;
    private bool _hudEditMode;

    private LevelOrb _orb = null!;
    private StatBar _hpBar = null!, _mpBar = null!;

    private MiniMap _miniMap = null!;
    private readonly List<MiniMap.Blip> _blipScratch = new();
    private const float QuestTargetBlipRadius = 4.5f;
    private Label _kcLabel = null!;

    private static readonly Vector2 StatusHudPos = new(HudAnchor.Edge, 10f);
    private static readonly Vector2 StatusHudSize = new(362f, 104f);
    private static readonly Vector2 HpBarPos = new(84f, 28f);
    private static readonly Vector2 HpBarSize = new(274f, 25f);
    private static readonly Vector2 MpBarPos = new(93f, 55f);
    private static readonly Vector2 MpBarSize = new(265f, 17f);
    private const float StatusHudGap = 6f;

    private static readonly Vector2 TouchStatusSize = new(362f, 48f);
    private static readonly Vector2 TouchHpBarPos = new(0f, 0f);
    private static readonly Vector2 TouchHpBarSize = new(362f, 26f);
    private static readonly Vector2 TouchMpBarPos = new(0f, 30f);
    private static readonly Vector2 TouchMpBarSize = new(362f, 18f);

    private static Vector2 VitalsSize => Platform.TouchUi ? TouchStatusSize : StatusHudSize;

    private const int HudLayerIndex = 64;

    private Control _statusRoot = null!;

    private readonly Dictionary<LibreKO.Plugins.HudPart, Control> _pluginHud = new();

    private void PluginHudSeam(CanvasLayer nativeLayer, LibreKO.Plugins.HudPart part)
    {
        if (!PluginHost.Ui.HudHidden(part)) return;
        nativeLayer.Visible = false;
        if (PluginHost.Ui.HudReplacement(part) is not { } build) return;
        var layer = new CanvasLayer { Layer = nativeLayer.Layer };
        AddChild(layer);
        var control = build();
        layer.AddChild(control);
        _pluginHud[part] = control;
    }

    private void BuildStatusHud()
    {
        var layer = new CanvasLayer { Layer = HudLayerIndex };
        AddChild(layer);
        PluginHudSeam(layer, LibreKO.Plugins.HudPart.StatusBars);

        var status = new Control
        {
            Position = StatusHudPos,
            CustomMinimumSize = VitalsSize,
        };
        layer.AddChild(status);
        _statusRoot = status;

        bool touch = Platform.TouchUi;
        _hpBar = new StatBar(new Color("d51f20"),
                             touch ? TouchHpBarSize : HpBarSize,
                             touch ? StatBar.RibbonKind.Plain : StatBar.RibbonKind.Upper)
        {
            Position = touch ? TouchHpBarPos : HpBarPos,
        };
        status.AddChild(_hpBar);
        _mpBar = new StatBar(new Color("246fd0"),
                             touch ? TouchMpBarSize : MpBarSize,
                             touch ? StatBar.RibbonKind.Plain : StatBar.RibbonKind.Lower)
        {
            Position = touch ? TouchMpBarPos : MpBarPos,
        };
        status.AddChild(_mpBar);

        _kcLabel = HudStyle.Label(11, HorizontalAlignment.Right);
        _kcLabel.Position = KnightCashHudPosition;
        _kcLabel.AddThemeColorOverride("font_color", UiTheme.GoldBright);
        _kcLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _kcLabel.AddThemeConstantOverride("outline_size", 2);
        status.AddChild(_kcLabel);

        _orb = new LevelOrb(92f) { Position = new Vector2(8f, 7f), Visible = !touch };
        status.AddChild(_orb);

        _orb.Set(Sheet.Level, Sheet.ExpPercent);
        _hpBar.Set(Vitals.Hp, Vitals.MaxHp);
        _mpBar.Set(Vitals.Mp, Vitals.MaxMp);
        UpdateStatusHud();

        HudLayout.Attach(status, "hud_status", status, () => StatusHudPos,
            anchor: HudPlacement.StatusAnchor, anchorMargin: HudPlacement.StatusMargin,
            anchorSize: VitalsSize);
    }

    private void ApplyHudTheme()
    {
        var theme = HudTheme.Shared;
        foreach (var child in GetChildren())
        {
            if (child is not CanvasLayer layer) continue;
            foreach (var node in layer.GetChildren())
                if (node is Control ctrl && ctrl.Theme == null)
                    ctrl.Theme = theme;
        }
    }

    private void BuildMiniMap()
    {
        var layer = new CanvasLayer { Layer = 64 };
        AddChild(layer);
        PluginHudSeam(layer, LibreKO.Plugins.HudPart.MiniMap);

        _miniMap = new MiniMap { MouseFilter = Control.MouseFilterEnum.Stop };
        layer.AddChild(_miniMap);
        HudPlacement.MiniMap.ApplyTo(_miniMap);

        string stem = _terrain != null ? _terrain.ZoneStem : ZoneCatalog.Stem(_zone) ?? "";
        _miniMap.SetZone(stem, MapName(_zone));
    }

    private void ToggleMiniMap()
    {
        if (_pluginHud.TryGetValue(LibreKO.Plugins.HudPart.MiniMap, out var themed))
        {
            themed.Visible = !themed.Visible;
            return;
        }
        if (_miniMap != null) _miniMap.Visible = !_miniMap.Visible;
    }

    private void UpdateMiniMap()
    {
        if (_miniMap == null) return;
        _blipScratch.Clear();
        foreach (var kv in _ents)
        {
            var e = kv.Value;
            if (e.Body == null) continue;
            var gp = e.Body.GlobalPosition;
            var (kx, kz) = Coord.ToKo(gp.X, gp.Z);
            Color col;
            float rad;
            if (e.IsNpc && e.Attackable) { col = UiTheme.Bad; rad = 2.6f; }
            else if (e.IsNpc) { col = UiTheme.Neutral; rad = 3f; }
            else { col = UiTheme.Good; rad = 3f; }
            if (e.Dead) col = col.Darkened(0.5f);
            _blipScratch.Add(new MiniMap.Blip(kx, kz, col, rad, false));
            if (e.IsNpc && !e.Dead && _questTargetNpcs.Contains(e.NpcId))
                _blipScratch.Add(new MiniMap.Blip(kx, kz, UiTheme.GoldBright, QuestTargetBlipRadius, true));
            if (kv.Key == _selectedId)
                _blipScratch.Add(new MiniMap.Blip(kx, kz, UiTheme.TextHi, 5.5f, true));
        }
        float heading = Coord.KoHeading(Mathf.Sin(_camYaw), -Mathf.Cos(_camYaw));
        _miniMap.UpdateView(_myKoX, _myKoZ, heading, _blipScratch);
        PluginNotifyMap(heading);
    }

    private void UpdateStatusHud()
    {
        if (_kcLabel == null || !IsInstanceValid(_kcLabel)) return;
        _kcLabel.Text = $"KC {Sheet.KnightCash:n0}";
    }

    private static string MapName(int zone) => ZoneCatalog.Name(zone);

    private static Label3D NameLabel(string name, float y) => NamePlate.Make(name, y);

}
