using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private void HoverWarehouseCell(WarehouseCell cell)
    {
        if (cell.Item.IsEmpty) HideItemTooltip();
        else ShowItemTooltip(cell.TipSlot, cell.Item);
    }

    private sealed partial class WarehouseCell : PanelContainer
    {
        public readonly int Index;
        public System.Action<int>? OnActivate;
        public System.Action<WarehouseCell>? OnHover;
        public System.Action? OnHoverEnd;

        private readonly TextureRect _icon;
        private readonly Label _count;
        private readonly UpgradeBadge _plus;
        private readonly bool _bag;
        private readonly StyleBox _normal = UiTheme.Slot();
        private bool _hovering;

        public ItemSlot Item { get; private set; }
        public int TipSlot => _bag ? Index : -1;

        public WarehouseCell(int index, bool bag = false)
        {
            Index = index;
            _bag = bag;
            CustomMinimumSize = new Vector2(44, 44);
            AddThemeStyleboxOverride("panel", _normal);
            _icon = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _icon.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(_icon);
            _count = HudStyle.Label(11, HorizontalAlignment.Right);
            _count.MouseFilter = MouseFilterEnum.Ignore;
            _count.VerticalAlignment = VerticalAlignment.Bottom;
            _count.SizeFlagsHorizontal = _count.SizeFlagsVertical = SizeFlags.Fill;
            _count.SetAnchorsPreset(LayoutPreset.FullRect);
            _count.AddThemeStyleboxOverride("normal",
                new StyleBoxEmpty { ContentMarginRight = 3, ContentMarginBottom = 1 });
            _count.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.92f));
            _count.AddThemeConstantOverride("shadow_offset_x", 1);
            _count.AddThemeConstantOverride("shadow_offset_y", 1);
            _count.AddThemeConstantOverride("outline_size", 3);
            _count.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
            AddChild(_count);
            _plus = UpgradeBadge.Attach(this);

            MouseEntered += () => { _hovering = true; Notify(); };
            MouseExited += () => { _hovering = false; OnHoverEnd?.Invoke(); };
        }

        public void Set(ItemSlot it)
        {
            Item = it;
            if (it.IsEmpty)
            {
                _icon.Texture = null;
                _count.Text = "";
                _plus.Clear();
                _icon.SelfModulate = Colors.White;
                AddThemeStyleboxOverride("panel", _normal);
            }
            else
            {
                _icon.Texture = ItemData.Icon(it.ItemId);
                int shown = ItemData.ShownCount(ItemData.Get(it.ItemId), it);
                _count.Text = shown > 1 ? shown.ToString() : "";
                _plus.Set(it.ItemId);
                SealLook.Apply(it.State, _icon, this, _normal);
            }
            Notify();
        }

        private void Notify()
        {
            if (!_hovering || !IsVisibleInTree()) return;
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
}
