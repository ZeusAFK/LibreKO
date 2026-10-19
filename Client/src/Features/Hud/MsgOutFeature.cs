using Godot;

namespace LibreKO.Features.Hud;

internal sealed class MsgOutFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly SkillCastSystem skillCast;

    public MsgOutFeature(IWorldContext context, SkillCastSystem skillCast)
    {
        this.context = context;
        this.skillCast = skillCast;
    }

    public void Build()
    {
        skillCast.SkillMessage += ShowSkill;
    }

    public void Dispose()
    {
        skillCast.SkillMessage -= ShowSkill;
    }

    private const int FontSize = 28;
    private const float HeightShare = 0.4f;
    private const float StartScale = 1.5f;
    private const double GrowSeconds = 0.25;
    private const double Seconds = 3.0;
    private static readonly Color Colour = new(1f, 1f, 0f);

    private Label? msgOut;
    internal double Until { get; private set; }

    internal void ShowSkill(string? text)
    {
        if (string.IsNullOrEmpty(text) || context.Now < Until) return;
        if (msgOut == null || !GodotObject.IsInstanceValid(msgOut))
        {
            var layer = new CanvasLayer { Layer = UiLayers.Notices };
            context.Root.AddChild(layer);
            msgOut = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            msgOut.AddThemeFontSizeOverride("font_size", FontSize);
            msgOut.AddThemeColorOverride("font_color", Colour);
            msgOut.AddThemeColorOverride("font_outline_color", Colors.Black);
            msgOut.AddThemeConstantOverride("outline_size", 6);
            layer.AddChild(msgOut);
        }

        var label = msgOut;
        label.Text = text;
        label.ResetSize();
        var view = context.Root.GetViewport().GetVisibleRect().Size;
        label.Position = new Vector2((view.X - label.Size.X) * 0.5f, view.Y * HeightShare - label.Size.Y * 0.5f);
        label.PivotOffset = label.Size * 0.5f;
        label.Scale = Vector2.One * StartScale;
        label.Visible = true;
        context.Root.CreateTween().TweenProperty(label, "scale", Vector2.One, GrowSeconds);

        Until = context.Now + Seconds;
        double until = Until;
        context.Root.After(Seconds, () =>
        {
            if (GodotObject.IsInstanceValid(label) && Until <= until) label.Visible = false;
        });
    }
}
