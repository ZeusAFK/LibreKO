using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class QuestTargetFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly NpcQuestSession npc;
    private readonly MapFeature map;
    private readonly SelfSession self;

    public QuestTargetFeature(IWorldContext context, NpcQuestSession npcQuest, MapFeature map, SelfSession self)
    {
        this.context = context;
        npc = npcQuest;
        this.map = map;
        this.self = self;
    }

    public void Build()
    {
        npc.QuestTargetEvent += OnTarget;
    }

    public void Dispose()
    {
        npc.QuestTargetEvent -= OnTarget;
    }

    private const int TargetMapSide = 260;

    internal HudWindow? TargetWindow { get; private set; }
    private TargetMapView? targetMap;
    private VBoxContainer? targetText;
    private Label? targetCaption;

    internal void OnTarget(QuestTargetDetail target)
    {
        EnsureTargetWindow();
        TargetWindow!.Title = target.QuestTitle.Length > 0 ? target.QuestTitle : target.Target;

        foreach (var child in targetText!.GetChildren()) { targetText.RemoveChild(child); child.QueueFree(); }
        var name = target.About.Length > 0 ? target.About.Split('\n', 2)[0] : target.Target;
        var lore = target.About.Contains('\n') ? target.About.Split('\n', 2)[1] : "";
        var heading = UiTheme.Text(name, 15, UiTheme.GoldBright);
        heading.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        heading.CustomMinimumSize = new Vector2(TargetMapSide, 0);
        targetText.AddChild(heading);
        if (lore.Length > 0)
            targetText.AddChild(QuestRows.Paragraph(lore, UiTheme.TextLo, self.LastEnter.Name, TargetMapSide));

        targetCaption!.Text = target.Where.Length > 0 ? target.Where : ZoneCatalog.Name(target.ZoneId);
        targetMap!.SetTarget(
            target.ZoneId == context.Zone ? map.MiniMap?.MapTexture : null,
            map.MiniMap?.MapExtent ?? 0f,
            target.ZoneId == context.Zone && target.HasCoordinates ? new Vector2(target.X, target.Z) : null,
            target.ZoneId == context.Zone ? "" : $"In {ZoneCatalog.Name(target.ZoneId)}");
        TargetWindow.Visible = true;
    }

    private void EnsureTargetWindow()
    {
        if (TargetWindow != null) return;
        var layer = new CanvasLayer { Layer = UiLayers.ServiceWindows };
        context.Root.AddChild(layer);
        TargetWindow = new HudWindow("quest_target", "Target", new Vector2(320, 150), TargetMapSide) { Visible = false };
        layer.AddChild(TargetWindow);

        var body = TargetWindow.Body;
        body.AddThemeConstantOverride("separation", 8);

        var textPanel = UiTheme.Section();
        body.AddChild(textPanel);
        targetText = new VBoxContainer();
        targetText.AddThemeConstantOverride("separation", 6);
        textPanel.AddChild(targetText);

        targetCaption = UiTheme.Text("", 13, UiTheme.Gold, HorizontalAlignment.Center);
        targetCaption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        targetCaption.CustomMinimumSize = new Vector2(TargetMapSide, 0);
        body.AddChild(targetCaption);

        targetMap = new TargetMapView
        {
            CustomMinimumSize = new Vector2(TargetMapSide, TargetMapSide),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        body.AddChild(targetMap);

        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(0, 36) };
        close.Pressed += () => TargetWindow.Visible = false;
        body.AddChild(close);
    }

    private sealed partial class TargetMapView : Control
    {
        private Texture2D? tex;
        private float extent;
        private Vector2? target;
        private string note = "";

        public void SetTarget(Texture2D? tex, float extent, Vector2? target, string note)
        {
            this.tex = tex; this.extent = extent; this.target = target; this.note = note;
            QueueRedraw();
        }

        public override void _Draw()
        {
            var side = Mathf.Min(Size.X, Size.Y);
            if (side <= 0f) return;
            var rect = new Rect2((Size - new Vector2(side, side)) * 0.5f, new Vector2(side, side));

            if (tex != null)
                DrawTextureRect(tex, rect, false);
            else
                DrawRect(rect, new Color(0.06f, 0.07f, 0.09f));
            DrawRect(rect, new Color(UiTheme.Gold, 0.85f), false, 2f);

            if (note.Length > 0)
            {
                var font = ThemeDB.FallbackFont;
                var width = font.GetStringSize(note, fontSize: 13).X;
                DrawString(font, rect.Position + new Vector2((side - width) * 0.5f, side * 0.5f),
                    note, HorizontalAlignment.Left, -1, 13, UiTheme.TextLo);
                return;
            }

            if (target is not { } spot || extent <= 0f) return;
            float scale = side / extent;
            var point = rect.Position + new Vector2(spot.X * scale, side - spot.Y * scale);
            DrawCircle(point, 11, new Color(0, 0, 0, 0.8f));
            DrawArc(point, 9, 0, Mathf.Tau, 24, UiTheme.GoldBright, 3, true);
            DrawLine(point - new Vector2(15, 0), point + new Vector2(15, 0), UiTheme.GoldBright, 2, true);
            DrawLine(point - new Vector2(0, 15), point + new Vector2(0, 15), UiTheme.GoldBright, 2, true);
        }
    }
}
