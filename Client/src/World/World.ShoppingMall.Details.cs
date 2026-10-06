using System;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const float PusModalDim = 0.62f;
    private const float PusDetailsWidth = 420f;
    private const float PusDetailsIconSide = 90f;
    private const int PusDetailsNameFontSize = 21;
    private const int PusDetailsPriceFontSize = 22;
    private const float PusDetailsButtonHeight = 42f;
    private const float PusDetailsTouchButtonHeight = 56f;

    private enum PusModal { None, Details, Gift }

    private Control _pusOverlay = null!;
    private PusModal _pusModal;
    private PanelContainer _pusDetailsBox = null!;
    private TextureRect _pusDetailsIcon = null!;
    private Label _pusDetailsName = null!;
    private Label _pusDetailsCategory = null!;
    private Label _pusDetailsDescription = null!;
    private HBoxContainer _pusDetailsPrice = null!;
    private Button _pusDetailsAdd = null!;
    private Button _pusDetailsBuy = null!;
    private Label _pusDetailsStatus = null!;
    private PowerUpStoreEntry? _pusDetailsEntry;
    private (Label Label, DateTime EndsAt)? _pusDetailsTimer;

    private void BuildPusModal()
    {
        _pusOverlay = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _pusWindow.AddChild(_pusOverlay);

        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, PusModalDim), MouseFilter = Control.MouseFilterEnum.Stop };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        dim.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            dim.AcceptEvent();
            Callable.From(ClosePusModal).CallDeferred();
        };
        _pusOverlay.AddChild(dim);

        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _pusOverlay.AddChild(centre);
        centre.AddChild(BuildPusDetailsBox());
        centre.AddChild(BuildPusGiftBox());
    }

    private static PanelContainer PusModalBox(float width, out VBoxContainer body)
    {
        var box = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        box.AddThemeStyleboxOverride("panel", UiTheme.WindowPanel(radius: 4));
        var margin = new MarginContainer();
        UiTheme.Margins(margin, 18, 14, 18, 16);
        box.AddChild(margin);
        body = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0) };
        body.AddThemeConstantOverride("separation", 10);
        margin.AddChild(body);
        return box;
    }

    private static HBoxContainer PusModalHeader(VBoxContainer body, string title, Texture2D? icon, Action close)
    {
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        body.AddChild(head);
        if (icon != null)
        {
            head.AddChild(new TextureRect
            {
                Texture = icon,
                CustomMinimumSize = new Vector2(20, 20),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SelfModulate = UiTheme.GoldVivid,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }
        var label = UiTheme.Text(title, 15, UiTheme.Gold);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(label);
        var closeButton = UiTheme.IconButton(UiIcons.Get("system/close"), "Close");
        closeButton.CustomMinimumSize = Platform.Pick(new Vector2(26, 26), new Vector2(46, 46));
        closeButton.Pressed += close;
        head.AddChild(closeButton);
        return head;
    }

    private Control BuildPusDetailsBox()
    {
        _pusDetailsBox = PusModalBox(PusDetailsWidth, out var body);
        PusModalHeader(body, "", null, ClosePusModal);

        var frame = new PanelContainer
        {
            CustomMinimumSize = new Vector2(PusDetailsIconSide, PusDetailsIconSide),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        frame.AddThemeStyleboxOverride("panel", UiTheme.Slot(UiTheme.Gold));
        frame.MouseEntered += () =>
        {
            if (_pusDetailsEntry is { } entry) ShowItemTooltip(-1, TooltipItem(entry.ItemId));
        };
        frame.MouseExited += HideItemTooltip;
        _pusDetailsIcon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        frame.AddChild(_pusDetailsIcon);
        body.AddChild(frame);

        _pusDetailsName = UiTheme.Text("", PusDetailsNameFontSize, UiTheme.GoldBright, HorizontalAlignment.Center);
        _pusDetailsName.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(_pusDetailsName);
        _pusDetailsCategory = UiTheme.Text("", 12, UiTheme.TextDim, HorizontalAlignment.Center);
        body.AddChild(_pusDetailsCategory);

        _pusDetailsDescription = UiTheme.Text("", 14, UiTheme.TextLo, HorizontalAlignment.Center);
        _pusDetailsDescription.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(_pusDetailsDescription);

        _pusDetailsPrice = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _pusDetailsPrice.AddThemeConstantOverride("separation", 10);
        body.AddChild(_pusDetailsPrice);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        body.AddChild(buttons);
        var height = Platform.Pick(PusDetailsButtonHeight, PusDetailsTouchButtonHeight);
        _pusDetailsAdd = UiTheme.SmallButton("Add to cart", "Put one in the cart");
        _pusDetailsAdd.CustomMinimumSize = new Vector2(0, height);
        _pusDetailsAdd.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _pusDetailsAdd.AddThemeFontSizeOverride("font_size", 14);
        _pusDetailsAdd.Pressed += AddPusDetailsToCart;
        buttons.AddChild(_pusDetailsAdd);
        _pusDetailsBuy = UiTheme.ActionButton("Buy now", "Buy one for yourself; it arrives in your mailbox");
        _pusDetailsBuy.CustomMinimumSize = new Vector2(0, height);
        _pusDetailsBuy.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _pusDetailsBuy.AddThemeFontSizeOverride("font_size", 14);
        _pusDetailsBuy.Pressed += BuyPusDetailsNow;
        buttons.AddChild(_pusDetailsBuy);

        _pusDetailsStatus = UiTheme.Text("", 12, UiTheme.Bad, HorizontalAlignment.Center);
        _pusDetailsStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _pusDetailsStatus.Visible = false;
        body.AddChild(_pusDetailsStatus);
        return _pusDetailsBox;
    }

    private void ShowPusModal(PusModal modal)
    {
        HideItemTooltip();
        _pusModal = modal;
        _pusHoveredCard = -1;
        _pusDetailsBox.Visible = modal == PusModal.Details;
        _pusGiftBox.Visible = modal == PusModal.Gift;
        _pusOverlay.Visible = modal != PusModal.None;
        if (modal != PusModal.Details) _pusDetailsTimer = null;
        RefreshPusCardStates();
    }

    private void ClosePusModal()
    {
        if (!IsInstanceValid(_pusOverlay)) return;
        if (_pusModal == PusModal.Details && _pusPending == PusPurchase.BuyNow) return;
        _pusGiftChecking = "";
        ShowPusModal(PusModal.None);
    }

    private void OpenPusDetails(PowerUpStoreEntry entry)
    {
        _pusDetailsEntry = entry;
        SetPusDetailsStatus("");
        _pusDetailsIcon.Texture = ItemData.Icon(entry.ItemId);
        _pusDetailsName.Text = entry.Name;
        var category = _pusCategories.Find(c => c.Id == entry.Category).Name ?? "";
        _pusDetailsCategory.Text = entry.Featured && category.Length > 0 ? $"{PusFeaturedName} · {category}" : category;
        _pusDetailsDescription.Text = entry.Description;
        _pusDetailsDescription.Visible = entry.Description.Length > 0;
        ShowPusModal(PusModal.Details);
        RenderPusDetails();
    }

    private void RenderPusDetails()
    {
        if (!IsInstanceValid(_pusDetailsBox) || _pusModal != PusModal.Details || _pusDetailsEntry is not { } entry) return;
        var affordable = entry.Price <= Sheet.KnightCash;
        ClearChildren(_pusDetailsPrice);
        _pusDetailsPrice.AddChild(PusPriceTag(entry, PusDetailsPriceFontSize, affordable ? UiTheme.Premium : UiTheme.Bad));
        _pusDetailsTimer = null;
        if (entry.Discounted && entry.DiscountEndsAt is { } endsAt)
        {
            var chip = PusChip("system/clock", PowerUpStoreTimer.Label(endsAt - DateTime.UtcNow), PusTimerChipColor, UiTheme.Ink, out var label);
            chip.TooltipText = "Time left at this price";
            chip.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            _pusDetailsPrice.AddChild(chip);
            _pusDetailsTimer = (label, endsAt);
        }
        _pusDetailsBuy.Text = _pusPending == PusPurchase.BuyNow ? "Buying…" : affordable ? "Buy now" : "Not enough cash";
        _pusDetailsBuy.Disabled = !affordable || _pusPending != PusPurchase.None;
        _pusDetailsAdd.Disabled = _pusPending != PusPurchase.None;
    }

    private void SetPusDetailsStatus(string text)
    {
        _pusDetailsStatus.Text = text;
        _pusDetailsStatus.Visible = text.Length > 0;
    }

    private void AddPusDetailsToCart()
    {
        if (_pusDetailsEntry is not { } entry) return;
        _pusCart.Add(entry);
        ClosePusModal();
        RenderPusCart();
    }

    private void BuyPusDetailsNow()
    {
        if (_pusDetailsEntry is not { } entry || _pusPending != PusPurchase.None || entry.Price > Sheet.KnightCash) return;
        var single = new PowerUpStoreCart();
        single.Add(entry);
        _pusPending = PusPurchase.BuyNow;
        SetPusDetailsStatus("");
        Net.I.SendPowerUpStorePurchase("", single.Lines);
        RenderPusDetails();
        RenderPusCart();
    }
}
