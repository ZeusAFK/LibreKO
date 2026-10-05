using System;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public sealed partial class HudLayout : Node
{
    public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

    public static bool EditMode { get; set; }

    public event Action? Placed;
    public static bool PersistLayouts { get; set; } = true;

    private readonly Control _target;
    private readonly Control? _dragHandle;
    private readonly string _id;
    private readonly Func<Vector2>? _defaultPosition;
    private readonly HudAnchor.Spot? _anchor;
    private Vector2 _anchorMargin;
    private readonly Vector2 _anchorSize;
    private readonly Vector2 _defaultSize;
    private Vector2 _minimumSize;
    private readonly bool _resizable;
    private readonly bool _persist;
    private readonly Corner _resizeCorner;
    private readonly Corner _moveCorner;
    private readonly bool _moveGripAlwaysVisible;
    private readonly bool _moveGripOverlay;
    private readonly Vector2 _resizeGripOffset;
    private readonly Action<float>? _backgroundOpacityChanged;
    private int _backgroundOpacityIndex = -1;
    private static readonly float[] BackgroundOpacityLevels = { 0.90f, 0.50f, 0.20f };

    private bool _dragging;
    private bool _resizing;
    private Vector2 _dragOffset;
    private Vector2 _dragOriginMouse;
    private Vector2 _dragOriginPosition;
    private bool _dragMoved;
    private Vector2 _resizeOriginMouse;
    private Vector2 _resizeOriginSize;
    private Vector2 _resizeOriginPosition;
    private ResizeCorner? _corner;
    private bool? _cornerShown;
    private bool _gripShown;
    private bool _locked;
    private Rect2 _overlayRect;
    private Vector2 _gripSize, _cornerSize;
    private MoveGrip? _moveGrip;

    private HudLayout(
        Control target,
        string id,
        Control? dragHandle,
        Func<Vector2>? defaultPosition,
        bool resizable,
        Vector2 defaultSize,
        Vector2 minimumSize,
        bool persist,
        Corner resizeCorner,
        Corner moveCorner,
        bool moveGripAlwaysVisible,
        Vector2 resizeGripOffset,
        Action<float>? backgroundOpacityChanged,
        HudAnchor.Spot? anchor,
        Vector2 anchorMargin,
        Vector2 anchorSize,
        bool moveGripOverlay)
    {
        _target = target;
        _id = id;
        _dragHandle = dragHandle;
        _defaultPosition = defaultPosition;
        _anchor = anchor;
        _anchorMargin = anchorMargin;
        _anchorSize = anchorSize;
        _resizable = resizable;
        _defaultSize = defaultSize;
        _minimumSize = minimumSize;
        _persist = persist && PersistLayouts;
        _resizeCorner = resizeCorner;
        _moveCorner = moveCorner;
        _moveGripAlwaysVisible = moveGripAlwaysVisible;
        _moveGripOverlay = moveGripOverlay;
        _resizeGripOffset = resizeGripOffset;
        _backgroundOpacityChanged = backgroundOpacityChanged;
    }

    public bool Locked
    {
        get => _locked;
        set
        {
            if (_locked == value) return;
            _locked = value;
            _cornerShown = null;
        }
    }

    private bool Frozen => _locked && !EditMode;

    public static HudLayout Attach(
        Control target,
        string id,
        Control? dragHandle,
        Func<Vector2>? defaultPosition,
        bool resizable = false,
        Vector2 defaultSize = default,
        Vector2 minimumSize = default,
        bool persist = true,
        Corner resizeCorner = Corner.BottomRight,
        Corner moveCorner = Corner.TopLeft,
        bool moveGripAlwaysVisible = false,
        Vector2 resizeGripOffset = default,
        Action<float>? backgroundOpacityChanged = null,
        HudAnchor.Spot? anchor = null,
        Vector2 anchorMargin = default,
        Vector2 anchorSize = default,
        bool moveGripOverlay = true)
    {
        var behavior = new HudLayout(
            target, id, dragHandle, defaultPosition, resizable, defaultSize, minimumSize, persist,
            resizeCorner, moveCorner, moveGripAlwaysVisible, resizeGripOffset, backgroundOpacityChanged,
            anchor, anchorMargin, anchorSize, moveGripOverlay);
        target.AddChild(behavior);
        return behavior;
    }

    public override void _Ready()
    {
        if (_dragHandle != null && _anchor == null)
        {
            _dragHandle.MouseFilter = Control.MouseFilterEnum.Stop;
            _dragHandle.MouseDefaultCursorShape = Control.CursorShape.Move;
            _dragHandle.GuiInput += OnDragHandleInput;
        }

        GetViewport().SizeChanged += KeepOnScreen;
        _target.VisibilityChanged += KeepOnScreen;
        _target.Resized += ClampAfterResize;

        Callable.From(InstallOverlays).CallDeferred();
        Callable.From(ApplySavedLayout).CallDeferred();
    }

    private void KeepOnScreen()
    {
        if (!GodotObject.IsInstanceValid(_target) || !_target.IsVisibleInTree()) return;
        Callable.From(FollowDefaultOnScreen).CallDeferred();
    }

    private void ClampAfterResize() => Callable.From(ClampOnScreen).CallDeferred();

    internal void ReapplyDefault() => FollowDefaultOnScreen();

    internal Vector2 MinimumSize
    {
        get => _minimumSize;
        set => _minimumSize = value;
    }

    internal void Place(Vector2 position, Vector2 size)
    {
        _target.Size = size;
        _target.Position = position;
        ClampOnScreen();
        if (_persist)
        {
            Config.SaveWindowPos(_id, _target.Position);
            Config.SaveWindowSize(_id, _target.Size);
        }
        Placed?.Invoke();
    }

    private void FollowDefaultOnScreen()
    {
        if (!GodotObject.IsInstanceValid(_target)) return;
        if (_anchor == null && _persist && _defaultPosition != null && !_dragging && !Config.HasWindowPos(_id))
            _target.Position = _defaultPosition();
        ClampOnScreen();
    }

    private void InstallOverlays()
    {
        if (!GodotObject.IsInstanceValid(_target) || _target.GetParent() == null) return;

        if (_anchor == null && _moveGripOverlay)
        {
            _moveGrip = new MoveGrip(this);
            _target.AddSibling(_moveGrip);
        }

        if (_resizable)
        {
            _corner = new ResizeCorner(this, _resizeCorner);
            _target.AddSibling(_corner);
        }
    }

    public void SetAnchorMargin(Vector2 margin)
    {
        if (_anchorMargin.IsEqualApprox(margin)) return;
        _anchorMargin = margin;
        ApplySavedLayout();
    }

    private void ApplySavedLayout()
    {
        if (!GodotObject.IsInstanceValid(_target)) return;

        if (_anchor is { } spot)
        {
            if (_resizable) _target.Size = ClampSize(SavedSize());
            HudAnchor.Pin(_target, spot, _anchorMargin, _resizable ? _target.Size : _anchorSize);
            return;
        }

        Vector2 fallback = _defaultPosition?.Invoke() ?? _target.Position;
        _target.SetAnchorsPreset(Control.LayoutPreset.TopLeft, true);
        _target.Position = _persist ? Config.GetWindowPos(_id, fallback) : fallback;

        if (_resizable) _target.Size = ClampSize(SavedSize());
        ClampOnScreen();
    }

    private Vector2 SavedSize()
    {
        Vector2 fallback = _defaultSize != Vector2.Zero ? _defaultSize : _target.Size;
        return _persist ? Config.GetWindowSize(_id, fallback) : fallback;
    }

    private void OnDragHandleInput(InputEvent ev)
    {
        if (ev is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb) return;
        if (mb.Pressed) BeginDrag(mb.GlobalPosition);
        else EndDrag();
        _dragHandle?.AcceptEvent();
    }

    public override void _Input(InputEvent ev)
    {
        if (_dragging && ev is InputEventMouseMotion drag)
        {
            if (drag.GlobalPosition.DistanceTo(_dragOriginMouse) >= 4f)
                _dragMoved = true;
            _target.Position = drag.GlobalPosition + _dragOffset;
            ClampOnScreen();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_resizing && ev is InputEventMouseMotion resize)
        {
            Vector2 delta = resize.GlobalPosition - _resizeOriginMouse;
            bool fromLeft = _resizeCorner is Corner.TopLeft or Corner.BottomLeft;
            bool fromTop = _resizeCorner is Corner.TopLeft or Corner.TopRight;
            Vector2 requested = _resizeOriginSize + new Vector2(
                fromLeft ? -delta.X : delta.X,
                fromTop ? -delta.Y : delta.Y);
            Vector2 size = ClampSize(requested, fromLeft, fromTop);
            Vector2 position = _resizeOriginPosition;
            if (fromLeft) position.X += _resizeOriginSize.X - size.X;
            if (fromTop) position.Y += _resizeOriginSize.Y - size.Y;
            _target.Position = position;
            _target.Size = size;
            ClampOnScreen();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (ev is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left })
        {
            if (_dragging)
            {
                EndDrag();
                GetViewport().SetInputAsHandled();
            }
            if (_resizing)
            {
                EndResize();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public override void _Process(double delta)
    {
        using var scope = Perf.Measure(Perf.Section.Ui);
        if (_moveGrip == null && _corner == null) return;
        bool shown = _target.IsVisibleInTree();
        bool frozen = Frozen;
        bool gripShown = shown && !frozen && (_moveGripAlwaysVisible || EditMode);
        bool cornerShown = shown && !frozen;
        var rect = new Rect2(_target.GlobalPosition, _target.Size);
        Vector2 gripSize = _moveGrip?.Size ?? Vector2.Zero, cornerSize = _corner?.Size ?? Vector2.Zero;
        if (cornerShown == _cornerShown && gripShown == _gripShown && rect == _overlayRect
            && gripSize == _gripSize && cornerSize == _cornerSize)
            return;
        _cornerShown = cornerShown;
        _gripShown = gripShown;
        _overlayRect = rect;
        _gripSize = gripSize;
        _cornerSize = cornerSize;
        if (_moveGrip != null)
        {
            _moveGrip.Visible = gripShown;
            if (gripShown) _moveGrip.GlobalPosition = OverlayPosition(gripSize, _moveCorner);
        }
        if (_corner != null)
        {
            _corner.Visible = cornerShown;
            if (cornerShown) _corner.GlobalPosition = OverlayPosition(cornerSize, _resizeCorner) + _resizeGripOffset;
        }
    }

    private Vector2 OverlayPosition(Vector2 overlaySize, Corner corner)
    {
        Vector2 inset = new(3f, 3f);
        return _target.GlobalPosition + corner switch
        {
            Corner.TopLeft => inset,
            Corner.TopRight => new Vector2(_target.Size.X - overlaySize.X - inset.X, inset.Y),
            Corner.BottomLeft => new Vector2(inset.X, _target.Size.Y - overlaySize.Y - inset.Y),
            _ => _target.Size - overlaySize - inset,
        };
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= KeepOnScreen;
        if (GodotObject.IsInstanceValid(_target))
        {
            _target.VisibilityChanged -= KeepOnScreen;
            _target.Resized -= ClampAfterResize;
        }
        if (_moveGrip != null && GodotObject.IsInstanceValid(_moveGrip))
            _moveGrip.QueueFree();
        if (_corner != null && GodotObject.IsInstanceValid(_corner))
            _corner.QueueFree();
    }

    private void BeginDrag(Vector2 mouse)
    {
        if (Frozen) return;
        _dragging = true;
        _dragMoved = false;
        _dragOriginMouse = mouse;
        _dragOriginPosition = _target.Position;
        _dragOffset = _target.Position - mouse;
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        if (!_dragMoved)
        {
            _target.Position = _dragOriginPosition;
            CycleBackgroundOpacity();
            return;
        }
        ClampOnScreen();
        if (_persist) Config.SaveWindowPos(_id, _target.Position);
        Placed?.Invoke();
    }

    internal void CycleBackgroundOpacity()
    {
        if (_backgroundOpacityChanged == null) return;
        _backgroundOpacityIndex = (_backgroundOpacityIndex + 1) % BackgroundOpacityLevels.Length;
        _backgroundOpacityChanged(BackgroundOpacityLevels[_backgroundOpacityIndex]);
    }

    private void BeginResize(Vector2 mouse)
    {
        if (Frozen) return;
        _resizing = true;
        _resizeOriginMouse = mouse;
        _resizeOriginSize = _target.Size;
        _resizeOriginPosition = _target.Position;
    }

    private void EndResize()
    {
        if (!_resizing) return;
        _resizing = false;
        if (!_persist) return;
        if (_anchor == null) Config.SaveWindowPos(_id, _target.Position);
        Config.SaveWindowSize(_id, _target.Size);
    }

    private Vector2 ClampSize(Vector2 requested, bool fromLeft = false, bool fromTop = false)
    {
        var vp = GetViewport().GetVisibleRect().Size;
        float minX = Mathf.Max(120f, _minimumSize.X);
        float minY = Mathf.Max(80f, _minimumSize.Y);
        var limit = HudResize.Limit(vp, _target.Position, new Rect2(_resizeOriginPosition, _resizeOriginSize), fromLeft, fromTop);
        float maxX = Mathf.Max(minX, limit.X);
        float maxY = Mathf.Max(minY, limit.Y);
        return new Vector2(
            Mathf.Clamp(requested.X, minX, maxX),
            Mathf.Clamp(requested.Y, minY, maxY));
    }

    private void ClampOnScreen()
    {
        if (_anchor != null || !GodotObject.IsInstanceValid(_target) || !_target.IsInsideTree()) return;
        var vp = GetViewport().GetVisibleRect().Size;
        var min = _target.GetCombinedMinimumSize();
        var size = new Vector2(
            Mathf.Max(_target.Size.X, min.X),
            Mathf.Max(_target.Size.Y, min.Y)) * _target.Scale;
        var p = _target.Position;
        p.X = Mathf.Clamp(p.X, 0f, Mathf.Max(0f, vp.X - size.X));
        p.Y = Mathf.Clamp(p.Y, 0f, Mathf.Max(0f, vp.Y - size.Y));
        _target.Position = p;
    }

    private sealed partial class ResizeCorner : Control
    {
        private const float GripSize = 24f;
        private const float StrokeWidth = 2.5f;
        private static readonly Color Stroke = new(UiTheme.Gold, 0.95f);
        private static readonly Color Shade = new(0f, 0f, 0f, 0.7f);
        private static readonly Vector2[][] Strokes =
        {
            new[] { new Vector2(9, 22), new Vector2(22, 9) },
            new[] { new Vector2(14, 22), new Vector2(22, 14) },
            new[] { new Vector2(19, 22), new Vector2(22, 19) },
        };

        private readonly HudLayout _owner;
        private readonly Corner _placement;
        private bool _hot;

        public ResizeCorner(HudLayout owner, Corner placement)
        {
            _owner = owner;
            _placement = placement;
            CustomMinimumSize = new Vector2(GripSize, GripSize);
            Size = CustomMinimumSize;
            MouseFilter = MouseFilterEnum.Stop;
            MouseDefaultCursorShape = placement is Corner.TopRight or Corner.BottomLeft
                ? CursorShape.Bdiagsize
                : CursorShape.Fdiagsize;
            TooltipText = "Drag to resize";
            ZIndex = 100;
            MouseEntered += () => { _hot = true; QueueRedraw(); };
            MouseExited += () => { _hot = false; QueueRedraw(); };
        }

        public override void _Draw()
        {
            Vector2 Mirror(Vector2 p) => new(
                _placement is Corner.TopRight or Corner.BottomRight ? p.X : Size.X - p.X,
                _placement is Corner.BottomLeft or Corner.BottomRight ? p.Y : Size.Y - p.Y);
            var shadeOffset = new Vector2(1, 1);
            foreach (var stroke in Strokes)
                DrawLine(Mirror(stroke[0]) + shadeOffset, Mirror(stroke[1]) + shadeOffset, Shade, StrokeWidth);
            foreach (var stroke in Strokes)
                DrawLine(Mirror(stroke[0]), Mirror(stroke[1]), _hot ? UiTheme.GoldBright : Stroke, StrokeWidth);
        }

        public override void _GuiInput(InputEvent ev)
        {
            if (ev is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb) return;
            if (mb.Pressed) _owner.BeginResize(mb.GlobalPosition);
            else _owner.EndResize();
            AcceptEvent();
        }
    }

    private sealed partial class MoveGrip : Control
    {
        private readonly HudLayout _owner;

        public MoveGrip(HudLayout owner)
        {
            _owner = owner;
            CustomMinimumSize = new Vector2(18, 18);
            Size = CustomMinimumSize;
            Visible = false;
            MouseFilter = MouseFilterEnum.Stop;
            MouseDefaultCursorShape = CursorShape.Move;
            ZIndex = 200;
        }

        public override void _Draw()
        {
            DrawCircle(Size * 0.5f, 8f, new Color(0.035f, 0.035f, 0.045f, 0.94f));
            DrawArc(Size * 0.5f, 7.5f, 0, Mathf.Tau, 24, new Color(0.72f, 0.75f, 0.78f, 0.95f), 1.2f, true);
            var c = Size * 0.5f;
            var color = new Color(0.90f, 0.92f, 0.94f);
            DrawLine(c + new Vector2(-4, 0), c + new Vector2(4, 0), color, 1.3f);
            DrawLine(c + new Vector2(0, -4), c + new Vector2(0, 4), color, 1.3f);
            DrawLine(c + new Vector2(-4, 0), c + new Vector2(-2, -2), color, 1.3f);
            DrawLine(c + new Vector2(-4, 0), c + new Vector2(-2, 2), color, 1.3f);
            DrawLine(c + new Vector2(4, 0), c + new Vector2(2, -2), color, 1.3f);
            DrawLine(c + new Vector2(4, 0), c + new Vector2(2, 2), color, 1.3f);
        }

        public override void _GuiInput(InputEvent ev)
        {
            if (ev is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb) return;
            if (mb.Pressed) _owner.BeginDrag(mb.GlobalPosition);
            else _owner.EndDrag();
            AcceptEvent();
        }
    }
}
