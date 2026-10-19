using Godot;

namespace LibreKO.Features.Windows;

internal static class MerchantUi
{
    internal const int StallSlots = MerchantWire.StallSlots;
    internal const int StallColumns = 6;
    internal const int StallBagColumns = 7;

    internal readonly record struct PendingTrade(int MerchantId, int ItemId, int Count, int Price);

    internal static HBoxContainer MoneyRow(string caption, out Label value, Color tint)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var label = UiTheme.Text(caption, 12, UiTheme.TextLo);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(label);
        value = UiTheme.Text("0", 13, tint);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(value);
        return row;
    }

    internal static string Money(long amount) => $"{amount:n0}";

    internal static string PriceNote(string caption, int price, int count) =>
        count > 1
            ? $"{caption} {Money(price)} each   ({Money((long)price * count)} total)"
            : $"{caption} {Money(price)}";

    internal static GridContainer Grid(int columns, int spacing = 4)
    {
        var grid = new GridContainer { Columns = columns };
        grid.AddThemeConstantOverride("h_separation", spacing);
        grid.AddThemeConstantOverride("v_separation", spacing);
        return grid;
    }

    internal static ItemSlot StallSlot(Network.MerchantStallItem it) => new()
    {
        ItemId = it.ItemId,
        Count = (short)it.Count,
        Durability = it.Durability,
    };

    internal static MerchantCell[] BuildGrid(
        IItemTooltips tooltips, Control parent, int count, int columns, System.Action<int>? onClick, int size = 45,
        string dragKey = "", string acceptKey = "", System.Action<int, int>? onDropFrom = null)
    {
        var grid = Grid(columns);
        parent.AddChild(grid);
        var cells = new MerchantCell[count];
        for (int i = 0; i < count; i++)
        {
            var cell = new MerchantCell(i, size)
            {
                OnActivate = onClick,
                OnHover = hovered => HoverCell(tooltips, hovered),
                OnHoverEnd = tooltips.Hide,
                DragKey = dragKey,
                AcceptKey = acceptKey,
                OnDropFrom = onDropFrom,
            };
            cells[i] = cell;
            grid.AddChild(cell);
        }
        return cells;
    }

    internal static void HoverCell(IItemTooltips tooltips, MerchantCell cell)
    {
        if (cell.Item.IsEmpty) tooltips.Hide();
        else tooltips.Show(cell.TipSlot, cell.Item, cell.Note);
    }
}
