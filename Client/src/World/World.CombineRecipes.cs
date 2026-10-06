using System;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const int CombineBookWidth = 760;
    private const float CombineBookHeight = 400f;
    private const float CombineBookListWidth = 210f;
    private const float CombineBookRecipeWidth = 250f;
    private const float CombineBookDetailWidth = 284f;
    private const int CombineBookIconSize = 28;
    private const float CombineBookSlotSize = 40f;
    private static readonly Color CombineUncraftable = new("6f9cff");

    private CanvasLayer _combineBookLayer = null!;
    private HudWindow _combineBookPanel = null!;
    private VBoxContainer _combineBookCategories = null!, _combineBookRecipes = null!, _combineBookDetail = null!;
    private Label _combineBookRecipeTitle = null!;
    private bool _combineBookShown;
    private int _combineBookCategory = -1, _combineBookRecipe = -1;

    private void BuildCombineRecipeBook()
    {
        _combineBookLayer = new CanvasLayer { Layer = 76 };
        AddChild(_combineBookLayer);

        _combineBookPanel = new HudWindow("combinerecipes", "Book of Transformation", bodyMinWidth: CombineBookWidth) { Visible = false };
        _combineBookPanel.Closed += CloseCombineRecipeBook;
        _combineBookLayer.AddChild(_combineBookPanel);

        var columns = new HBoxContainer { CustomMinimumSize = new Vector2(0, CombineBookHeight) };
        columns.AddThemeConstantOverride("separation", 8);
        _combineBookPanel.Body.AddChild(columns);

        _combineBookCategories = CombineBookColumn(columns, CombineText(ItemCombine.ClassesText, "Compounding Item Class"), CombineBookListWidth, out _);
        _combineBookRecipes = CombineBookColumn(columns, "", CombineBookRecipeWidth, out _combineBookRecipeTitle);
        _combineBookDetail = CombineBookColumn(columns, "", CombineBookDetailWidth, out var detailHeading);
        detailHeading.Visible = false;
    }

    private static VBoxContainer CombineBookColumn(HBoxContainer columns, string title, float width, out Label heading)
    {
        var section = UiTheme.Section();
        section.CustomMinimumSize = new Vector2(width, 0);
        columns.AddChild(section);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        section.AddChild(box);
        heading = UiTheme.SectionTitle(title);
        box.AddChild(heading);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        UiTheme.ThinScrollbar(scroll.GetVScrollBar());
        box.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(list);
        return list;
    }

    private void OpenCombineRecipeBook()
    {
        if (_combineBookShown) return;
        EnsureCombineBook();
        _combineBookShown = true;
        _combineBookPanel.Visible = true;
        _combineBookCategory = -1;
        _combineBookRecipe = -1;
        RenderCombineBook();
    }

    private void CloseCombineRecipeBook()
    {
        if (!_combineBookShown) return;
        _combineBookShown = false;
        _combineBookPanel.Visible = false;
        HideItemTooltip();
    }

    private void RenderCombineBook()
    {
        foreach (var child in _combineBookCategories.GetChildren()) child.QueueFree();
        foreach (var category in _combineBook.Shown)
        {
            int key = category.Key;
            _combineBookCategories.AddChild(CombineBookRow(category.Name, null, key == _combineBookCategory, UiTheme.TextHi,
                () => SelectCombineCategory(key)));
        }
        RenderCombineRecipes();
        RenderCombineDetail();
    }

    private void SelectCombineCategory(int key)
    {
        _combineBookCategory = key;
        _combineBookRecipe = -1;
        RenderCombineBook();
    }

    private void SelectCombineRecipe(int id)
    {
        _combineBookRecipe = id;
        RenderCombineRecipes();
        RenderCombineDetail();
    }

    private void RenderCombineRecipes()
    {
        foreach (var child in _combineBookRecipes.GetChildren()) child.QueueFree();
        var category = Array.Find(_combineBook.Categories, c => c.Key == _combineBookCategory);
        _combineBookRecipeTitle.Text = category == null ? CombineText(ItemCombine.ItemListText, "Item list")
            : $"{category.Name} {CombineText(ItemCombine.ItemListText, "Item list")}";
        if (category == null) return;
        foreach (var recipe in _combineBook.Listed(category.Key))
        {
            if (ItemData.Get(recipe.Result) == null) continue;
            int id = recipe.Id;
            bool craftable = ItemCombine.Craftable(recipe, item => ItemData.Get(item) != null);
            _combineBookRecipes.AddChild(CombineBookRow(
                craftable ? recipe.Name : CombineText(ItemCombine.UncraftableText, "Item cannot be crafted"),
                ItemData.Icon(recipe.Result), id == _combineBookRecipe, craftable ? UiTheme.TextHi : CombineUncraftable,
                () => SelectCombineRecipe(id)));
        }
    }

    private void RenderCombineDetail()
    {
        foreach (var child in _combineBookDetail.GetChildren()) child.QueueFree();
        if (_combineBook.Row(_combineBookRecipe) is not { } recipe)
        {
            var intro = UiTheme.Text(ItemCombine.Paragraphs(CombineText(ItemCombine.IntroText, "")), 12, UiTheme.TextLo);
            intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            intro.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _combineBookDetail.AddChild(intro);
            return;
        }

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 10);
        _combineBookDetail.AddChild(head);
        head.AddChild(CombineBookSlot(recipe.Result, 1));
        var name = UiTheme.Text(recipe.Name, 15, ItemGrade.Tint(recipe.Result));
        name.VerticalAlignment = VerticalAlignment.Center;
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        head.AddChild(name);

        foreach (var material in recipe.Materials)
        {
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 10);
            _combineBookDetail.AddChild(line);
            line.AddChild(CombineBookSlot(material.Item, material.Count));
            var label = UiTheme.Text(ItemData.Get(material.Item) == null
                ? CombineText(ItemCombine.UncraftableText, "Item cannot be crafted")
                : ItemData.DisplayName(material.Item), 13, UiTheme.TextHi);
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            line.AddChild(label);
            var count = UiTheme.Text($"x{material.Count}", 13, UiTheme.Gold);
            count.VerticalAlignment = VerticalAlignment.Center;
            line.AddChild(count);
        }
    }

    private ItemSlotView CombineBookSlot(int itemId, int count)
    {
        var slot = new ItemSlotView(CombineBookSlotSize);
        if (ItemData.Get(itemId) != null) slot.Set(TooltipItem(itemId, count));
        slot.Hovered += s => { if (!s.Item.IsEmpty) ShowItemTooltip(-1, s.Item); };
        slot.Unhovered += _ => HideItemTooltip();
        return slot;
    }

    private static Button CombineBookRow(string text, Texture2D? icon, bool selected, Color colour, Action pressed)
    {
        var button = new Button
        {
            Text = text,
            Icon = icon,
            Alignment = HorizontalAlignment.Left,
            FocusMode = Control.FocusModeEnum.None,
            ClipText = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        button.AddThemeConstantOverride("icon_max_width", CombineBookIconSize);
        button.AddThemeFontSizeOverride("font_size", 13);
        button.AddThemeColorOverride("font_color", colour);
        button.AddThemeColorOverride("font_hover_color", UiTheme.GoldBright);
        button.AddThemeStyleboxOverride("normal", UiTheme.ListRow(selected));
        button.AddThemeStyleboxOverride("hover", UiTheme.ListRow(true));
        button.AddThemeStyleboxOverride("pressed", UiTheme.ListRow(true));
        button.Pressed += pressed;
        return button;
    }
}
