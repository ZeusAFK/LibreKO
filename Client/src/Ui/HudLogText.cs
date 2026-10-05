using Godot;

namespace LibreKO;

public sealed partial class HudLogText : RichTextLabel
{
    public const float ScrollbarGutter = 12f;
    private const float ScrollbarWidth = 6f;
    private const float ScrollbarTopInset = 5f;
    public const float DefaultScrollbarBottomInset = 22f;
    private const float TextEdgePadding = 5f;
    private const float TextTopPadding = 3f;
    private const float TextBottomPadding = 4f;
    public bool ScrollbarOnLeft { get; init; }
    public float ScrollbarBottomInset { get; init; } = DefaultScrollbarBottomInset;
    private float _reservedBottom;
    private bool? _wasScrollable;
    private bool _syncQueued;

    public override void _Ready()
    {
        LayoutDirection = Control.LayoutDirectionEnum.Ltr;
        TextDirection = Control.TextDirection.Ltr;
        StyleTextPadding();
        StyleScrollbar();
        VScrollBar bar = GetVScrollBar();
        bar.ItemRectChanged += QueueSync;
        bar.VisibilityChanged += QueueSync;
        bar.Changed += QueueSync;
        Resized += QueueSync;
        Sync();
    }

    private void QueueSync()
    {
        if (_syncQueued) return;
        _syncQueued = true;
        Callable.From(Sync).CallDeferred();
    }

    private void Sync()
    {
        _syncQueued = false;
        ConstrainScrollbar();
        VScrollBar bar = GetVScrollBar();
        bool scrollable = bar.MaxValue > bar.Page + 0.5;
        if (_wasScrollable == scrollable) return;
        _wasScrollable = scrollable;
        UiTheme.ThinScrollbarThumb(bar, scrollable);
    }

    private void StyleScrollbar()
    {
        VScrollBar bar = GetVScrollBar();
        bar.Show();
        UiTheme.ThinScrollbar(bar);
    }

    private void StyleTextPadding()
    {
        var content = new StyleBoxFlat { BgColor = Colors.Transparent };
        content.ContentMarginTop = TextTopPadding;
        content.ContentMarginBottom = TextBottomPadding + _reservedBottom;
        content.ContentMarginLeft = ScrollbarOnLeft ? ScrollbarGutter : TextEdgePadding;
        content.ContentMarginRight = ScrollbarOnLeft ? TextEdgePadding : ScrollbarGutter;
        AddThemeStyleboxOverride("normal", content);
    }

    public void ReserveBottom(float height)
    {
        if (Mathf.IsEqualApprox(_reservedBottom, height)) return;
        _reservedBottom = height;
        if (IsNodeReady()) StyleTextPadding();
    }

    private void ConstrainScrollbar()
    {
        VScrollBar bar = GetVScrollBar();
        bar.Show();
        bar.SetAnchorsPreset(LayoutPreset.TopLeft);
        bar.CustomMinimumSize = new Vector2(ScrollbarWidth, 0);
        bar.Position = new Vector2(
            ScrollbarOnLeft ? 2f : Mathf.Max(0f, Size.X - ScrollbarWidth - 2f),
            ScrollbarTopInset);
        bar.Size = new Vector2(
            ScrollbarWidth,
            Mathf.Max(0f, Size.Y - ScrollbarTopInset - ScrollbarBottomInset));
    }
}
