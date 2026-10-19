using Godot;

namespace LibreKO;

public partial class RebirthEmblem : Control
{
    private const float RingWidth = 3f;
    private const int RingPoints = 48;
    private const float PulseSeconds = 0.8f;
    private const float PulseLow = 0.55f;
    private static readonly Color Disc = new(0.10f, 0.105f, 0.11f, 0.97f);
    private static readonly Color Edge = new(0.24f, 0.22f, 0.17f, 0.92f);
    private static readonly Color Track = new(0.25f, 0.08f, 0.08f, 0.85f);
    private static readonly Color Halo = new(0.86f, 0.18f, 0.16f, 1f);
    private static readonly Color Number = new(1.00f, 0.82f, 0.39f);

    private readonly Label count;
    private float fraction;
    private bool ready;
    private float pulse;

    public RebirthEmblem(float size)
    {
        CustomMinimumSize = new Vector2(size, size);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        count = HudStyle.Label(12, HorizontalAlignment.Center);
        count.AddThemeColorOverride("font_color", Number);
        count.AddThemeConstantOverride("outline_size", 2);
        count.SetAnchorsPreset(LayoutPreset.FullRect);
        count.VerticalAlignment = VerticalAlignment.Center;
        count.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(count);
        SetProcess(false);
    }

    public void Set(int rebirths, float fraction, bool ready, string tooltip)
    {
        count.Text = rebirths.ToString();
        this.fraction = Mathf.Clamp(fraction, 0f, 1f);
        TooltipText = tooltip;
        if (this.ready != ready)
        {
            this.ready = ready;
            SetProcess(ready);
            if (!ready) Modulate = Colors.White;
        }
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        pulse = (pulse + (float)delta / PulseSeconds) % 1f;
        float wave = 0.5f + 0.5f * Mathf.Cos(pulse * Mathf.Tau);
        Modulate = new Color(1f, 1f, 1f, Mathf.Lerp(PulseLow, 1f, wave));
    }

    public override void _Draw()
    {
        float radius = Mathf.Min(Size.X, Size.Y) * 0.5f - RingWidth;
        var centre = Size * 0.5f;
        DrawCircle(centre, radius, Disc);
        DrawArc(centre, radius, 0f, Mathf.Tau, RingPoints, Track, RingWidth, true);
        if (fraction > 0f)
            DrawArc(centre, radius, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * fraction, RingPoints, Halo, RingWidth, true);
        DrawArc(centre, radius + RingWidth * 0.5f + 0.5f, 0f, Mathf.Tau, RingPoints, Edge, 1f, true);
    }
}
