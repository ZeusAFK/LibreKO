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
    private static readonly Color DimmedTint = new(1f, 1f, 1f, 0.32f);
    private static readonly Color UnfitTint = new(1f, 0.6f, 0.6f, 0.45f);
    private static readonly Color StagedTint = new(1f, 1f, 1f, 0.45f);
    private static readonly Color GhostTint = new(1f, 1f, 1f, 0.45f);
    private static readonly Color LockTint = new(1f, 0.86f, 0.55f, 0.9f);
    private static readonly Color LockedIconTint = new(0.45f, 0.45f, 0.45f);
    private const float LockInset = 3f;
    private const float LockSize = 14f;
    private static readonly Color WearTrackColour = new(0.13f, 0.13f, 0.13f, 0.9f);
    private static readonly Dictionary<(Color?, bool), StyleBoxFlat> Frames =
        Shutdown.Track(new Dictionary<(Color?, bool), StyleBoxFlat>());

    private readonly TextureRect icon;
    private readonly Label countLabel;
    private readonly UpgradeBadge plus;
    private readonly IconVeilView veil;
    private readonly ColorRect wearTrack;
    private readonly ColorRect wearFill;
    private SlotLook look;
    private Control? lockIcon;
    private bool hover;

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
    public bool Veiled { get; init; }
    public bool PicksAmount { get; init; }

    public bool Locked
    {
        get => lockIcon?.Visible ?? false;
        set
        {
            if (value && lockIcon == null)
            {
                lockIcon = new Control { MouseFilter = MouseFilterEnum.Ignore };
                AddChild(lockIcon);
                lockIcon.AddChild(new TextureRect
                {
                    Texture = UiIcons.Get("system/lock"),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = MouseFilterEnum.Ignore,
                    Modulate = LockTint,
                    AnchorLeft = 1f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 0f,
                    OffsetLeft = -LockInset - LockSize, OffsetRight = -LockInset,
                    OffsetTop = LockInset, OffsetBottom = LockInset + LockSize,
                });
            }
            if (lockIcon != null) lockIcon.Visible = value;
            icon.SelfModulate = value ? LockedIconTint : look == SlotLook.Ghost ? GhostTint : Colors.White;
        }
    }

    public ItemSlotView(float size)
    {
        CustomMinimumSize = new Vector2(size, size);
        MouseFilter = MouseFilterEnum.Stop;

        icon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(icon);
        veil = IconVeilView.Attach(this);

        var overlay = new Control { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(overlay);
        wearTrack = new ColorRect
        {
            Color = WearTrackColour,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 1f, AnchorBottom = 1f,
            OffsetLeft = WearInset, OffsetRight = -WearInset,
            OffsetTop = -WearInset - WearHeight, OffsetBottom = -WearInset,
        };
        overlay.AddChild(wearTrack);
        wearFill = new ColorRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = 0f, AnchorTop = 0f, AnchorBottom = 1f, AnchorRight = 0f,
        };
        wearTrack.AddChild(wearFill);

        plus = UpgradeBadge.Attach(this);

        countLabel = HudStyle.Label(11, HorizontalAlignment.Right);
        countLabel.MouseFilter = MouseFilterEnum.Ignore;
        countLabel.VerticalAlignment = VerticalAlignment.Bottom;
        countLabel.AddThemeStyleboxOverride("normal", new StyleBoxEmpty { ContentMarginRight = 3, ContentMarginBottom = 1 });
        countLabel.AddThemeConstantOverride("outline_size", 3);
        countLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        AddChild(countLabel);

        MouseEntered += () =>
        {
            hover = true;
            ApplyFrame();
            if (!Item.IsEmpty) Hovered?.Invoke(this);
        };
        MouseExited += () =>
        {
            hover = false;
            ApplyFrame();
            Unhovered?.Invoke(this);
        };
        ApplyFrame();
    }

    public void Set(ItemSlot item, int? count = null)
    {
        Item = item;
        if (item.IsEmpty)
        {
            icon.Texture = null;
            countLabel.Text = "";
            plus.Clear();
            veil.Clear();
        }
        else
        {
            icon.Texture = ItemIcons.Get(item.ItemId);
            int shown = count ?? ItemData.ShownCount(ItemData.Get(item.ItemId), item);
            countLabel.Text = shown > 1 ? shown.ToString() : "";
            plus.Set(item.ItemId);
            if (Veiled) veil.Set(item);
        }
        ApplyFrame();
        if (!hover) return;
        if (item.IsEmpty) Unhovered?.Invoke(this);
        else Hovered?.Invoke(this);
    }

    public void Clear() => Set(default);

    public SlotLook Look
    {
        get => look;
        set
        {
            if (look == value) return;
            look = value;
            Modulate = value switch
            {
                SlotLook.Dimmed => DimmedTint,
                SlotLook.Unfit => UnfitTint,
                SlotLook.Staged => StagedTint,
                _ => Colors.White,
            };
            icon.SelfModulate = Locked ? LockedIconTint : value == SlotLook.Ghost ? GhostTint : Colors.White;
            ApplyFrame();
        }
    }

    public void SetWear(float? share, Color tint)
    {
        wearTrack.Visible = share.HasValue;
        if (share is not { } value) return;
        wearFill.Color = tint;
        wearFill.AnchorRight = Mathf.Clamp(value, 0f, 1f);
    }

    private void ApplyFrame()
    {
        Color? grade = look == SlotLook.Selected ? UiTheme.GoldBright
            : Item.IsEmpty ? null : ItemGrade.Tint(Item.ItemId);
        var key = (grade, hover);
        if (!Frames.TryGetValue(key, out var frame))
            Frames[key] = frame = UiTheme.Slot(grade, hover);
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
        if (PicksAmount && data.VariantType == Variant.Type.Dictionary)
            DragLayer.ShowStack(this, Item, atPosition, data.AsGodotDictionary());
        else
            DragLayer.ShowItem(this, Item.ItemId, atPosition);
        return data;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) => CanDrop?.Invoke(this, data) ?? false;

    public override void _DropData(Vector2 atPosition, Variant data) => Dropped?.Invoke(this, data);
}
