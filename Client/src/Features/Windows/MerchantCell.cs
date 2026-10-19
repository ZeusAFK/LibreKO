using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class MerchantCell : PanelContainer
{
    private readonly int Index;
    public System.Action<int>? OnActivate;
    public System.Action<int>? OnSecondary;
    public System.Action<MerchantCell>? OnHover;
    public System.Action? OnHoverEnd;
    public System.Action<int, int>? OnDropFrom;
    public string DragKey = "";
    public string AcceptKey = "";

    private readonly TextureRect icon;
    private readonly Label count;
    private readonly UpgradeBadge plus;
    private bool hovering;

    public ItemSlot Item { get; private set; }
    public int TipSlot { get; set; } = -1;
    public string Note { get; private set; } = "";

    public MerchantCell(int index, int size = 45)
    {
        Index = index;
        CustomMinimumSize = new Vector2(size, size);
        AddThemeStyleboxOverride("panel", UiTheme.Slot());

        icon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        icon.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(icon);

        count = Overlay(11, HorizontalAlignment.Right, VerticalAlignment.Bottom);
        AddChild(count);

        plus = UpgradeBadge.Attach(this);

        MouseEntered += () => { hovering = true; Notify(); };
        MouseExited += () => { hovering = false; OnHoverEnd?.Invoke(); };
    }

    public void Set(ItemSlot item, string note = "")
    {
        Item = item;
        Note = item.IsEmpty ? "" : note;
        if (item.IsEmpty)
        {
            icon.Texture = null;
            count.Text = "";
            plus.Clear();
        }
        else
        {
            icon.Texture = ItemIcons.Get(item.ItemId);
            int shown = ItemData.ShownCount(ItemData.Get(item.ItemId), item);
            count.Text = shown > 1 ? shown.ToString() : "";
            plus.Set(item.ItemId);
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
        if (!hovering || !IsVisibleInTree()) return;
        if (Item.IsEmpty) OnHoverEnd?.Invoke();
        else OnHover?.Invoke(this);
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (DragKey.Length == 0 || Item.IsEmpty) return default;

        DragLayer.ShowItem(this, Item.ItemId, atPosition);

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
        if (ev is not InputEventMouseButton { Pressed: true } click) return;
        var handler = click.ButtonIndex switch
        {
            MouseButton.Left => OnActivate,
            MouseButton.Right => OnSecondary,
            _ => null,
        };
        if (handler == null) return;
        handler(Index);
        AcceptEvent();
    }
}
