using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private enum CombatLogKind { Damage, Outgoing, Incoming, Recovery, Resource, Status }

    private PanelContainer _combatLogRoot = null!;
    private HudLogText _combatLogText = null!;
    private StyleBoxFlat _combatLogPanelStyle = null!;
    private readonly Queue<string> _combatLogLines = new();
    private const int CombatLogMaxLines = 120;

    private void BuildCombatLog()
    {
        var layer = new CanvasLayer { Layer = 66 };
        AddChild(layer);

        _combatLogRoot = new PanelContainer
        {
            Size = new Vector2(420, 205),
            CustomMinimumSize = new Vector2(290, 135),
        };
        _combatLogPanelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.50f),
            BorderColor = new Color(UiTheme.Edge, 0.46f),
        };
        _combatLogPanelStyle.SetBorderWidthAll(1);
        _combatLogPanelStyle.SetCornerRadiusAll(4);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            _combatLogPanelStyle.Set($"content_margin_{side}", 7f);
        _combatLogRoot.AddThemeStyleboxOverride("panel", _combatLogPanelStyle);
        layer.AddChild(_combatLogRoot);

        _combatLogText = new HudLogText
        {
            ScrollbarOnLeft = false,
            BbcodeEnabled = true,
            ScrollActive = true,
            ScrollFollowing = true,
            FitContent = false,
            SelectionEnabled = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _combatLogText.AddThemeFontSizeOverride("normal_font_size", 12);
        _combatLogText.AddThemeColorOverride("default_color", new Color("#d4d1c9"));
        _combatLogRoot.AddChild(_combatLogText);

        AttachCombatLogLayout(
            _combatLogRoot,
            () =>
            {
                Vector2 vp = GetViewport().GetVisibleRect().Size;
                return new Vector2(Mathf.Max(0f, vp.X - 432f), Mathf.Max(0f, vp.Y - 282f));
            });

        _combatLogRoot.Visible = Config.CombatLog;
        Config.EffectsChanged += ApplyCombatLogVisibility;
    }

    private void CombatLogDispose() => Config.EffectsChanged -= ApplyCombatLogVisibility;

    private void ApplyCombatLogVisibility()
    {
        if (_combatLogRoot != null && GodotObject.IsInstanceValid(_combatLogRoot))
            _combatLogRoot.Visible = Config.CombatLog;
    }

    private void AttachCombatLogLayout(Control target, Func<Vector2> defaultPosition, bool persist = true)
    {
        target.Modulate = Colors.White;
        HudLayout.Attach(
            target, persist ? "hud_combat_log" : "uilab_actual_combat_log", null, defaultPosition,
            resizable: true,
            defaultSize: new Vector2(420, 205),
            minimumSize: new Vector2(290, 135),
            persist: persist,
            resizeCorner: HudLayout.Corner.TopLeft,
            moveCorner: HudLayout.Corner.BottomRight,
            moveGripAlwaysVisible: true,
            backgroundOpacityChanged: alpha =>
            {
                _combatLogPanelStyle.BgColor = new Color(0, 0, 0, alpha);
                GD.Print($"[hud] combat black background opacity={alpha:0.00}");
            });
    }

    private void CombatLogAdd(string message, CombatLogKind kind)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        string color = kind switch
        {
            CombatLogKind.Damage => "ffffff",
            CombatLogKind.Outgoing => "f2c45e",
            CombatLogKind.Incoming => "f07870",
            CombatLogKind.Recovery => "79d892",
            CombatLogKind.Resource => "70aee8",
            _ => "aaa79f",
        };
        _combatLogLines.Enqueue($"[color=#{color}]{BbCode.Esc(message)}[/color]");
        while (_combatLogLines.Count > CombatLogMaxLines)
            _combatLogLines.Dequeue();
        if (_combatLogText != null)
            _combatLogText.Text = string.Join("\n", _combatLogLines);
    }

    private void CombatNotice(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Floaters?.Notice(message);
        CombatLogAdd(message, CombatLogKind.Status);
    }

    private string CombatEntityName(int id)
    {
        if (id == _myId) return "You";
        return _ents.TryGetValue(id, out var e) && !string.IsNullOrWhiteSpace(e.Name)
            ? e.Name
            : "Unknown";
    }
}
