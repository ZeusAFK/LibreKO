using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private Control BuildTradeRow(int itemId, string priceText, string action,
        System.Action onButton, System.Action onDoubleClick, int tipSlot, ItemSlot tipItem,
        int count = 0, bool worthless = false)
    {
        var row = new TradeRow(onDoubleClick);
        row.AddThemeStyleboxOverride("panel", UiTheme.Row());
        row.MouseEntered += () => ShowItemTooltip(tipSlot, tipItem);
        row.MouseExited += HideItemTooltip;

        var hb = new HBoxContainer();
        hb.AddThemeConstantOverride("separation", 8);
        row.AddChild(hb);

        var icon = new TextureRect
        {
            Texture = ItemData.Icon(itemId),
            CustomMinimumSize = new Vector2(34, 34),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hb.AddChild(icon);

        var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        info.AddThemeConstantOverride("separation", -2);
        info.MouseFilter = Control.MouseFilterEnum.Ignore;
        var name = UiTheme.Text("", 13, UiTheme.TextHi);
        name.Text = count > 1 ? $"{ItemData.DisplayName(itemId)}  x{count}" : ItemData.DisplayName(itemId);
        name.MouseFilter = Control.MouseFilterEnum.Ignore;
        info.AddChild(name);
        var price = UiTheme.Text("", 11, worthless ? UiTheme.TextLo : UiTheme.Gold);
        price.Text = priceText;
        price.MouseFilter = Control.MouseFilterEnum.Ignore;
        info.AddChild(price);
        hb.AddChild(info);

        var btn = new Button { Text = action, FocusMode = Control.FocusModeEnum.None };
        btn.AddThemeFontSizeOverride("font_size", 12);
        btn.Pressed += () => onButton();
        hb.AddChild(btn);
        return row;
    }

    private sealed partial class TradeRow : PanelContainer
    {
        private readonly System.Action _onDoubleClick;
        public TradeRow(System.Action onDoubleClick) => _onDoubleClick = onDoubleClick;

        public override void _GuiInput(InputEvent ev)
        {
            if (ev is InputEventMouseButton { Pressed: true, DoubleClick: true, ButtonIndex: MouseButton.Left })
                _onDoubleClick();
        }
    }
}
