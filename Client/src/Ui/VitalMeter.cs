using Godot;

namespace LibreKO;

public sealed partial class VitalMeter : Control
{
    private const float GlideRate = 8f;
    private const float SettleEpsilon = 0.002f;

    private readonly Color track;
    private Color fill = Colors.White;
    private float shown;
    private float target;

    public VitalMeter(Color track)
    {
        this.track = track;
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(false);
    }

    public Color Fill
    {
        get => fill;
        set
        {
            if (fill == value) return;
            fill = value;
            QueueRedraw();
        }
    }

    public float Target
    {
        get => target;
        set
        {
            if (Mathf.IsEqualApprox(target, value)) return;
            target = value;
            SetProcess(true);
        }
    }

    public override void _Process(double delta)
    {
        shown = Mathf.Lerp(shown, target, 1f - Mathf.Exp(-GlideRate * (float)delta));
        if (Mathf.Abs(shown - target) < SettleEpsilon)
        {
            shown = target;
            SetProcess(false);
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), track);
        if (shown > 0f) DrawRect(new Rect2(0f, 0f, Size.X * shown, Size.Y), fill);
    }
}
