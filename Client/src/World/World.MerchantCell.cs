using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private sealed partial class MerchantCell : PanelContainer
    {
        public readonly int Index;
        public System.Action<int>? OnActivate;
        public System.Action<MerchantCell>? OnHover;
        public System.Action? OnHoverEnd;
        public System.Action<int, int>? OnDropFrom;
        public string DragKey = "";
        public string AcceptKey = "";

        private readonly TextureRect _icon;
        private readonly Label _count;
        private readonly UpgradeBadge _plus;
        private bool _hovering;

        public ItemSlot Item { get; private set; }
        public int TipSlot { get; set; } = -1;
        public string Note { get; private set; } = "";

        public MerchantCell(int index, int size = 45)
        {
            Index = index;
            CustomMinimumSize = new Vector2(size, size);
            AddThemeStyleboxOverride("panel", UiTheme.Slot());

            _icon = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _icon.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(_icon);

            _count = Overlay(11, HorizontalAlignment.Right, VerticalAlignment.Bottom);
            AddChild(_count);

            _plus = UpgradeBadge.Attach(this);

            MouseEntered += () => { _hovering = true; Notify(); };
            MouseExited += () => { _hovering = false; OnHoverEnd?.Invoke(); };
        }

        public void Set(ItemSlot item, string note = "")
        {
            Item = item;
            Note = item.IsEmpty ? "" : note;
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
            Notify();
        }

        private static Label Overlay(int size, HorizontalAlignment h, VerticalAlignment v)
        {
            var label = HudStyle.Label(size, h);
            label.MouseFilter = MouseFilterEnum.Ignore;
            label.VerticalAlignment = v;
            label.SizeFlagsHorizontal = label.SizeFlagsVertical = SizeFlags.Fill;
            label.SetAnchorsPreset(LayoutPreset.FullRect);
            label.AddThemeStyleboxOverride("normal",
                new StyleBoxEmpty { ContentMarginLeft = 2, ContentMarginRight = 3, ContentMarginBottom = 1 });
            label.AddThemeConstantOverride("outline_size", 3);
            label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
            return label;
        }

        private void Notify()
        {
            if (!_hovering || !IsVisibleInTree()) return;
            if (Item.IsEmpty) OnHoverEnd?.Invoke();
            else OnHover?.Invoke(this);
        }

        public override Variant _GetDragData(Vector2 atPosition)
        {
            if (DragKey.Length == 0 || Item.IsEmpty) return default;

            var preview = new TextureRect
            {
                Texture = ItemData.Icon(Item.ItemId),
                CustomMinimumSize = new Vector2(40, 40),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            };
            UpgradeBadge.Show(preview, Item.ItemId);
            SetDragPreview(preview);

            return new Godot.Collections.Dictionary
            {
                { DragKey, Index },
                { "id", Item.ItemId },
            };
        }

        public override bool _CanDropData(Vector2 atPosition, Variant data) =>
            AcceptKey.Length > 0
            && OnDropFrom != null
            && data.VariantType == Variant.Type.Dictionary
            && data.AsGodotDictionary().ContainsKey(AcceptKey);

        public override void _DropData(Vector2 atPosition, Variant data) =>
            OnDropFrom?.Invoke(data.AsGodotDictionary()[AcceptKey].AsInt32(), Index);

        public override void _GuiInput(InputEvent ev)
        {
            if (OnActivate != null
                && ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                OnActivate(Index);
                AcceptEvent();
            }
        }
    }

    private static GridContainer MerchantGrid(int columns, int spacing = 4)
    {
        var grid = new GridContainer { Columns = columns };
        grid.AddThemeConstantOverride("h_separation", spacing);
        grid.AddThemeConstantOverride("v_separation", spacing);
        return grid;
    }

    private MerchantCell[] BuildMerchantGrid(
        Control parent, int count, int columns, System.Action<int>? onClick, int size = 45,
        string dragKey = "", string acceptKey = "", System.Action<int, int>? onDropFrom = null)
    {
        var grid = MerchantGrid(columns);
        parent.AddChild(grid);
        var cells = new MerchantCell[count];
        for (int i = 0; i < count; i++)
        {
            var cell = new MerchantCell(i, size)
            {
                OnActivate = onClick,
                OnHover = HoverMerchantCell,
                OnHoverEnd = HideItemTooltip,
                DragKey = dragKey,
                AcceptKey = acceptKey,
                OnDropFrom = onDropFrom,
            };
            cells[i] = cell;
            grid.AddChild(cell);
        }
        return cells;
    }

    private void HoverMerchantCell(MerchantCell cell)
    {
        if (cell.Item.IsEmpty) HideItemTooltip();
        else ShowItemTooltip(cell.TipSlot, cell.Item, cell.Note);
    }

    private static ItemSlot StallSlot(Network.MerchantStallItem it) => new()
    {
        ItemId = it.ItemId,
        Count = (short)it.Count,
        Durability = it.Durability,
    };
}
