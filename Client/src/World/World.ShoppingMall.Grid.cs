using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const string PusFeaturedName = "Featured";
    private const int PusCategoryWidth = 228;
    private const int PusCategoryFontSize = 13;
    private const int PusCategoryIconSize = 14;
    private const float PusCategoryRowHeight = 40f;
    private const float PusCategoryTouchRowHeight = 54f;
    private const float PusCardWidth = 128f;
    private const float PusCardHeight = 168f;
    private const float PusCardGap = 8f;
    private const float PusCardIconSide = 64f;
    private const int PusCardSideMargin = 8;
    private const int PusCardTopMargin = 26;
    private const int PusCardBottomMargin = 8;
    private const int PusCardNameFontSize = 13;
    private const int PusCardNameLines = 2;
    private const int PusCardPriceFontSize = 14;
    private const int PusChipFontSize = 11;
    private const int PusChipInset = 5;
    private const float PusBadgeSide = 26f;
    private const int PusTitleFontSize = 17;
    private const float PusSortWidth = 172f;
    private const float PusSearchWidth = 240f;
    private const float PusSearchHeight = 32f;
    private const int PusSearchFontSize = 13;

    private static readonly Color PusTimerChipColor = new("f2c94c");
    private static readonly Color PusFeaturedFill = new(0.16f, 0.13f, 0.08f, 0.92f);

    private sealed class PusCardView
    {
        public required PowerUpStoreEntry Entry;
        public required PanelContainer Card;
        public required PanelContainer CartChip;
        public required Label CartCount;
    }

    private VBoxContainer _pusCategoryList = null!;
    private Label _pusGridTitle = null!;
    private LineEdit _pusSearch = null!;
    private OptionButton _pusSortPick = null!;
    private ScrollContainer _pusGridScroll = null!;
    private GridContainer _pusGrid = null!;
    private Label _pusGridEmpty = null!;
    private int _pusCategory = PowerUpStoreCatalog.FeaturedCategory;
    private PowerUpStoreSort _pusSort = PowerUpStoreCatalog.DefaultSort;
    private readonly Dictionary<int, PusCardView> _pusCards = new();
    private readonly List<(Label Label, DateTime EndsAt)> _pusTimerLabels = new();
    private int _pusHoveredCard = -1;

    private static PanelContainer PusWell()
    {
        var well = new PanelContainer();
        var style = UiTheme.Inset();
        style.BgColor = PusWellFill;
        well.AddThemeStyleboxOverride("panel", style);
        return well;
    }

    private Control BuildPusCategoryColumn()
    {
        var well = PusWell();
        well.CustomMinimumSize = new Vector2(PusCategoryWidth, 0);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        UiTheme.ThinScrollbar(scroll.GetVScrollBar());
        well.AddChild(scroll);
        _pusCategoryList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _pusCategoryList.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_pusCategoryList);
        return well;
    }

    private Control BuildPusGridColumn()
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", 8);

        var title = new HBoxContainer();
        title.AddThemeConstantOverride("separation", 8);
        column.AddChild(title);
        _pusGridTitle = UiTheme.Text("", PusTitleFontSize, UiTheme.GoldBright);
        _pusGridTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _pusGridTitle.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _pusGridTitle.ClipText = true;
        _pusGridTitle.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        title.AddChild(_pusGridTitle);
        title.AddChild(BuildPusCashRow());

        var tools = new HBoxContainer();
        tools.AddThemeConstantOverride("separation", 8);
        column.AddChild(tools);
        tools.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var search = PusSearchField("Search items", PusSearchFontSize, PusSearchHeight, out _pusSearch);
        search.CustomMinimumSize = new Vector2(PusSearchWidth, PusSearchHeight);
        _pusSearch.TextChanged += _ =>
        {
            RenderPusCategories();
            RenderPusGrid();
        };
        tools.AddChild(search);
        _pusSortPick = UiTheme.Dropdown(PowerUpStoreCatalog.Sorts.Select(PowerUpStoreCatalog.Label).ToList());
        _pusSortPick.CustomMinimumSize = new Vector2(PusSortWidth, PusSearchHeight);
        _pusSortPick.TooltipText = "Sort the items";
        _pusSortPick.Selected = PowerUpStoreCatalog.Sorts.ToList().IndexOf(_pusSort);
        _pusSortPick.ItemSelected += index =>
        {
            _pusSort = PowerUpStoreCatalog.Sorts[(int)index];
            RenderPusGrid();
        };
        tools.AddChild(_pusSortPick);

        _pusGridScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        UiTheme.ThinScrollbar(_pusGridScroll.GetVScrollBar());
        _pusGridScroll.Resized += FitPusGridColumns;
        column.AddChild(_pusGridScroll);
        var badgeRoom = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        badgeRoom.AddThemeConstantOverride("margin_top", (int)(PusBadgeSide / 2) + 2);
        _pusGridScroll.AddChild(badgeRoom);
        _pusGrid = new GridContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _pusGrid.AddThemeConstantOverride("h_separation", (int)PusCardGap);
        _pusGrid.AddThemeConstantOverride("v_separation", (int)(PusCardGap + PusBadgeSide / 2));
        badgeRoom.AddChild(_pusGrid);

        _pusGridEmpty = UiTheme.Text("", 14, UiTheme.TextLo, HorizontalAlignment.Center);
        _pusGridEmpty.VerticalAlignment = VerticalAlignment.Center;
        _pusGridEmpty.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _pusGridEmpty.Visible = false;
        column.AddChild(_pusGridEmpty);
        return column;
    }

    private static PanelContainer PusSearchField(string placeholder, int fontSize, float height, out LineEdit edit)
    {
        var box = new StyleBoxFlat { BgColor = new Color(0.04f, 0.03f, 0.02f, 0.9f), BorderColor = new Color(UiTheme.Edge, 0.7f) };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(4);
        box.ContentMarginLeft = 10;
        box.ContentMarginRight = 4;
        var focus = (StyleBoxFlat)box.Duplicate();
        focus.BorderColor = new Color(UiTheme.Gold, 0.9f);

        var field = new PanelContainer { CustomMinimumSize = new Vector2(0, height) };
        field.AddThemeStyleboxOverride("panel", box);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        field.AddChild(row);
        row.AddChild(UiIcons.Image("system/search", new Vector2(fontSize + 2, fontSize + 2), UiTheme.TextLo));
        var input = new LineEdit
        {
            PlaceholderText = placeholder,
            ClearButtonEnabled = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        foreach (var state in new[] { "normal", "focus", "read_only" })
            input.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        input.AddThemeFontSizeOverride("font_size", fontSize);
        input.AddThemeColorOverride("font_color", UiTheme.TextHi);
        input.AddThemeColorOverride("font_placeholder_color", new Color(UiTheme.TextLo, 0.6f));
        input.AddThemeColorOverride("caret_color", UiTheme.Gold);
        input.FocusEntered += () => field.AddThemeStyleboxOverride("panel", focus);
        input.FocusExited += () => field.AddThemeStyleboxOverride("panel", box);
        row.AddChild(input);
        edit = input;
        return field;
    }

    private void FitPusGridColumns()
    {
        var bar = _pusGridScroll.GetVScrollBar();
        var width = _pusGridScroll.Size.X - (bar.Visible ? bar.Size.X : 0);
        var columns = PowerUpStoreLayout.Columns(width, PusCardWidth, PusCardGap);
        if (_pusGrid.Columns != columns) _pusGrid.Columns = columns;
    }

    private void RenderPusCategories()
    {
        ClearChildren(_pusCategoryList);
        var searching = _pusSearch.Text.Trim().Length > 0;
        if (_pusCatalog.Exists(e => e.Featured))
            _pusCategoryList.AddChild(PusCategoryButton(PowerUpStoreCatalog.FeaturedCategory, PusFeaturedName, searching));
        foreach (var category in _pusCategories)
            _pusCategoryList.AddChild(PusCategoryButton(category.Id, category.Name, searching));
    }

    private Button PusCategoryButton(int id, string name, bool searching)
    {
        var selected = id == _pusCategory && !searching;
        var button = new Button
        {
            Text = name,
            Alignment = HorizontalAlignment.Left,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, Platform.Pick(PusCategoryRowHeight, PusCategoryTouchRowHeight)),
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        if (PowerUpStoreCatalog.HasDiscount(_pusCatalog, id))
        {
            button.Icon = UiIcons.Get("system/clock");
            button.IconAlignment = HorizontalAlignment.Right;
            button.TooltipText = "Items on sale";
            button.AddThemeConstantOverride("icon_max_width", PusCategoryIconSize);
            foreach (var state in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color" })
                button.AddThemeColorOverride(state, PusTimerChipColor);
        }
        button.AddThemeFontSizeOverride("font_size", PusCategoryFontSize);
        button.AddThemeColorOverride("font_color", selected ? UiTheme.GoldBright : UiTheme.TextLo);
        button.AddThemeColorOverride("font_hover_color", UiTheme.GoldBright);
        button.AddThemeColorOverride("font_pressed_color", UiTheme.GoldBright);
        button.AddThemeStyleboxOverride("normal", UiTheme.ListRow(selected));
        button.AddThemeStyleboxOverride("hover", UiTheme.ListRow(true));
        button.AddThemeStyleboxOverride("pressed", UiTheme.ListRow(true));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.Pressed += () =>
        {
            _pusCategory = id;
            _pusSearch.Text = "";
            Callable.From(() =>
            {
                RenderPusCategories();
                RenderPusGrid();
            }).CallDeferred();
        };
        return button;
    }

    private void RenderPusGrid()
    {
        HideItemTooltip();
        ClearChildren(_pusGrid);
        _pusCards.Clear();
        _pusTimerLabels.Clear();
        _pusHoveredCard = -1;
        var search = _pusSearch.Text.Trim();
        var category = _pusCategories.FirstOrDefault(c => c.Id == _pusCategory);
        var featured = _pusCategory == PowerUpStoreCatalog.FeaturedCategory;
        _pusGridTitle.Text = search.Length > 0 ? $"Results for \"{search}\""
            : featured ? PusFeaturedName
            : category.Name ?? "Power-Up Store";

        var shown = PowerUpStoreCatalog.View(_pusCatalog, _pusCategory, search, _pusSort);
        foreach (var entry in shown)
            _pusGrid.AddChild(BuildPusCard(entry));
        RefreshPusCardStates();

        string empty = _pusStoreError.Length > 0 ? _pusStoreError
            : !_pusLoaded ? "Loading the store…"
            : shown.Count > 0 ? ""
            : search.Length > 0 ? $"No items match \"{search}\"."
            : "Nothing is sold in this category yet.";
        _pusGridEmpty.Text = empty;
        _pusGridEmpty.Visible = empty.Length > 0;
        _pusGridScroll.Visible = empty.Length == 0;
        if (_pusGridEmpty.Visible) _pusGridEmpty.AddThemeColorOverride("font_color", _pusStoreError.Length > 0 ? UiTheme.Bad : UiTheme.TextLo);
        Callable.From(FitPusGridColumns).CallDeferred();
    }

    private static StyleBoxFlat PusCardStyle(bool featured, bool active)
    {
        var style = new StyleBoxFlat
        {
            BgColor = featured ? PusFeaturedFill : active ? UiTheme.RowHover : PusCardFill,
            BorderColor = active ? UiTheme.GoldVivid : featured ? new Color(UiTheme.Gold, 0.45f) : new Color(UiTheme.EdgeSoft, 0.45f),
        };
        style.SetBorderWidthAll(active ? 2 : 1);
        style.SetCornerRadiusAll(4);
        style.ContentMarginLeft = style.ContentMarginRight = PusCardSideMargin;
        style.ContentMarginTop = PusCardTopMargin;
        style.ContentMarginBottom = PusCardBottomMargin;
        return style;
    }

    private Control BuildPusCard(PowerUpStoreEntry entry)
    {
        var card = new PanelContainer
        {
            CustomMinimumSize = new Vector2(PusCardWidth, PusCardHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        card.MouseEntered += () =>
        {
            _pusHoveredCard = entry.Id;
            RefreshPusCardStates();
        };
        card.MouseExited += () =>
        {
            if (_pusHoveredCard != entry.Id || card.GetGlobalRect().HasPoint(card.GetGlobalMousePosition())) return;
            _pusHoveredCard = -1;
            RefreshPusCardStates();
        };
        card.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            card.AcceptEvent();
            Callable.From(() => OpenPusDetails(entry)).CallDeferred();
        };

        var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 6);
        card.AddChild(box);

        var frame = new PanelContainer
        {
            CustomMinimumSize = new Vector2(PusCardIconSide, PusCardIconSide),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        frame.AddThemeStyleboxOverride("panel", UiTheme.Slot());
        frame.MouseEntered += () => ShowItemTooltip(-1, TooltipItem(entry.ItemId));
        frame.MouseExited += HideItemTooltip;
        frame.AddChild(new TextureRect
        {
            Texture = ItemData.Icon(entry.ItemId),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        box.AddChild(frame);

        var name = UiTheme.Text(entry.Name, PusCardNameFontSize, UiTheme.TextHi, HorizontalAlignment.Center);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        name.MaxLinesVisible = PusCardNameLines;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.CustomMinimumSize = new Vector2(0, (PusCardNameFontSize + 4) * PusCardNameLines);
        name.VerticalAlignment = VerticalAlignment.Center;
        box.AddChild(name);

        var price = PusPriceTag(entry, PusCardPriceFontSize);
        price.Alignment = BoxContainer.AlignmentMode.Center;
        box.AddChild(price);

        var overlay = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        card.AddChild(overlay);
        if (entry.Featured) overlay.AddChild(PusFeaturedBadge());

        var cartChip = PusChip("system/cart", "", UiTheme.GoldVivid, UiTheme.Ink, out var cartCount);
        cartChip.TooltipText = "In your cart";
        cartChip.Position = new Vector2(PusChipInset - PusCardSideMargin, PusChipInset - PusCardTopMargin);
        overlay.AddChild(cartChip);

        if (entry.Discounted && entry.DiscountEndsAt is { } endsAt)
        {
            var timer = PusChip("system/clock", PowerUpStoreTimer.Label(endsAt - DateTime.UtcNow), PusTimerChipColor, UiTheme.Ink, out var timerLabel);
            timer.TooltipText = "Time left at this price";
            timer.AnchorLeft = timer.AnchorRight = 1f;
            timer.OffsetLeft = timer.OffsetRight = PusCardSideMargin - PusChipInset;
            timer.OffsetTop = timer.OffsetBottom = PusChipInset - PusCardTopMargin;
            timer.GrowHorizontal = Control.GrowDirection.Begin;
            overlay.AddChild(timer);
            _pusTimerLabels.Add((timerLabel, endsAt));
        }

        _pusCards[entry.Id] = new PusCardView { Entry = entry, Card = card, CartChip = cartChip, CartCount = cartCount };
        return card;
    }

    private static PanelContainer PusChip(string icon, string text, Color fill, Color ink, out Label label)
    {
        var chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        var style = new StyleBoxFlat { BgColor = fill };
        style.SetCornerRadiusAll(9);
        style.ContentMarginLeft = style.ContentMarginRight = 6;
        style.ContentMarginTop = style.ContentMarginBottom = 1;
        chip.AddThemeStyleboxOverride("panel", style);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 3);
        chip.AddChild(row);
        row.AddChild(UiIcons.Image(icon, new Vector2(PusChipFontSize, PusChipFontSize), ink));
        label = UiTheme.Text(text, PusChipFontSize, ink);
        label.AddThemeConstantOverride("font_embolden", 1);
        row.AddChild(label);
        return chip;
    }

    private static Control PusFeaturedBadge()
    {
        var badge = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Pass,
            TooltipText = PusFeaturedName,
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            OffsetLeft = -PusBadgeSide / 2,
            OffsetRight = PusBadgeSide / 2,
            OffsetTop = -PusCardTopMargin - PusBadgeSide / 2,
            OffsetBottom = -PusCardTopMargin + PusBadgeSide / 2,
        };
        var style = new StyleBoxFlat { BgColor = UiTheme.GlassDeep, BorderColor = PusTimerChipColor };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll((int)(PusBadgeSide / 2));
        style.SetContentMarginAll(4);
        badge.AddThemeStyleboxOverride("panel", style);
        badge.AddChild(UiIcons.Image("system/star", new Vector2(PusBadgeSide - 10, PusBadgeSide - 10), PusTimerChipColor));
        return badge;
    }

    private void RefreshPusCardStates()
    {
        foreach (var view in _pusCards.Values)
        {
            if (!IsInstanceValid(view.Card)) continue;
            var inCart = _pusCart.Lines.FirstOrDefault(l => l.Entry.Id == view.Entry.Id)?.Count ?? 0;
            view.CartChip.Visible = inCart > 0;
            view.CartCount.Text = $"×{inCart}";
            var active = inCart > 0 || (_pusHoveredCard == view.Entry.Id && _pusModal == PusModal.None);
            view.Card.AddThemeStyleboxOverride("panel", PusCardStyle(view.Entry.Featured, active));
        }
    }
}
