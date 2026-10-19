using Godot;

namespace LibreKO.Features.Windows;

internal sealed class PowerUpStoreCartView
{
    internal const int CartWidth = 292;
    internal const int WellMargin = 8;
    private const int CartTextWidth = CartWidth - 2 * WellMargin;
    private const float CartIconSide = 36f;
    private const float StepperButtonSide = 24f;
    private const float StepperCountWidth = 34f;
    private const float CheckoutHeight = 42f;
    private const float CheckoutTouchHeight = 56f;
    private const int CheckoutFontSize = 15;
    private const int TotalFontSize = 17;
    private const int GiftNameFontSize = 17;
    private const float GiftIconSide = 26f;

    private readonly IItemTooltips tooltips;
    private readonly PowerUpStoreModel model;
    private readonly SelfSession self;

    public PowerUpStoreCartView(IItemTooltips tooltips, PowerUpStoreModel model, SelfSession self)
    {
        this.tooltips = tooltips;
        this.model = model;
        this.self = self;
    }

    internal event Action? GiftRequested;

    internal event Action? CheckoutRequested;

    internal event Action? Rendered;

    private Label cartTitle = null!;
    private Button clearCart = null!;
    private ScrollContainer cartScroll = null!;
    private VBoxContainer cartLines = null!;
    private Label cartEmpty = null!;
    private Button giftButton = null!;
    private PanelContainer giftBanner = null!;
    private Label giftName = null!;
    private Label giftInfo = null!;
    private HBoxContainer totalRow = null!;
    private Label shortfall = null!;
    private Button checkout = null!;
    private Label cartStatus = null!;

    internal Control BuildCartColumn()
    {
        var well = PowerUpStoreUi.Well();
        well.CustomMinimumSize = new Vector2(CartWidth, 0);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        well.AddChild(column);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 6);
        column.AddChild(head);
        cartTitle = UiTheme.Text("My Cart", 15, UiTheme.GoldBright);
        cartTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        cartTitle.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(cartTitle);
        clearCart = UiTheme.SmallButton("Clear all", "Empty the cart");
        clearCart.Pressed += () =>
        {
            model.Cart.Clear();
            SetCartStatus("", false);
            RenderCart();
        };
        head.AddChild(clearCart);

        cartScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        UiTheme.ThinScrollbar(cartScroll.GetVScrollBar());
        column.AddChild(cartScroll);
        cartLines = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        cartLines.AddThemeConstantOverride("separation", 4);
        cartScroll.AddChild(cartLines);
        cartEmpty = UiTheme.Text("Your cart is empty.\nClick an item to look at it and add it here.", 13, UiTheme.TextLo, HorizontalAlignment.Center);
        cartEmpty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        cartEmpty.CustomMinimumSize = new Vector2(CartTextWidth, 0);
        cartEmpty.VerticalAlignment = VerticalAlignment.Center;
        cartEmpty.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        column.AddChild(cartEmpty);

        column.AddChild(UiTheme.Rule());

        giftButton = UiTheme.SmallButton("Send as a gift…", "Mail everything in the cart to a friend or clan member");
        giftButton.Icon = UiIcons.Get("system/gift");
        giftButton.AddThemeConstantOverride("icon_max_width", 16);
        giftButton.AddThemeColorOverride("icon_normal_color", UiTheme.Gold);
        giftButton.AddThemeColorOverride("icon_hover_color", UiTheme.GoldBright);
        giftButton.CustomMinimumSize = new Vector2(0, Platform.Pick(32f, 46f));
        giftButton.AddThemeFontSizeOverride("font_size", 13);
        giftButton.Pressed += () => GiftRequested?.Invoke();
        column.AddChild(giftButton);
        column.AddChild(BuildGiftBanner());

        totalRow = new HBoxContainer();
        totalRow.AddThemeConstantOverride("separation", 6);
        column.AddChild(totalRow);

        shortfall = UiTheme.Text("", 12, UiTheme.Bad, HorizontalAlignment.Right);
        shortfall.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        shortfall.CustomMinimumSize = new Vector2(CartTextWidth, 0);
        column.AddChild(shortfall);

        cartStatus = UiTheme.Text("", 12, UiTheme.TextLo, HorizontalAlignment.Center);
        cartStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        cartStatus.CustomMinimumSize = new Vector2(CartTextWidth, 0);
        cartStatus.Visible = false;
        column.AddChild(cartStatus);

