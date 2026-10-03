using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const float NestTimerGap = 6f;
    private const string NestTimerFallbackTitle = "Monster Stone";
    private const string DrakiTimerTitle = "Draki's Tower";

    private CanvasLayer? _nestTimerLayer;
    private PanelContainer? _nestTimerPanel;
    private Label? _nestTimerTitle;
    private Label? _nestTimerDone;
    private Label? _nestTimerLine;
    private string _nestTimerText = "";

    private void NestTimerInit()
    {
        if (!NestDungeon.IsNestZone(_zone)) Net.I.ClearNestTimer();
    }

    private void NestTimerDispose()
    {
        if (_nestTimerLayer != null && IsInstanceValid(_nestTimerLayer)) _nestTimerLayer.QueueFree();
        _nestTimerLayer = null;
        _nestTimerPanel = null;
    }

    private void EnsureNestTimer()
    {
        if (_nestTimerPanel != null) return;

        _nestTimerLayer = new CanvasLayer { Layer = 78 };
        AddChild(_nestTimerLayer);

        var anchor = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        anchor.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _nestTimerLayer.AddChild(anchor);

        _nestTimerPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _nestTimerPanel.AddThemeStyleboxOverride("panel", QuestToastStyle());
        anchor.AddChild(_nestTimerPanel);

        var rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 6);
        _nestTimerPanel.AddChild(rows);
        rows.AddChild(QuestToastRule());

        var text = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("margin_left", 58);
        text.AddThemeConstantOverride("margin_right", 58);
        rows.AddChild(text);

        var lines = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        lines.AddThemeConstantOverride("separation", 3);
        text.AddChild(lines);

        _nestTimerTitle = NestTimerLabel(14, UiTheme.Gold);
        _nestTimerDone = NestTimerLabel(16, UiTheme.Good);
        _nestTimerLine = NestTimerLabel(16, UiTheme.TextHi);
        lines.AddChild(_nestTimerTitle);
        lines.AddChild(_nestTimerDone);
        lines.AddChild(_nestTimerLine);
        rows.AddChild(QuestToastRule());

        _nestTimerPanel.Resized += CentreNestTimer;
    }

    private static Label NestTimerLabel(int size, Color color)
    {
        var label = UiTheme.Text("", size, color, HorizontalAlignment.Center);
        label.AddThemeConstantOverride("font_embolden", 1);
        label.AddThemeConstantOverride("outline_size", 4);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        return label;
    }

    private void CentreNestTimer()
    {
        if (_nestTimerPanel?.GetParent() is not Control anchor) return;
        _nestTimerPanel.Position = new Vector2(
            Mathf.Round((anchor.Size.X - _nestTimerPanel.Size.X) * 0.5f), QuestToastTop);
    }

    private float NestTimerOffset() =>
        _nestTimerPanel is { Visible: true } panel ? panel.Size.Y + NestTimerGap : 0f;

    private void TickNestTimer()
    {
        int draki = Net.I.DrakiSecondsLeft;
        if (draki > 0)
        {
            ShowTimerPlate(Localization.Loc.Tr(DrakiTimerTitle), $"{Localization.Loc.Tr("Stage")} {Net.I.DrakiStage}-{Net.I.DrakiSubStage}",
                           $"{draki / 60}:{draki % 60:00}");
            return;
        }
        int left = NestDungeon.IsNestZone(_zone) ? Net.I.NestSecondsLeft : 0;
        if (left <= 0)
        {
            if (_nestTimerPanel is { Visible: true }) _nestTimerPanel.Visible = false;
            return;
        }
        ShowNestTimer(Net.I.NestStoneItemId, left, Net.I.NestCompleted);
    }

    private void ShowNestTimer(int stoneItemId, int secondsLeft, bool completed)
    {
        string title = stoneItemId != 0 ? ItemData.DisplayName(stoneItemId) : Localization.Loc.Tr(NestTimerFallbackTitle);
        ShowTimerPlate(title, completed ? NestDungeon.CompletedLine : null,
                       NestDungeon.TerminationLine(secondsLeft, completed));
    }

    private void ShowTimerPlate(string title, string? middle, string line)
    {
        EnsureNestTimer();
        bool showMiddle = middle != null;
        string key = $"{title}|{middle}|{line}";
        if (key != _nestTimerText)
        {
            _nestTimerText = key;
            _nestTimerTitle!.Text = $"<< {title} >>";
            _nestTimerDone!.Text = middle ?? "";
            _nestTimerDone.Visible = showMiddle;
            _nestTimerLine!.Text = line;
            _nestTimerPanel!.ResetSize();
            CentreNestTimer();
        }
        if (!_nestTimerPanel!.Visible) _nestTimerPanel.Visible = true;
    }
}
