using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const int PusCartWidth = 292;
    private const int PusWellMargin = 8;
    private const int PusCartTextWidth = PusCartWidth - 2 * PusWellMargin;
    private const float PusCartIconSide = 36f;
    private const float PusStepperButtonSide = 24f;
    private const float PusStepperCountWidth = 34f;
    private const float PusCheckoutHeight = 42f;
    private const float PusCheckoutTouchHeight = 56f;
    private const int PusCheckoutFontSize = 15;
    private const int PusTotalFontSize = 17;
    private const int PusGiftNameFontSize = 17;
    private const float PusGiftIconSide = 26f;

    private Label _pusCartTitle = null!;
    private Button _pusClearCart = null!;
    private ScrollContainer _pusCartScroll = null!;
    private VBoxContainer _pusCartLines = null!;
    private Label _pusCartEmpty = null!;
    private Button _pusGiftButton = null!;
    private PanelContainer _pusGiftBanner = null!;
    private Label _pusGiftName = null!;
    private Label _pusGiftInfo = null!;
    private HBoxContainer _pusTotalRow = null!;
    private Label _pusShortfall = null!;
    private Button _pusCheckout = null!;
    private Label _pusCartStatus = null!;
    private PusRecipient? _pusGiftRecipient;

    private Control BuildPusCartColumn()
    {
        var well = PusWell();
        well.CustomMinimumSize = new Vector2(PusCartWidth, 0);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        well.AddChild(column);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 6);
        column.AddChild(head);
        _pusCartTitle = UiTheme.Text("My Cart", 15, UiTheme.GoldBright);
        _pusCartTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _pusCartTitle.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(_pusCartTitle);
        _pusClearCart = UiTheme.SmallButton("Clear all", "Empty the cart");
        _pusClearCart.Pressed += () =>
        {
            _pusCart.Clear();
            SetPusCartStatus("", false);
            RenderPusCart();
        };
        head.AddChild(_pusClearCart);

        _pusCartScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        UiTheme.ThinScrollbar(_pusCartScroll.GetVScrollBar());
        column.AddChild(_pusCartScroll);
        _pusCartLines = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _pusCartLines.AddThemeConstantOverride("separation", 4);
        _pusCartScroll.AddChild(_pusCartLines);
        _pusCartEmpty = UiTheme.Text("Your cart is empty.\nClick an item to look at it and add it here.", 13, UiTheme.TextLo, HorizontalAlignment.Center);
        _pusCartEmpty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _pusCartEmpty.CustomMinimumSize = new Vector2(PusCartTextWidth, 0);
        _pusCartEmpty.VerticalAlignment = VerticalAlignment.Center;
        _pusCartEmpty.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        column.AddChild(_pusCartEmpty);

        column.AddChild(UiTheme.Rule());

        _pusGiftButton = UiTheme.SmallButton("Send as a gift…", "Mail everything in the cart to a friend or clan member");
        _pusGiftButton.Icon = UiIcons.Get("system/gift");
        _pusGiftButton.AddThemeConstantOverride("icon_max_width", 16);
        _pusGiftButton.AddThemeColorOverride("icon_normal_color", UiTheme.Gold);
        _pusGiftButton.AddThemeColorOverride("icon_hover_color", UiTheme.GoldBright);
        _pusGiftButton.CustomMinimumSize = new Vector2(0, Platform.Pick(32f, 46f));
        _pusGiftButton.AddThemeFontSizeOverride("font_size", 13);
        _pusGiftButton.Pressed += OpenPusGiftPicker;
        column.AddChild(_pusGiftButton);
        column.AddChild(BuildPusGiftBanner());

        _pusTotalRow = new HBoxContainer();
        _pusTotalRow.AddThemeConstantOverride("separation", 6);
        column.AddChild(_pusTotalRow);

        _pusShortfall = UiTheme.Text("", 12, UiTheme.Bad, HorizontalAlignment.Right);
        _pusShortfall.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _pusShortfall.CustomMinimumSize = new Vector2(PusCartTextWidth, 0);
        column.AddChild(_pusShortfall);

        _pusCartStatus = UiTheme.Text("", 12, UiTheme.TextLo, HorizontalAlignment.Center);
        _pusCartStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _pusCartStatus.CustomMinimumSize = new Vector2(PusCartTextWidth, 0);
        _pusCartStatus.Visible = false;
        column.AddChild(_pusCartStatus);

        _pusCheckout = UiTheme.ActionButton("Buy cart", "Buy everything in the cart");
        _pusCheckout.CustomMinimumSize = new Vector2(0, Platform.Pick(PusCheckoutHeight, PusCheckoutTouchHeight));
        _pusCheckout.AddThemeFontSizeOverride("font_size", PusCheckoutFontSize);
        _pusCheckout.ClipText = true;
        _pusCheckout.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _pusCheckout.Pressed += BuyPusCart;
        column.AddChild(_pusCheckout);
        return well;
    }

    private Control BuildPusGiftBanner()
    {
        _pusGiftBanner = new PanelContainer { Visible = false };
        var style = UiTheme.Inset(4);
        style.BorderColor = UiTheme.GoldVivid;
        style.BgColor = new Color(0.17f, 0.13f, 0.06f, 0.92f);
        _pusGiftBanner.AddThemeStyleboxOverride("panel", style);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 2);
        _pusGiftBanner.AddChild(column);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 6);
        column.AddChild(head);
        head.AddChild(new TextureRect
        {
            Texture = UiIcons.Get("system/gift"),
            CustomMinimumSize = new Vector2(PusGiftIconSide, PusGiftIconSide),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SelfModulate = UiTheme.GoldVivid,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        var caption = UiTheme.Text("Gift for", 13, UiTheme.TextLo);
        caption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        caption.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(caption);
        var change = UiTheme.SmallButton("Change", "Choose another recipient");
        change.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        change.Pressed += OpenPusGiftPicker;
        head.AddChild(change);
        var remove = UiTheme.IconButton(UiIcons.Get("system/close"), "Buy for yourself instead");
        remove.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        remove.Pressed += () =>
        {
            _pusGiftRecipient = null;
            RenderPusCart();
        };
        head.AddChild(remove);

        _pusGiftName = UiTheme.Text("", PusGiftNameFontSize, UiTheme.GoldBright);
        _pusGiftName.ClipText = true;
        _pusGiftName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        column.AddChild(_pusGiftName);
        _pusGiftInfo = UiTheme.Text("", 12, UiTheme.TextLo);
        _pusGiftInfo.ClipText = true;
        _pusGiftInfo.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        column.AddChild(_pusGiftInfo);
        return _pusGiftBanner;
    }

    private void RenderPusCart()
    {
        if (!IsInstanceValid(_pusCartLines)) return;
        ClearChildren(_pusCartLines);
        foreach (var line in _pusCart.Lines)
            _pusCartLines.AddChild(BuildPusCartLine(line));

        _pusCartTitle.Text = _pusCart.IsEmpty ? "My Cart" : $"My Cart ({_pusCart.Units})";
        _pusClearCart.Disabled = _pusCart.IsEmpty || _pusPending != PusPurchase.None;
        _pusCartScroll.Visible = !_pusCart.IsEmpty;
        _pusCartEmpty.Visible = _pusCart.IsEmpty;

        _pusGiftButton.Visible = _pusGiftRecipient == null;
        _pusGiftButton.Disabled = _pusCart.IsEmpty || _pusPending != PusPurchase.None;
        _pusGiftBanner.Visible = _pusGiftRecipient != null;
        if (_pusGiftRecipient is { } recipient)
        {
            _pusGiftName.Text = recipient.Name;
            _pusGiftInfo.Text = PusRecipientInfo(recipient);
        }

        ClearChildren(_pusTotalRow);
        var total = UiTheme.Text("Total", 14, UiTheme.TextHi);
        total.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _pusTotalRow.AddChild(total);
        var affordable = _pusCart.Affordable(Sheet.KnightCash);
        _pusTotalRow.AddChild(PusPrice(_pusCart.Total, PusTotalFontSize, affordable ? UiTheme.Premium : UiTheme.Bad));
        _pusShortfall.Text = affordable ? "" : $"You need {_pusCart.Total - Sheet.KnightCash:n0} more cash.";
        _pusShortfall.Visible = !affordable;

        _pusCheckout.Text = _pusPending == PusPurchase.Cart ? "Buying…"
            : _pusGiftRecipient is { } gift ? $"Buy gift for {gift.Name}"
            : "Buy cart";
        _pusCheckout.TooltipText = _pusGiftRecipient is { } to
            ? $"Buy everything in the cart and mail it to {to.Name}"
            : "Buy everything in the cart; it arrives in your mailbox";
        _pusCheckout.Disabled = _pusCart.IsEmpty || !affordable || _pusPending != PusPurchase.None;
        RefreshPusCardStates();
    }

    private Control BuildPusCartLine(PowerUpStoreCart.Line line)
    {
        var row = UiTheme.RowPanel();
        var hb = new HBoxContainer();
        hb.AddThemeConstantOverride("separation", 8);
        row.AddChild(hb);

        var icon = new TextureRect
        {
            Texture = ItemData.Icon(line.Entry.ItemId),
            CustomMinimumSize = new Vector2(PusCartIconSide, PusCartIconSide),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        icon.MouseEntered += () => ShowItemTooltip(-1, TooltipItem(line.Entry.ItemId));
        icon.MouseExited += HideItemTooltip;
        hb.AddChild(icon);

        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 2);
        hb.AddChild(body);
        var name = UiTheme.Text(line.Entry.Name, 13, UiTheme.TextHi);
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        body.AddChild(name);

        var controls = new HBoxContainer();
        controls.AddThemeConstantOverride("separation", 2);
        body.AddChild(controls);
        var id = line.Entry.Id;
        controls.AddChild(PusStepButton("−", "One less", line.Count > 1, () => { _pusCart.Decrease(id); RenderPusCart(); }));
        var count = UiTheme.Text(line.Count.ToString(), 13, UiTheme.TextHi, HorizontalAlignment.Center);
        count.CustomMinimumSize = new Vector2(PusStepperCountWidth, 0);
        count.VerticalAlignment = VerticalAlignment.Center;
        controls.AddChild(count);
        controls.AddChild(PusStepButton("+", "One more", _pusCart.CanIncrease(id), () => { _pusCart.Increase(id); RenderPusCart(); }));
        controls.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        controls.AddChild(PusPrice(line.Total, 13));

        var remove = UiTheme.IconButton(UiIcons.Get("system/close"), "Remove from the cart");
        remove.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        remove.Pressed += () => { _pusCart.Remove(id); RenderPusCart(); };
        hb.AddChild(remove);
        return row;
    }

    private Button PusStepButton(string glyph, string tooltip, bool enabled, System.Action step)
    {
        var button = UiTheme.IconButton(glyph, tooltip);
        var side = Platform.Pick(PusStepperButtonSide, PusStepperButtonSide * 1.5f);
        button.CustomMinimumSize = new Vector2(side, side);
        button.AddThemeFontSizeOverride("font_size", 14);
        var disabled = (StyleBoxFlat)button.GetThemeStylebox("normal").Duplicate();
        disabled.BgColor = new Color(disabled.BgColor, disabled.BgColor.A * 0.5f);
        disabled.BorderColor = new Color(disabled.BorderColor, disabled.BorderColor.A * 0.5f);
        button.AddThemeStyleboxOverride("disabled", disabled);
        button.AddThemeColorOverride("font_disabled_color", UiTheme.TextDim);
        button.Disabled = !enabled || _pusPending != PusPurchase.None;
        button.Pressed += step;
        return button;
    }

    private static string PusRecipientInfo(PusRecipient recipient)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (recipient.Level > 0) parts.Add($"Level {recipient.Level}");
        if (recipient.Class > 0)
        {
            parts.Add(CharacterClassCatalog.DisplayName(recipient.Class));
            parts.Add(Nations.Name(Nations.OfClass(recipient.Class)));
        }
        return string.Join(" · ", parts);
    }

    private void SetPusCartStatus(string text, bool bad)
    {
        _pusCartStatus.Text = text;
        _pusCartStatus.Visible = text.Length > 0;
        _pusCartStatus.AddThemeColorOverride("font_color", bad ? UiTheme.Bad : UiTheme.Good);
    }

    private void BuyPusCart()
    {
        if (_pusCart.IsEmpty || _pusPending != PusPurchase.None) return;
        if (!_pusCart.Affordable(Sheet.KnightCash))
        {
            SetPusCartStatus("You don't have enough cash for this.", true);
            return;
        }
        _pusPending = PusPurchase.Cart;
        SetPusCartStatus("", false);
        Net.I.SendPowerUpStorePurchase(_pusGiftRecipient?.Name ?? "", _pusCart.Lines);
        RenderPusCart();
    }
}