        checkout = UiTheme.ActionButton("Buy cart", "Buy everything in the cart");
        checkout.CustomMinimumSize = new Vector2(0, Platform.Pick(CheckoutHeight, CheckoutTouchHeight));
        checkout.AddThemeFontSizeOverride("font_size", CheckoutFontSize);
        checkout.ClipText = true;
        checkout.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        checkout.Pressed += () => CheckoutRequested?.Invoke();
        column.AddChild(checkout);
        return well;
    }

    private Control BuildGiftBanner()
    {
        giftBanner = new PanelContainer { Visible = false };
        var style = UiTheme.Inset(4);
        style.BorderColor = UiTheme.GoldVivid;
        style.BgColor = new Color(0.17f, 0.13f, 0.06f, 0.92f);
        giftBanner.AddThemeStyleboxOverride("panel", style);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 2);
        giftBanner.AddChild(column);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 6);
        column.AddChild(head);
        head.AddChild(new TextureRect
        {
            Texture = UiIcons.Get("system/gift"),
            CustomMinimumSize = new Vector2(GiftIconSide, GiftIconSide),
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
        change.Pressed += () => GiftRequested?.Invoke();
        head.AddChild(change);
        var remove = UiTheme.IconButton(UiIcons.Get("system/close"), "Buy for yourself instead");
        remove.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        remove.Pressed += () =>
        {
            model.GiftRecipient = null;
            RenderCart();
        };
        head.AddChild(remove);

        giftName = UiTheme.Text("", GiftNameFontSize, UiTheme.GoldBright);
        giftName.ClipText = true;
        giftName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        column.AddChild(giftName);
        giftInfo = UiTheme.Text("", 12, UiTheme.TextLo);
        giftInfo.ClipText = true;
        giftInfo.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        column.AddChild(giftInfo);
        return giftBanner;
    }

    internal void RenderCart()
    {
        if (!GodotObject.IsInstanceValid(cartLines)) return;
        var cart = model.Cart;
        UiKit.ClearChildren(cartLines);
        foreach (var line in cart.Lines)
            cartLines.AddChild(BuildCartLine(line));

        cartTitle.Text = cart.IsEmpty ? "My Cart" : $"My Cart ({cart.Units})";
        clearCart.Disabled = cart.IsEmpty || model.Busy;
        cartScroll.Visible = !cart.IsEmpty;
        cartEmpty.Visible = cart.IsEmpty;

        giftButton.Visible = model.GiftRecipient == null;
        giftButton.Disabled = cart.IsEmpty || model.Busy;
        giftBanner.Visible = model.GiftRecipient != null;
        if (model.GiftRecipient is { } recipient)
        {
            giftName.Text = recipient.Name;
            giftInfo.Text = recipient.Summary;
        }

        UiKit.ClearChildren(totalRow);
        var total = UiTheme.Text("Total", 14, UiTheme.TextHi);
        total.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        totalRow.AddChild(total);
        var affordable = cart.Affordable(self.Sheet.KnightCash);
        totalRow.AddChild(PowerUpStoreUi.Price(cart.Total, TotalFontSize, affordable ? UiTheme.Premium : UiTheme.Bad));
        shortfall.Text = affordable ? "" : $"You need {cart.Total - self.Sheet.KnightCash:n0} more cash.";
        shortfall.Visible = !affordable;

        checkout.Text = model.Pending == PowerUpStorePurchase.Cart ? "Buying…"
            : model.GiftRecipient is { } gift ? $"Buy gift for {gift.Name}"
            : "Buy cart";
        checkout.TooltipText = model.GiftRecipient is { } to
            ? $"Buy everything in the cart and mail it to {to.Name}"
            : "Buy everything in the cart; it arrives in your mailbox";
        checkout.Disabled = cart.IsEmpty || !affordable || model.Busy;
        Rendered?.Invoke();
    }

    private Control BuildCartLine(PowerUpStoreCart.Line line)
    {
        var row = UiTheme.RowPanel();
        var hb = new HBoxContainer();
        hb.AddThemeConstantOverride("separation", 8);
        row.AddChild(hb);

        var icon = new TextureRect
        {
            Texture = ItemIcons.Get(line.Entry.ItemId),
            CustomMinimumSize = new Vector2(CartIconSide, CartIconSide),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        icon.MouseEntered += () => tooltips.Show(-1, UiKit.TooltipItem(line.Entry.ItemId));
        icon.MouseExited += tooltips.Hide;
        hb.AddChild(icon);

        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 2);
        hb.AddChild(body);
        var name = UiTheme.Text(line.Entry.Name, 13, UiTheme.TextHi);
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        body.AddChild(name);

        var cart = model.Cart;
        var controls = new HBoxContainer();
        controls.AddThemeConstantOverride("separation", 2);
        body.AddChild(controls);
        var id = line.Entry.Id;
        controls.AddChild(StepButton("−", "One less", line.Count > 1, () => { cart.Decrease(id); RenderCart(); }));
        var count = UiTheme.Text(line.Count.ToString(), 13, UiTheme.TextHi, HorizontalAlignment.Center);
        count.CustomMinimumSize = new Vector2(StepperCountWidth, 0);
        count.VerticalAlignment = VerticalAlignment.Center;
        controls.AddChild(count);
        controls.AddChild(StepButton("+", "One more", cart.CanIncrease(id), () => { cart.Increase(id); RenderCart(); }));
        controls.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        controls.AddChild(PowerUpStoreUi.Price(line.Total, 13));

        var remove = UiTheme.IconButton(UiIcons.Get("system/close"), "Remove from the cart");
        remove.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        remove.Pressed += () => { cart.Remove(id); RenderCart(); };
        hb.AddChild(remove);
        return row;
    }

    private Button StepButton(string glyph, string tooltip, bool enabled, System.Action step)
    {
        var button = UiTheme.IconButton(glyph, tooltip);
        var side = Platform.Pick(StepperButtonSide, StepperButtonSide * 1.5f);
        button.CustomMinimumSize = new Vector2(side, side);
        button.AddThemeFontSizeOverride("font_size", 14);
        var disabled = (StyleBoxFlat)button.GetThemeStylebox("normal").Duplicate();
        disabled.BgColor = new Color(disabled.BgColor, disabled.BgColor.A * 0.5f);
        disabled.BorderColor = new Color(disabled.BorderColor, disabled.BorderColor.A * 0.5f);
        button.AddThemeStyleboxOverride("disabled", disabled);
        button.AddThemeColorOverride("font_disabled_color", UiTheme.TextDim);
        button.Disabled = !enabled || model.Busy;
        button.Pressed += step;
        return button;
    }

    internal void SetCartStatus(string text, bool bad)
    {
        cartStatus.Text = text;
        cartStatus.Visible = text.Length > 0;
        cartStatus.AddThemeColorOverride("font_color", bad ? UiTheme.Bad : UiTheme.Good);
    }
}
