using Godot;

namespace LibreKO.Features.Windows;

internal sealed class QuestProgressToastFeature : IFeature, ITickFeature
{
    private readonly IWorldContext context;
    private readonly EventPlatesFeature eventPlates;
    private readonly QuestBook book;
    private readonly SelfSession self;

    public QuestProgressToastFeature(IWorldContext context, EventPlatesFeature eventPlates, QuestBook questBook, SelfSession self)
    {
        this.context = context;
        this.eventPlates = eventPlates;
        book = questBook;
        this.self = self;
    }

    private Dictionary<(int Quest, string Objective), int> questProgressSeen = new();

    public void Build()
    {
    }

    public void Dispose()
    {
    }

    public void Tick(double delta, double nowSec) => TickToast(delta);

    private const double ToastSeconds = 3.2;
    private const double ToastFadeSeconds = 0.55;

    private CanvasLayer? toastLayer;
    internal PanelContainer? ToastPanel { get; private set; }
    private Label? toastTitle;
    private Label? toastLine;
    private double toastLeft;
    private string toastKey = "";

    internal void ReportProgress()
    {
        string selfName = self.LastEnter.Name ?? "";
        (string Quest, string Label, int Done, int Needed)? advanced = null;
        var seen = new Dictionary<(int, string), int>();
        foreach (var quest in book.Entries)
        {
            if (quest.State != QuestStates.Active && quest.State != QuestStates.ReadyToTurnIn) continue;
            foreach (var (label, done, needed) in QuestInfo.ObjectiveSteps(book, self.Inventory, quest.QuestId))
            {
                var key = (quest.QuestId, label);
                seen[key] = done;
                if (advanced is null && questProgressSeen.TryGetValue(key, out var was) && done > was)
                    advanced = (QuestInfo.Name(book, quest.QuestId, selfName), label, done, needed);
            }
        }
        questProgressSeen = seen;
        if (advanced is { } step)
            Show(step.Quest, step.Label, step.Done, step.Needed);
    }

    private void EnsureToast()
    {
        if (ToastPanel != null) return;

        toastLayer = new CanvasLayer { Layer = UiLayers.TopWindows };
        context.Root.AddChild(toastLayer);

        var anchor = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        anchor.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        toastLayer.AddChild(anchor);

        ToastPanel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0),
            Visible = false,
        };
        ToastPanel.AddThemeStyleboxOverride("panel", ToastStyle());
        anchor.AddChild(ToastPanel);

        var rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 6);
        ToastPanel.AddChild(rows);
        rows.AddChild(ToastRule());

        var text = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("margin_left", 58);
        text.AddThemeConstantOverride("margin_right", 58);
        rows.AddChild(text);

        var lines = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        lines.AddThemeConstantOverride("separation", 3);
        text.AddChild(lines);

        toastTitle = UiTheme.Text("", 14, UiTheme.Gold, HorizontalAlignment.Center);
        toastTitle.AddThemeConstantOverride("font_embolden", 1);
        toastTitle.AddThemeConstantOverride("outline_size", 4);
        toastTitle.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        lines.AddChild(toastTitle);

        toastLine = UiTheme.Text("", 16, UiTheme.TextHi, HorizontalAlignment.Center);
        toastLine.AddThemeConstantOverride("font_embolden", 1);
        toastLine.AddThemeConstantOverride("outline_size", 4);
        toastLine.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        lines.AddChild(toastLine);
        rows.AddChild(ToastRule());

        eventPlates.AddPlate(ToastPanel);
    }

    internal static StyleBoxTexture ToastStyle()
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

    internal static TextureRect ToastRule()
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

    internal void Show(string questName, string objective, int current, int target)
    {
        if (target <= 0 || !Config.QuestNotices) return;
        EnsureToast();
        var key = $"{questName}|{objective}|{current}/{target}";
        if (key == toastKey && toastLeft > 0) return;
        toastKey = key;
        toastTitle!.Text = $"<< {questName} >>";
        toastLine!.Text = $"{objective} : {current}/{target}";
        toastLine.AddThemeColorOverride("font_color",
            current >= target ? UiTheme.Good : UiTheme.TextHi);
        ToastPanel!.Visible = true;
        ToastPanel.Modulate = Colors.White;
        toastLeft = ToastSeconds;
        ToastPanel.ResetSize();
    }

    internal void TickToast(double delta)
    {
        if (ToastPanel is not { Visible: true }) return;
        toastLeft -= delta;
        if (toastLeft <= 0)
        {
            ToastPanel.Visible = false;
            toastKey = "";
            return;
        }
        ToastPanel.Modulate = new Color(1, 1, 1,
            (float)Mathf.Min(1.0, toastLeft / ToastFadeSeconds));
    }
}
