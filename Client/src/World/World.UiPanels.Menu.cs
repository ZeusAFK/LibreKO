using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private void BuildEscMenu()
    {
        _escLayer = new CanvasLayer { Layer = 110, Visible = false };
        AddChild(_escLayer);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.48f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _escLayer.AddChild(dim);
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _escLayer.AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(300, 0) };
        panel.AddThemeStyleboxOverride("panel", UiTheme.WindowPanel(4));
        center.AddChild(panel);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 0);
        panel.AddChild(root);

        var header = new PanelContainer();
        header.AddThemeStyleboxOverride("panel", UiTheme.HeaderBand(4));
        root.AddChild(header);
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 8);
        header.AddChild(headerRow);
        var title = UiTheme.Text("Menu", 18, UiTheme.TextHi);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        headerRow.AddChild(title);

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 12, 12, 12, 14);
        root.AddChild(margin);

        var vb = new VBoxContainer();
        vb.AddThemeConstantOverride("separation", 6);
        margin.AddChild(vb);

        AddEscButton(vb, "Resume", () => ToggleEsc(false), "Return to the world");
        AddEscButton(vb, "Change Character", () =>
        {
            Net.I.ReturnToCharSelect();
            GetTree().ChangeSceneToFile("res://scenes/CharSelect.tscn");
        }, "Return to character selection");
        AddEscButton(vb, "Settings", () => { ToggleEsc(false); SettingsPanel.Open(this); }, "Open graphics and game settings");
        AddEscButton(vb, "Exit", OnExitGame, "Close the game");

        _hudEditLayer = new CanvasLayer { Layer = 109, Visible = false };
        AddChild(_hudEditLayer);
        var editBanner = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            GrowHorizontal = Control.GrowDirection.Both,
            OffsetTop = 18,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        editBanner.AddThemeStyleboxOverride("panel", UiTheme.Chip());
        _hudEditLayer.AddChild(editBanner);
        var editText = UiTheme.Text(
            "HUD LAYOUT  ·  Drag the gold grips  ·  ESC to finish",
            13, UiTheme.GoldBright, HorizontalAlignment.Center);
        editText.MouseFilter = Control.MouseFilterEnum.Ignore;
        editBanner.AddChild(editText);
    }

    private static void AddEscButton(VBoxContainer vb, string text, System.Action onPressed, string tooltip = "")
    {
        var b = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(0, 42),
            Alignment = HorizontalAlignment.Left,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = tooltip,
        };
        b.AddThemeFontSizeOverride("font_size", 15);
        b.Pressed += onPressed;
        vb.AddChild(b);
    }

    private void ToggleEsc(bool? show = null)
    {
        _escShown = show ?? !_escShown;
        _escLayer.Visible = _escShown;
    }

    private void SetHudEditMode(bool enabled)
    {
        _hudEditMode = enabled;
        HudLayout.EditMode = enabled;
        if (_hudEditLayer != null) _hudEditLayer.Visible = enabled;
    }

    private void OnExitGame() => _ = Diag.Guard("world-exit", () => Shutdown.Begin(this, 200));

    private void BuildLoading()
    {
        _loadingLayer = new LoadingScreen();
        AddChild(_loadingLayer);
    }

    private async System.Threading.Tasks.Task<bool> LoadStep(string status, double progress, string detail = "")
    {
        Diag.Phase = $"world-load {progress * 100.0:0}% {status}";
        GD.Print($"[load] {progress * 100.0:0}% {status} (+{Now() - _loadStartedAt:0.0}s)" +
                 (string.IsNullOrWhiteSpace(detail) ? "" : $" — {detail}"));
        _loadingLayer?.Set(status, progress, detail);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!Alive) return false;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        return Alive;
    }

    private void HideLoading()
    {
        _loadingLayer?.QueueFree();
        _loadingLayer = null;
        Backdrop.RerollLoadingArt();
    }
}
