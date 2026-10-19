using Godot;

namespace LibreKO.Features.Windows;

internal sealed class PowerUpStoreDetailsView
{
    private const float DetailsWidth = 420f;
    private const float DetailsIconSide = 90f;
    private const int DetailsNameFontSize = 21;
    private const int DetailsPriceFontSize = 22;

    private readonly IItemTooltips tooltips;
    private readonly PowerUpStoreModel model;
    private readonly SelfSession self;

    public PowerUpStoreDetailsView(IItemTooltips tooltips, PowerUpStoreModel model, SelfSession self)
    {
        this.tooltips = tooltips;
        this.model = model;
        this.self = self;
    }

    internal event Action? CloseRequested;

    internal event Action? AddRequested;

    internal event Action? BuyRequested;

    private PanelContainer detailsBox = null!;
    private TextureRect detailsIcon = null!;
    private Label detailsName = null!;
    private Label detailsCategory = null!;
    private Label detailsDescription = null!;
    private HBoxContainer detailsPrice = null!;
    private Button detailsAdd = null!;
    private Button detailsBuy = null!;
    private Label detailsStatus = null!;
    private bool shown;

    internal PowerUpStoreEntry? Entry { get; private set; }

    internal (Label Label, DateTime EndsAt)? Timer { get; private set; }

    internal Control BuildDetailsBox()
    {
        detailsBox = PowerUpStoreUi.ModalBox(DetailsWidth, out var body);
        PowerUpStoreUi.ModalHeader(body, "", null, () => CloseRequested?.Invoke());

        var frame = new PanelContainer
        {
            CustomMinimumSize = new Vector2(DetailsIconSide, DetailsIconSide),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        frame.AddThemeStyleboxOverride("panel", UiTheme.Slot(UiTheme.Gold));
        frame.MouseEntered += () =>
        {
            if (Entry is { } entry) tooltips.Show(-1, UiKit.TooltipItem(entry.ItemId));
        };
        frame.MouseExited += tooltips.Hide;
        detailsIcon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        frame.AddChild(detailsIcon);
        body.AddChild(frame);

        detailsName = UiTheme.Text("", DetailsNameFontSize, UiTheme.GoldBright, HorizontalAlignment.Center);
        detailsName.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(detailsName);
        detailsCategory = UiTheme.Text("", 12, UiTheme.TextDim, HorizontalAlignment.Center);
        body.AddChild(detailsCategory);

        detailsDescription = UiTheme.Text("", 14, UiTheme.TextLo, HorizontalAlignment.Center);
        detailsDescription.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(detailsDescription);

        detailsPrice = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        detailsPrice.AddThemeConstantOverride("separation", 10);
        body.AddChild(detailsPrice);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        body.AddChild(buttons);
        var height = Platform.Pick(PowerUpStoreUi.ModalButtonHeight, PowerUpStoreUi.ModalTouchButtonHeight);
        detailsAdd = UiTheme.SmallButton("Add to cart", "Put one in the cart");
        detailsAdd.CustomMinimumSize = new Vector2(0, height);
        detailsAdd.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        detailsAdd.AddThemeFontSizeOverride("font_size", 14);
        detailsAdd.Pressed += () => AddRequested?.Invoke();
        buttons.AddChild(detailsAdd);
        detailsBuy = UiTheme.ActionButton("Buy now", "Buy one for yourself; it arrives in your mailbox");
        detailsBuy.CustomMinimumSize = new Vector2(0, height);
        detailsBuy.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        detailsBuy.AddThemeFontSizeOverride("font_size", 14);
        detailsBuy.Pressed += () => BuyRequested?.Invoke();
        buttons.AddChild(detailsBuy);

        detailsStatus = UiTheme.Text("", 12, UiTheme.Bad, HorizontalAlignment.Center);
        detailsStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        detailsStatus.Visible = false;
        body.AddChild(detailsStatus);
        return detailsBox;
    }

    internal void SetShown(bool value)
    {
        shown = value;
        detailsBox.Visible = value;
        if (!value) Timer = null;
    }

    internal void Fill(PowerUpStoreEntry entry)
    {
        Entry = entry;
        SetDetailsStatus("");
        detailsIcon.Texture = ItemIcons.Get(entry.ItemId);
        detailsName.Text = entry.Name;
        var categoryName = model.CategoryName(entry.Category);
        detailsCategory.Text = entry.Featured && categoryName.Length > 0 ? $"{PowerUpStoreUi.FeaturedName} · {categoryName}" : categoryName;
        detailsDescription.Text = entry.Description;
        detailsDescription.Visible = entry.Description.Length > 0;
    }

    internal void RenderDetails()
    {
        if (!GodotObject.IsInstanceValid(detailsBox) || !shown || Entry is not { } entry) return;
        var affordable = entry.Price <= self.Sheet.KnightCash;
        UiKit.ClearChildren(detailsPrice);
        detailsPrice.AddChild(PowerUpStoreUi.PriceTag(entry, DetailsPriceFontSize, affordable ? UiTheme.Premium : UiTheme.Bad));
        Timer = null;
        if (entry.Discounted && entry.DiscountEndsAt is { } endsAt)
        {
            var chip = PowerUpStoreUi.Chip("system/clock", PowerUpStoreTimer.Label(endsAt - DateTime.UtcNow), PowerUpStoreUi.TimerChipColor, UiTheme.Ink, out var label);
            chip.TooltipText = "Time left at this price";
            chip.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            detailsPrice.AddChild(chip);
            Timer = (label, endsAt);
        }
        detailsBuy.Text = model.Pending == PowerUpStorePurchase.BuyNow ? "Buying…" : affordable ? "Buy now" : "Not enough cash";
        detailsBuy.Disabled = !affordable || model.Busy;
        detailsAdd.Disabled = model.Busy;
    }

    internal void SetDetailsStatus(string text)
    {
        detailsStatus.Text = text;
        detailsStatus.Visible = text.Length > 0;
    }
}
