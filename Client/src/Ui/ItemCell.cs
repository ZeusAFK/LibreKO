using Godot;

namespace LibreKO;

internal partial class ItemCell : PanelContainer
{
    public int Slot { get; private set; }
    public System.Action<int>? OnContext;
    public System.Action<int>? OnClick;
    public System.Action<ItemCell, bool>? OnHoverChanged;
    public System.Action<int, int, int>? OnDropItem;
    public System.Func<int, int, int?, bool>? OnCompanionDrop;
    public ItemSlot Current { get; private set; }
    private readonly TextureRect icon;
    private readonly LockGlyph lockIcon;
    private readonly TextureRect emptyIcon;
    private bool locked;

    public bool Locked
    {
        get => locked;
        set
        {
            if (locked == value) return;
            locked = value;
            lockIcon.Visible = value;
            AddThemeStyleboxOverride("panel", RestStyle);
        }
    }
    private readonly Label count;
    private readonly Label hint;
    private readonly UpgradeBadge plus;
    private readonly IconVeilView veil;
    private string emptyHint = "";
    private StyleBoxFlat normal;
    private StyleBoxFlat hover;
    private readonly StyleBoxFlat lockedStyle = LockedStyle();
    private readonly StyleBoxFlat hitStyle = UiTheme.Slot(UiTheme.GoldBright);
    private static readonly Color UnfitTint = new(1f, 0.6f, 0.6f, 0.45f);
    private static readonly Color StagedTint = new(1f, 1f, 1f, 0.45f);
    private static readonly Color SearchMissTint = new(1f, 1f, 1f, 0.32f);
    private BagFit fit;
    private bool searchMiss;
    private bool shaded;

    public bool Shaded
    {
        set { if (shaded == value) return; shaded = value; ApplyTint(); }
    }
    private bool searchHit;

    public BagFit Fit
    {
        set { fit = value; ApplyTint(); }
    }

    public bool SearchMiss
    {
        set { searchMiss = value; ApplyTint(); }
    }

    public bool SearchHit
    {
        set
        {
            if (searchHit == value) return;
            searchHit = value;
            AddThemeStyleboxOverride("panel", RestStyle);
        }
    }

    private StyleBoxFlat RestStyle => locked ? lockedStyle : searchHit ? hitStyle : normal;

    private void ApplyTint()
    {
        Color tint = fit switch
        {
            BagFit.Unfit => UnfitTint,
            BagFit.Staged => StagedTint,
            _ => Colors.White,
        };
        if (searchMiss) tint *= SearchMissTint;
        Modulate = shaded ? tint * ChaosShade : tint;
    }

    public ItemCell(int slot, float size = 46f)
    {
        Slot = slot;
        CustomMinimumSize = new Vector2(size, size);

        normal = UiTheme.Slot();
        hover = UiTheme.Slot(hover: true);
        AddThemeStyleboxOverride("panel", normal);

        hint = UiTheme.Text("", 9, new Color(UiTheme.TextLo, 0.42f), HorizontalAlignment.Center);
        hint.AddThemeColorOverride("font_color", new Color(UiTheme.TextLo, 0.42f));
        hint.MouseFilter = MouseFilterEnum.Ignore;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.CustomMinimumSize = Vector2.Zero;
        hint.Visible = false;
        AddChild(hint);

        emptyIcon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            SelfModulate = new Color(UiTheme.TextLo, 0.34f),
            Visible = false,
        };
        emptyIcon.SetAnchorsPreset(LayoutPreset.FullRect);
        emptyIcon.OffsetLeft = 9;
        emptyIcon.OffsetTop = 9;
        emptyIcon.OffsetRight = -9;
        emptyIcon.OffsetBottom = -9;
        AddChild(emptyIcon);

