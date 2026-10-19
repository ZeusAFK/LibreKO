using Godot;

namespace LibreKO;

internal sealed partial class WarehouseCell : PanelContainer
{
    private readonly int Index;
    public System.Action<int>? OnActivate;
    public System.Action<WarehouseCell>? OnHover;
    public System.Action? OnHoverEnd;

    private readonly TextureRect icon;
    private readonly Label count;
    private readonly UpgradeBadge plus;
    private readonly IconVeilView veil;
    private readonly bool bag;
    private readonly StyleBox normal = UiTheme.Slot();
    private bool hovering;

    public ItemSlot Item { get; private set; }
    public int TipSlot => bag ? Index : -1;

    public WarehouseCell(int index, bool bag = false)
    {
        Index = index;
        this.bag = bag;
        CustomMinimumSize = new Vector2(44, 44);
        AddThemeStyleboxOverride("panel", normal);
        icon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        icon.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(icon);
        count = HudStyle.Label(11, HorizontalAlignment.Right);
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
        veil = IconVeilView.Attach(this);
        AddChild(count);
        plus = UpgradeBadge.Attach(this);

        MouseEntered += () => { hovering = true; Notify(); };
        MouseExited += () => { hovering = false; OnHoverEnd?.Invoke(); };
    }

    public void Set(ItemSlot it)
    {
        Item = it;
        if (it.IsEmpty)
        {
            icon.Texture = null;
            count.Text = "";
            plus.Clear();
            veil.Clear();
            icon.SelfModulate = Colors.White;
            AddThemeStyleboxOverride("panel", normal);
        }
        else
        {
            icon.Texture = ItemIcons.Get(it.ItemId);
            int shown = ItemData.ShownCount(ItemData.Get(it.ItemId), it);
            count.Text = shown > 1 ? shown.ToString() : "";
            plus.Set(it.ItemId);
            veil.Set(it);
            icon.SelfModulate = Colors.White;
            AddThemeStyleboxOverride("panel", normal);
        }
        Notify();
    }

    private void Notify()
    {
        if (!hovering || !IsVisibleInTree()) return;
        if (Item.IsEmpty) OnHoverEnd?.Invoke();
        else OnHover?.Invoke(this);
    }

    public override void _GuiInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        {
            OnActivate?.Invoke(Index);
            AcceptEvent();
        }
    }
}
