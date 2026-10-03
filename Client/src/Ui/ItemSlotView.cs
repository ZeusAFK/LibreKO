using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public enum SlotLook { Normal, Selected, Dimmed, Unfit, Staged, Ghost }

public sealed partial class ItemSlotView : PanelContainer
{
    private const float WearHeight = 3f;
    private const float WearInset = 3f;
    private const float DragPreviewSize = 40f;
    private static readonly Color DimmedTint = new(1f, 1f, 1f, 0.32f);
    private static readonly Color UnfitTint = new(1f, 0.6f, 0.6f, 0.45f);
    private static readonly Color StagedTint = new(1f, 1f, 1f, 0.45f);
    private static readonly Color GhostTint = new(1f, 1f, 1f, 0.45f);
    private static readonly Color WearTrackColour = new(0.13f, 0.13f, 0.13f, 0.9f);
    private static readonly Dictionary<(Color?, bool), StyleBoxFlat> Frames =
        Shutdown.Track(new Dictionary<(Color?, bool), StyleBoxFlat>());

    private readonly TextureRect _icon;
    private readonly Label _count;
    private readonly UpgradeBadge _plus;
    private readonly ColorRect _wearTrack;
    private readonly ColorRect _wearFill;
    private SlotLook _look;
    private bool _hover;

    public event Action<ItemSlotView>? Clicked;
    public event Action<ItemSlotView>? DoubleClicked;
    public event Action<ItemSlotView>? RightClicked;
    public event Action<ItemSlotView>? Hovered;
    public event Action<ItemSlotView>? Unhovered;
    public event Action<ItemSlotView, int>? Wheeled;
    public Func<ItemSlotView, Variant>? DragOut;
    public Func<ItemSlotView, Variant, bool>? CanDrop;
    public Action<ItemSlotView, Variant>? Dropped;

    public int Index { get; set; }
    public ItemSlot Item { get; private set; }

    public ItemSlotView(float size)
    {
        CustomMinimumSize = new Vector2(size, size);
        MouseFilter = MouseFilterEnum.Stop;

        _icon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_icon);

        var overlay = new Control { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(overlay);
        _wearTrack = new ColorRect
        {
            Color = WearTrackColour,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 1f, AnchorBottom = 1f,
            OffsetLeft = WearInset, OffsetRight = -WearInset,
            OffsetTop = -WearInset - WearHeight, OffsetBottom = -WearInset,
        };
        overlay.AddChild(_wearTrack);
        _wearFill = new ColorRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = 0f, AnchorTop = 0f, AnchorBottom = 1f, AnchorRight = 0f,
        };
        _wearTrack.AddChild(_wearFill);

        _plus = UpgradeBadge.Attach(this);

        _count = HudStyle.Label(11, HorizontalAlignment.Right);
        _count.MouseFilter = MouseFilterEnum.Ignore;
        _count.VerticalAlignment = VerticalAlignment.Bottom;
        _count.AddThemeStyleboxOverride("normal", new StyleBoxEmpty { ContentMarginRight = 3, ContentMarginBottom = 1 });
        _count.AddThemeConstantOverride("outline_size", 3);
        _count.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        AddChild(_count);

        MouseEntered += () =>
        {
            _hover = true;
            ApplyFrame();
            if (!Item.IsEmpty) Hovered?.Invoke(this);
        };
        MouseExited += () =>
        {
            _hover = false;
            ApplyFrame();
            Unhovered?.Invoke(this);
        };
        ApplyFrame();
    }

    public void Set(ItemSlot item)
    {
        Item = item;
        if (item.IsEmpty)
        {
            _icon.Texture = null;
            _count.Text = "";
            _plus.Clear();
        }
        else
        {
            _icon.Texture = ItemData.Icon(item.ItemId);
            int shown = ItemData.ShownCount(ItemData.Get(item.ItemId), item);
            _count.Text = shown > 1 ? shown.ToString() : "";
            _plus.Set(item.ItemId);
        }
        ApplyFrame();
        if (!_hover) return;
        if (item.IsEmpty) Unhovered?.Invoke(this);
        else Hovered?.Invoke(this);
    }

    public void Clear() => Set(default);

    public SlotLook Look
    {
        get => _look;
        set
        {
            if (_look == value) return;
            _look = value;
            Modulate = value switch
            {
                SlotLook.Dimmed => DimmedTint,
                SlotLook.Unfit => UnfitTint,
                SlotLook.Staged => StagedTint,
                _ => Colors.White,
            };
            _icon.SelfModulate = value == SlotLook.Ghost ? GhostTint : Colors.White;
            ApplyFrame();
        }
    }

    public void SetWear(float? share, Color tint)
    {
        _wearTrack.Visible = share.HasValue;
        if (share is not { } value) return;
        _wearFill.Color = tint;
        _wearFill.AnchorRight = Mathf.Clamp(value, 0f, 1f);
    }

    private void ApplyFrame()
    {
        Color? grade = _look == SlotLook.Selected ? UiTheme.GoldBright
            : Item.IsEmpty ? null : ItemGrade.Tint(Item.ItemId);
        var key = (grade, _hover);
        if (!Frames.TryGetValue(key, out var frame))
            Frames[key] = frame = UiTheme.Slot(grade, _hover);
        AddThemeStyleboxOverride("panel", frame);
    }

    public override void _GuiInput(InputEvent ev)
    {
        if (ev is not InputEventMouseButton mb) return;
        if (mb.Pressed && Wheeled != null && mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            Wheeled(this, mb.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
            AcceptEvent();
        }
        else if (mb.Pressed && mb.ButtonIndex == MouseButton.Right)
        {
            RightClicked?.Invoke(this);
            AcceptEvent();
        }
        else if (mb.Pressed && mb.DoubleClick && mb.ButtonIndex == MouseButton.Left)
        {
            DoubleClicked?.Invoke(this);
            AcceptEvent();
        }
        else if (!mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            Clicked?.Invoke(this);
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (DragOut == null || Item.IsEmpty) return default;
        var data = DragOut(this);
        if (data.VariantType == Variant.Type.Nil) return default;
        DragLayer.Show(this, new TextureRect
        {
            Texture = ItemData.Icon(Item.ItemId),
            CustomMinimumSize = new Vector2(DragPreviewSize, DragPreviewSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        });
        return data;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) => CanDrop?.Invoke(this, data) ?? false;

    public override void _DropData(Vector2 atPosition, Variant data) => Dropped?.Invoke(this, data);
}
