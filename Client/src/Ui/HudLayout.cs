using System;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public sealed partial class HudLayout : Node
{
    public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

    public event Action? Placed;
    public event Action? Clicked;
    public static bool PersistLayouts { get; set; } = true;

    private static readonly System.Collections.Generic.HashSet<HudLayout> Live = new();

    private readonly Control target;
    private readonly Control? dragHandle;
    private readonly string id;
    private readonly Func<Vector2>? defaultPosition;
    private bool userPlaced;
    private readonly HudAnchor.Spot? anchor;
    private Vector2 anchorMargin;
    private readonly Vector2 anchorSize;
    private readonly Vector2 defaultSize;
    private Vector2 minimumSize;
    private readonly bool resizable;
    private readonly bool persist;
    private readonly Corner resizeCorner;
    private readonly Corner moveCorner;
    private readonly bool moveGripAlwaysVisible;
    private readonly bool moveGripOverlay;
    private readonly Vector2 resizeGripOffset;
    private readonly Action<float>? backgroundOpacityChanged;
    private int backgroundOpacityIndex = -1;
    private static readonly float[] BackgroundOpacityLevels = { 0.90f, 0.50f, 0.20f };

    private bool dragging;
    private bool resizing;
    private Vector2 dragOffset;
    private Vector2 dragOriginMouse;
    private Vector2 dragOriginPosition;
    private bool dragMoved;
    private Vector2 resizeOriginMouse;
    private Vector2 resizeOriginSize;
    private Vector2 resizeOriginPosition;
    private ResizeCorner? resizeGrip;
    private bool? cornerShown;
    private bool gripShown;
    private bool locked;
    private Rect2 overlayRect;
    private Vector2 gripSize, cornerSize;
    private MoveGrip? moveGrip;

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
        this.target = target;
        this.id = id;
        this.dragHandle = dragHandle;
        this.defaultPosition = defaultPosition;
        this.anchor = anchor;
        this.anchorMargin = anchorMargin;
        this.anchorSize = anchorSize;
        this.resizable = resizable;
        this.defaultSize = defaultSize;
        this.minimumSize = minimumSize;
        this.persist = persist && PersistLayouts;
        this.resizeCorner = resizeCorner;
        this.moveCorner = moveCorner;
        this.moveGripAlwaysVisible = moveGripAlwaysVisible;
        this.moveGripOverlay = moveGripOverlay;
        this.resizeGripOffset = resizeGripOffset;
        this.backgroundOpacityChanged = backgroundOpacityChanged;
    }

    public bool Locked
    {
        get => locked;
        set
        {
            if (locked == value) return;
            locked = value;
            cornerShown = null;
        }
    }

    private bool Frozen => locked;

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
        if (dragHandle != null && anchor == null)
        {
            dragHandle.MouseFilter = Control.MouseFilterEnum.Stop;
            dragHandle.MouseDefaultCursorShape = Control.CursorShape.Move;
            dragHandle.GuiInput += OnDragHandleInput;
        }

        GetViewport().SizeChanged += KeepOnScreen;
        Config.UiScaleApplied += KeepOnScreen;
        target.VisibilityChanged += KeepOnScreen;
        target.Resized += ClampAfterResize;
        Live.Add(this);

        Callable.From(InstallOverlays).CallDeferred();
        Callable.From(ApplySavedLayout).CallDeferred();
    }

    private void KeepOnScreen()
    {
        if (!GodotObject.IsInstanceValid(target) || !target.IsVisibleInTree()) return;
        Callable.From(FollowDefaultOnScreen).CallDeferred();
    }

    private void ClampAfterResize() => Callable.From(ClampAndKeep).CallDeferred();

    private void ClampAndKeep()
    {
        if (!GodotObject.IsInstanceValid(target)) return;
        var before = target.Position;
        ClampOnScreen();
        if (persist && anchor == null && !dragging && Config.HasWindowPos(id) && !before.IsEqualApprox(target.Position))
            Config.SaveWindowPos(id, target.Position);
    }

    public void ReapplyDefault() => FollowDefaultOnScreen();

    public static void ResetAll()
    {
        Config.ForgetAllWindowLayouts();
        foreach (var layout in new System.Collections.Generic.List<HudLayout>(Live))
        {
            if (!GodotObject.IsInstanceValid(layout)) continue;
            layout.userPlaced = false;
            layout.ApplySavedLayout();
            layout.Placed?.Invoke();
        }
    }

    public Vector2 MaximumViewportShare { get; set; }

    internal Vector2 MinimumSize
    {
        get => minimumSize;
        set => minimumSize = value;
    }

    internal void Place(Vector2 position, Vector2 size)
    {
        target.Size = size;
        target.Position = position;
        userPlaced = true;
        ClampOnScreen();
        if (persist)
        {
            Config.SaveWindowPos(id, target.Position);
            Config.SaveWindowSize(id, target.Size);
        }
        Placed?.Invoke();
    }

    private void FollowDefaultOnScreen()
    {
        if (!GodotObject.IsInstanceValid(target)) return;
        if (anchor == null && defaultPosition != null && !dragging && !userPlaced
            && !(persist && Config.HasWindowPos(id)))
            target.Position = defaultPosition();
        ClampOnScreen();
    }

    private void InstallOverlays()
    {
        if (!GodotObject.IsInstanceValid(target) || target.GetParent() == null) return;

        if (anchor == null && moveGripOverlay)
        {
            moveGrip = new MoveGrip(this);
            target.AddSibling(moveGrip);
        }

        if (resizable)
        {
            resizeGrip = new ResizeCorner(this, resizeCorner);
            target.AddSibling(resizeGrip);
        }
    }

    private void ApplySavedLayout()
    {
        if (!GodotObject.IsInstanceValid(target)) return;

        if (anchor is { } spot)
        {
            if (resizable) target.Size = ClampSize(SavedSize());
            HudAnchor.Pin(target, spot, anchorMargin, resizable ? target.Size : anchorSize);
            return;
        }

        Vector2 fallback = defaultPosition?.Invoke() ?? target.Position;
        target.SetAnchorsPreset(Control.LayoutPreset.TopLeft, true);
        target.Position = persist ? Config.GetWindowPos(id, fallback) : fallback;

        if (resizable) target.Size = ClampSize(SavedSize());
        ClampOnScreen();
    }

    private Vector2 SavedSize()
    {
        Vector2 fallback = defaultSize != Vector2.Zero ? defaultSize : target.Size;
        return persist ? Config.GetWindowSize(id, fallback) : fallback;
    }

    private void OnDragHandleInput(InputEvent ev) => DragFrom(dragHandle, ev);

    private void DragFrom(Control? handle, InputEvent ev)
    {
        if (ev is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb) return;
        if (mb.Pressed) BeginDrag(mb.GlobalPosition);
        else EndDrag();
        handle?.AcceptEvent();
    }

    public void AddDragHandle(Control handle)
    {
        if (anchor != null) return;
        handle.MouseFilter = Control.MouseFilterEnum.Stop;
        handle.MouseDefaultCursorShape = Control.CursorShape.Move;
        handle.GuiInput += ev => DragFrom(handle, ev);
    }

    public override void _Input(InputEvent ev)
    {
        if (dragging && ev is InputEventMouseMotion drag)
        {
            if (drag.GlobalPosition.DistanceTo(dragOriginMouse) >= 4f)
                dragMoved = true;
            target.Position = drag.GlobalPosition + dragOffset;
            ClampOnScreen();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (resizing && ev is InputEventMouseMotion resize)
        {
            Vector2 delta = resize.GlobalPosition - resizeOriginMouse;
            bool fromLeft = resizeCorner is Corner.TopLeft or Corner.BottomLeft;
            bool fromTop = resizeCorner is Corner.TopLeft or Corner.TopRight;
            Vector2 requested = resizeOriginSize + new Vector2(
                fromLeft ? -delta.X : delta.X,
                fromTop ? -delta.Y : delta.Y);
            Vector2 size = ClampSize(requested, fromLeft, fromTop);
            Vector2 position = resizeOriginPosition;
            if (fromLeft) position.X += resizeOriginSize.X - size.X;
            if (fromTop) position.Y += resizeOriginSize.Y - size.Y;
            target.Position = position;
            target.Size = size;
            ClampOnScreen();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (ev is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left })
        {
            if (dragging)
            {
                EndDrag();
                GetViewport().SetInputAsHandled();
            }
            if (resizing)
            {
                EndResize();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public override void _Process(double delta)
    {
        using var scope = Perf.Measure(Perf.Section.Ui);
        if (moveGrip == null && resizeGrip == null) return;
        bool shown = target.IsVisibleInTree();
        bool frozen = Frozen;
        bool gripShown = shown && !frozen && moveGripAlwaysVisible;
        bool showCorner = shown && !frozen;
        var rect = new Rect2(target.GlobalPosition, ScaledTargetSize());
        Vector2 gripSize = moveGrip?.Size ?? Vector2.Zero, cornerSize = resizeGrip?.Size ?? Vector2.Zero;
        if (showCorner == cornerShown && gripShown == this.gripShown && rect == overlayRect
            && gripSize == this.gripSize && cornerSize == this.cornerSize)
            return;
        cornerShown = showCorner;
        this.gripShown = gripShown;
        overlayRect = rect;
        this.gripSize = gripSize;
        this.cornerSize = cornerSize;
        if (moveGrip != null)
        {
            moveGrip.Visible = gripShown;
            if (gripShown) moveGrip.GlobalPosition = OverlayPosition(gripSize, moveCorner);
        }
        if (resizeGrip != null)
        {
            resizeGrip.Visible = showCorner;
            if (showCorner) resizeGrip.GlobalPosition = OverlayPosition(cornerSize, resizeCorner) + resizeGripOffset;
        }
    }

    private Vector2 ScaledTargetSize() => target.Size * target.Scale;

    private Vector2 OverlayPosition(Vector2 overlaySize, Corner corner)
    {
        Vector2 inset = new(3f, 3f);
        Vector2 size = ScaledTargetSize();
        return target.GlobalPosition + corner switch
        {
            Corner.TopLeft => inset,
            Corner.TopRight => new Vector2(size.X - overlaySize.X - inset.X, inset.Y),
            Corner.BottomLeft => new Vector2(inset.X, size.Y - overlaySize.Y - inset.Y),
            _ => size - overlaySize - inset,
        };
    }

    public override void _ExitTree()
    {
        Live.Remove(this);
        GetViewport().SizeChanged -= KeepOnScreen;
        Config.UiScaleApplied -= KeepOnScreen;
        if (GodotObject.IsInstanceValid(target))
        {
            target.VisibilityChanged -= KeepOnScreen;
            target.Resized -= ClampAfterResize;
        }
        if (moveGrip != null && GodotObject.IsInstanceValid(moveGrip))
            moveGrip.QueueFree();
        if (resizeGrip != null && GodotObject.IsInstanceValid(resizeGrip))
            resizeGrip.QueueFree();
    }

    private void BeginDrag(Vector2 mouse)
    {
        if (Frozen) return;
        dragging = true;
        dragMoved = false;
        dragOriginMouse = mouse;
        dragOriginPosition = target.Position;
        dragOffset = target.Position - mouse;
    }

    private void EndDrag()
    {
        if (!dragging) return;
        dragging = false;
        if (!dragMoved)
        {
            target.Position = dragOriginPosition;
            CycleBackgroundOpacity();
            Clicked?.Invoke();
            return;
        }
        userPlaced = true;
        ClampOnScreen();
        if (persist) Config.SaveWindowPos(id, target.Position);
        Placed?.Invoke();
    }

    internal void CycleBackgroundOpacity()
    {
        if (backgroundOpacityChanged == null) return;
        backgroundOpacityIndex = (backgroundOpacityIndex + 1) % BackgroundOpacityLevels.Length;
        backgroundOpacityChanged(BackgroundOpacityLevels[backgroundOpacityIndex]);
    }

    private void BeginResize(Vector2 mouse)
    {
        if (Frozen) return;
        resizing = true;
        resizeOriginMouse = mouse;
        resizeOriginSize = target.Size;
        resizeOriginPosition = target.Position;
    }

    private void EndResize()
    {
        if (!resizing) return;
        resizing = false;
        userPlaced = true;
        if (!persist) return;
        if (anchor == null) Config.SaveWindowPos(id, target.Position);
        Config.SaveWindowSize(id, target.Size);
    }

    private Vector2 ClampSize(Vector2 requested, bool fromLeft = false, bool fromTop = false)
    {
        var vp = GetViewport().GetVisibleRect().Size;
        float minX = Mathf.Max(120f, minimumSize.X);
        float minY = Mathf.Max(80f, minimumSize.Y);
        var limit = HudResize.Limit(vp, target.Position, new Rect2(resizeOriginPosition, resizeOriginSize), fromLeft, fromTop);
        float maxX = Mathf.Max(minX, limit.X);
        float maxY = Mathf.Max(minY, limit.Y);
        if (MaximumViewportShare.X > 0f) maxX = Mathf.Max(minX, Mathf.Min(maxX, vp.X * MaximumViewportShare.X));
        if (MaximumViewportShare.Y > 0f) maxY = Mathf.Max(minY, Mathf.Min(maxY, vp.Y * MaximumViewportShare.Y));
        return new Vector2(
            Mathf.Clamp(requested.X, minX, maxX),
            Mathf.Clamp(requested.Y, minY, maxY));
    }

    private void ClampOnScreen()
    {
        if (anchor != null || !GodotObject.IsInstanceValid(target) || !target.IsInsideTree()) return;
        var vp = GetViewport().GetVisibleRect().Size;
        var min = target.GetCombinedMinimumSize();
        var size = new Vector2(
            Mathf.Max(target.Size.X, min.X),
            Mathf.Max(target.Size.Y, min.Y)) * target.Scale;
        var p = target.Position;
        p.X = Mathf.Clamp(p.X, 0f, Mathf.Max(0f, vp.X - size.X));
        p.Y = Mathf.Clamp(p.Y, 0f, Mathf.Max(0f, vp.Y - size.Y));
        target.Position = p;
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

        private readonly HudLayout owner;
        private readonly Corner placement;
        private bool hot;

        public ResizeCorner(HudLayout owner, Corner placement)
        {
            this.owner = owner;
            this.placement = placement;
            CustomMinimumSize = new Vector2(GripSize, GripSize);
            Size = CustomMinimumSize;
            MouseFilter = MouseFilterEnum.Stop;
            MouseDefaultCursorShape = placement is Corner.TopRight or Corner.BottomLeft
                ? CursorShape.Bdiagsize
                : CursorShape.Fdiagsize;
            TooltipText = "Drag to resize";
            ZIndex = 100;
            MouseEntered += () => { hot = true; QueueRedraw(); };
            MouseExited += () => { hot = false; QueueRedraw(); };
        }

        public override void _Draw()
        {
            Vector2 Mirror(Vector2 p) => new(
                placement is Corner.TopRight or Corner.BottomRight ? p.X : Size.X - p.X,
                placement is Corner.BottomLeft or Corner.BottomRight ? p.Y : Size.Y - p.Y);
            var shadeOffset = new Vector2(1, 1);
            foreach (var stroke in Strokes)
                DrawLine(Mirror(stroke[0]) + shadeOffset, Mirror(stroke[1]) + shadeOffset, Shade, StrokeWidth);
            foreach (var stroke in Strokes)
                DrawLine(Mirror(stroke[0]), Mirror(stroke[1]), hot ? UiTheme.GoldBright : Stroke, StrokeWidth);
        }

        public override void _GuiInput(InputEvent ev)
        {
            if (ev is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb) return;
            if (mb.Pressed) owner.BeginResize(mb.GlobalPosition);
            else owner.EndResize();
            AcceptEvent();
        }
    }

    private sealed partial class MoveGrip : Control
    {
        private readonly HudLayout owner;

        public MoveGrip(HudLayout owner)
        {
            this.owner = owner;
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
            if (mb.Pressed) owner.BeginDrag(mb.GlobalPosition);
            else owner.EndDrag();
            AcceptEvent();
        }
    }
}