        icon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = Vector2.Zero,
            SizeFlagsHorizontal = SizeFlags.Fill,
            SizeFlagsVertical = SizeFlags.Fill,
        };
        icon.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(icon);

        lockIcon = new LockGlyph
        {
            CustomMinimumSize = new Vector2(16, 18),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        AddChild(lockIcon);

        veil = IconVeilView.Attach(this);
        plus = UpgradeBadge.Attach(this);

        count = HudStyle.Label(12, HorizontalAlignment.Right);
        count.MouseFilter = MouseFilterEnum.Ignore;
        count.VerticalAlignment = VerticalAlignment.Bottom;
        count.SizeFlagsHorizontal = count.SizeFlagsVertical = SizeFlags.Fill;
        count.SetAnchorsPreset(LayoutPreset.FullRect);
        count.AddThemeStyleboxOverride("normal",
            new StyleBoxEmpty { ContentMarginRight = 3, ContentMarginBottom = 1 });
        count.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.92f));
        count.AddThemeConstantOverride("shadow_offset_x", 1);
        count.AddThemeConstantOverride("shadow_offset_y", 1);
        count.AddThemeConstantOverride("outline_size", 3);
        count.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        AddChild(count);

        MouseEntered += () =>
        {
            if (locked) return;
            AddThemeStyleboxOverride("panel", hover);
            OnHoverChanged?.Invoke(this, true);
        };
        MouseExited += () =>
        {
            if (locked) return;
            AddThemeStyleboxOverride("panel", RestStyle);
            OnHoverChanged?.Invoke(this, false);
        };
    }

    private static StyleBoxFlat LockedStyle()
    {
        var sb = new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.057f, 0.066f, 0.92f),
            BorderColor = new Color(UiTheme.EdgeSoft, 0.3f),
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(3);
        return sb;
    }

    public Color? Frame
    {
        set
        {
            normal = UiTheme.Slot(value);
            hover = UiTheme.Slot(value, hover: true);
            AddThemeStyleboxOverride("panel", RestStyle);
        }
    }

    public string EmptyHint
    {
        set
        {
            emptyHint = value;
            hint.Text = value;
            emptyIcon.Texture = UiIcons.Equipment(Slot);
            if (!Current.IsEmpty) return;
            emptyIcon.Visible = emptyIcon.Texture != null;
            hint.Visible = emptyIcon.Texture == null && value.Length > 0;
            TooltipText = value;
        }
    }

    public void Bind(int slot, ItemSlot it)
    {
        Slot = slot;
        Set(it);
    }

    public void ShowGhost(int itemId)
    {
        if (!Current.IsEmpty) return;
        icon.Texture = ItemIcons.Get(itemId);
        icon.SelfModulate = GhostTint;
        emptyIcon.Visible = false;
        hint.Visible = false;
    }

    private static readonly Color GhostTint = new(1f, 1f, 1f, 0.32f);

    public void Set(ItemSlot it)
    {
        Current = it;
        if (it.IsEmpty)
        {
            icon.Texture = null;
            count.Text = "";
            plus.Clear();
            veil.Clear();
            emptyIcon.Visible = emptyIcon.Texture != null;
            hint.Visible = emptyIcon.Texture == null && hint.Text.Length > 0;
            icon.SelfModulate = Colors.White;
            if (!locked) AddThemeStyleboxOverride("panel", RestStyle);
            TooltipText = emptyHint;
            return;
        }
        hint.Visible = false;
        emptyIcon.Visible = false;
        icon.Texture = ItemIcons.Get(it.ItemId);
        int shown = ItemData.ShownCount(ItemData.Get(it.ItemId), it);
        count.Text = shown > 1 ? shown.ToString() : "";
        TooltipText = "";
        plus.Set(it.ItemId);
        veil.Set(it);
        icon.SelfModulate = Colors.White;
        if (!locked) AddThemeStyleboxOverride("panel", RestStyle);
    }

    public override void _GuiInput(InputEvent ev)
    {
        if (Locked) return;
        if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }
            || (Platform.TouchUi && ev is InputEventMouseButton { Pressed: true, DoubleClick: true, ButtonIndex: MouseButton.Left }))
        {
            OnContext?.Invoke(Slot);
            AcceptEvent();
        }
        else if (OnClick != null && ev is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left })
        {
            OnClick(Slot);
            AcceptEvent();
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (Locked || Current.IsEmpty) return default;
        var data = new Godot.Collections.Dictionary
        {
            { "id", Current.ItemId },
            { "invFrom", Slot },
        };
        DragLayer.ShowStack(this, Current, atPosition, data);
        return data;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (Locked || Slot < 0 || OnDropItem == null) return false;
        if (data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        if (d.ContainsKey("companionFrom")) return OnCompanionDrop != null;
        if (!d.ContainsKey("invFrom")) return false;
        int from = d["invFrom"].AsInt32();
        return from >= 0 && from != Slot;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        AddThemeStyleboxOverride("panel", normal);
        var d = data.AsGodotDictionary();
        int? amount = DragLayer.ChosenAmount(d);
        if (d.ContainsKey("companionFrom")) OnCompanionDrop?.Invoke(d["companionFrom"].AsInt32(), Slot, amount);
        else OnDropItem?.Invoke(d["invFrom"].AsInt32(), Slot, amount ?? ItemMove.WholeStack);
    }

    private static readonly Color ChaosShade = new(0.45f, 0.45f, 0.45f);

    private sealed partial class LockGlyph : Control
    {
        public override void _Draw()
        {
            var tint = new Color(UiTheme.TextLo, 0.32f);
            float w = Size.X, h = Size.Y;
            float cx = Mathf.Round(w * 0.5f);
            float bodyTop = Mathf.Round(h * 0.44f);
            float inset = Mathf.Round(w * 0.125f);

            DrawRect(new Rect2(inset, bodyTop, w - inset * 2f, h - bodyTop - 1f), tint, false, 1f);

            float sr = Mathf.Round(w * 0.22f);
            float arcCy = bodyTop - Mathf.Round(h * 0.14f);
            DrawArc(new Vector2(cx, arcCy), sr, Mathf.Pi, Mathf.Tau, 16, tint, 1f);
            DrawLine(new Vector2(cx - sr, arcCy), new Vector2(cx - sr, bodyTop), tint, 1f);
            DrawLine(new Vector2(cx + sr, arcCy), new Vector2(cx + sr, bodyTop), tint, 1f);
        }
    }
}
