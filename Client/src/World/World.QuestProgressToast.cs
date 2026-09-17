using Godot;

namespace LibreKO;

public partial class World
{
    private const float QuestToastTop = 112f;
    private const double QuestToastSeconds = 3.2;
    private const double QuestToastFadeSeconds = 0.55;

    private CanvasLayer? _questToastLayer;
    private PanelContainer? _questToastPanel;
    private Label? _questToastTitle;
    private Label? _questToastLine;
    private double _questToastLeft;
    private string _questToastKey = "";

    private void EnsureQuestToast()
    {
        if (_questToastPanel != null) return;

        _questToastLayer = new CanvasLayer { Layer = 78 };
        AddChild(_questToastLayer);

        var anchor = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        anchor.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _questToastLayer.AddChild(anchor);

        _questToastPanel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0),
            Visible = false,
        };
        _questToastPanel.AddThemeStyleboxOverride("panel", QuestToastStyle());
        anchor.AddChild(_questToastPanel);

        var rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 6);
        _questToastPanel.AddChild(rows);
        rows.AddChild(QuestToastRule());

        var text = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("margin_left", 58);
        text.AddThemeConstantOverride("margin_right", 58);
        rows.AddChild(text);

        var lines = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        lines.AddThemeConstantOverride("separation", 3);
        text.AddChild(lines);

        _questToastTitle = UiTheme.Text("", 14, UiTheme.Gold, HorizontalAlignment.Center);
        _questToastTitle.AddThemeConstantOverride("font_embolden", 1);
        _questToastTitle.AddThemeConstantOverride("outline_size", 4);
        _questToastTitle.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        lines.AddChild(_questToastTitle);

        _questToastLine = UiTheme.Text("", 16, UiTheme.TextHi, HorizontalAlignment.Center);
        _questToastLine.AddThemeConstantOverride("font_embolden", 1);
        _questToastLine.AddThemeConstantOverride("outline_size", 4);
        _questToastLine.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        lines.AddChild(_questToastLine);
        rows.AddChild(QuestToastRule());

        _questToastPanel.Resized += CentreQuestToast;
    }

    private static StyleBoxTexture QuestToastStyle()
    {
        var ink = new Color(0.035f, 0.035f, 0.042f);
        var ramp = new Gradient
        {
            Offsets = [0f, 0.5f, 1f],
            Colors = [new Color(ink, 0f), new Color(ink, 0.92f), new Color(ink, 0f)],
        };
        var texture = new GradientTexture2D
        {
            Gradient = ramp,
            Width = 256,
            Height = 4,
            Fill = GradientTexture2D.FillEnum.Linear,
            FillFrom = new Vector2(0, 0.5f),
            FillTo = new Vector2(1, 0.5f),
        };
        return new StyleBoxTexture
        {
            Texture = texture,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 7,
            ContentMarginBottom = 8,
        };
    }

    private static TextureRect QuestToastRule()
    {
        var ramp = new Gradient
        {
            Offsets = [0f, 0.5f, 1f],
            Colors = [new Color(UiTheme.Gold, 0f), new Color(UiTheme.Gold, 0.9f), new Color(UiTheme.Gold, 0f)],
        };
        return new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = ramp,
                Width = 256,
                Height = 1,
                Fill = GradientTexture2D.FillEnum.Linear,
                FillFrom = new Vector2(0, 0.5f),
                FillTo = new Vector2(1, 0.5f),
            },
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            CustomMinimumSize = new Vector2(0, 1),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }

    private void CentreQuestToast()
    {
        if (_questToastPanel?.GetParent() is not Control anchor) return;
        _questToastPanel.Position = new Vector2(
            Mathf.Round((anchor.Size.X - _questToastPanel.Size.X) * 0.5f), QuestToastTop);
    }

    private void ShowQuestProgressToast(string questName, string objective, int current, int target)
    {
        if (target <= 0) return;
        EnsureQuestToast();
        var key = $"{questName}|{objective}|{current}/{target}";
        if (key == _questToastKey && _questToastLeft > 0) return;
        _questToastKey = key;
        _questToastTitle!.Text = $"<< {questName} >>";
        _questToastLine!.Text = $"{objective} : {current}/{target}";
        _questToastLine.AddThemeColorOverride("font_color",
            current >= target ? UiTheme.Good : UiTheme.TextHi);
        _questToastPanel!.Visible = true;
        _questToastPanel.Modulate = Colors.White;
        _questToastLeft = QuestToastSeconds;
        _questToastPanel.ResetSize();
        CentreQuestToast();
    }

    private void TickQuestToast(double delta)
    {
        if (_questToastPanel is not { Visible: true }) return;
        _questToastLeft -= delta;
        if (_questToastLeft <= 0)
        {
            _questToastPanel.Visible = false;
            _questToastKey = "";
            return;
        }
        _questToastPanel.Modulate = new Color(1, 1, 1,
            (float)Mathf.Min(1.0, _questToastLeft / QuestToastFadeSeconds));
    }
}
