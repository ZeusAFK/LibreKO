using Godot;

namespace LibreKO.Features.Hud;

internal sealed class TopIconBadge
{
    private const float Size = 13f;
    private const float TouchSize = 30f;
    private const int CountFont = 9;
    private const int TouchCountFont = 17;
    private const int MaxCount = 9;
    private const float BlinkDim = 0.3f;
    private const float BlinkStep = 0.6f;

    private readonly Button button;
    private readonly TextureRect image;
    private readonly bool blinks;
    private readonly PanelContainer badge;
    private readonly Label count;

    internal TopIconBadge(Button button, TextureRect image, bool blinks)
    {
        this.button = button;
        this.image = image;
        this.blinks = blinks;

        float size = Platform.TouchUi ? TouchSize : Size;
        badge = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        badge.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        badge.AddChild(new BadgeDisc { MouseFilter = Control.MouseFilterEnum.Ignore });
        badge.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        badge.OffsetLeft = -size;
        badge.OffsetTop = -size;
        badge.OffsetRight = 2;
        badge.OffsetBottom = 2;
        button.AddChild(badge);

        count = UiTheme.Text("", Platform.TouchUi ? TouchCountFont : CountFont, UiTheme.Self, HorizontalAlignment.Center);
        count.VerticalAlignment = VerticalAlignment.Center;
        count.MouseFilter = Control.MouseFilterEnum.Ignore;
        badge.AddChild(count);
        badge.Visible = false;
    }

    internal Tween? Blink { get; private set; }

    internal void Show(int waiting)
    {
        bool any = waiting > 0;
        badge.Visible = any;
        count.Text = waiting > MaxCount ? $"{MaxCount}+" : waiting.ToString();
        image.SelfModulate = UiKit.TopIconColor(any);
        if (!blinks) return;

        StopBlink();
        button.Modulate = Colors.White;
        if (!any) return;

        var tween = button.CreateTween().SetLoops();
        tween.TweenProperty(button, "modulate:a", BlinkDim, BlinkStep);
        tween.TweenProperty(button, "modulate:a", 1f, BlinkStep);
        Blink = tween;
    }

    internal void StopBlink()
    {
        if (Blink != null && Blink.IsValid()) Blink.Kill();
        Blink = null;
    }
}
