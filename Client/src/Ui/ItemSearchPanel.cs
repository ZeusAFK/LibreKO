using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class ItemSearchPanel : VBoxContainer
{
    private readonly bool tradeableOnly;
    private readonly bool showQuantity;
    private readonly string actionText;
    private readonly Action<ItemSearchHit, int> onAction;
    private readonly Action<int>? showTooltip;
    private readonly Action? hideTooltip;

    private readonly List<string> names = new();
    private readonly List<List<ItemSearchHit>> hits = new();
    private readonly List<string> groups = new();
    private readonly List<int> levels = new();
    private readonly Dictionary<string, Button> tabButtons = new();
    private string tab = ItemSearch.TabBasic;

    private LineEdit query = null!;
    private OptionButton pick = null!, groupPicker = null!, levelPicker = null!;
    private VBoxContainer results = null!;
    private Label summary = null!;

    public ItemSearchPanel(
        bool tradeableOnly,
        string actionText,
        Action<ItemSearchHit, int> onAction,
        Action<int>? showTooltip = null,
        Action? hideTooltip = null,
        Vector2 resultsSize = default,
        bool showQuantity = true)
    {
        this.tradeableOnly = tradeableOnly;
        this.showQuantity = showQuantity;
        this.actionText = actionText;
        this.onAction = onAction;
        this.showTooltip = showTooltip;
        this.hideTooltip = hideTooltip;

        AddThemeConstantOverride("separation", 6);
        if (resultsSize == default) resultsSize = new Vector2(470, 320);
        CustomMinimumSize = new Vector2(resultsSize.X, 0);

        AddChild(UiTheme.SectionTitle("Item search", UiIcons.Get("system/bag")));

        var findRow = new HBoxContainer();
        findRow.AddThemeConstantOverride("separation", 5);
        AddChild(findRow);
        query = new LineEdit
        {
            PlaceholderText = "item name",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        query.TextSubmitted += _ => Run();
        findRow.AddChild(query);
        var search = new Button { Text = "Search", FocusMode = FocusModeEnum.None };
        search.Pressed += Run;
        findRow.AddChild(search);

        var tabBar = new HBoxContainer();
        tabBar.AddThemeConstantOverride("separation", 4);
        AddChild(tabBar);
        foreach (string label in ItemSearch.Tabs)
        {
            string key = label;
            var button = new Button { Text = label, ToggleMode = true, FocusMode = FocusModeEnum.None };
            button.AddThemeFontSizeOverride("font_size", 12);
            button.Pressed += () => SelectTab(key);
            tabButtons[key] = button;
            tabBar.AddChild(button);
        }
        tabButtons[tab].ButtonPressed = true;

        var pickRow = new HBoxContainer();
        pickRow.AddThemeConstantOverride("separation", 5);
        AddChild(pickRow);
        pick = UiTheme.Dropdown();
        pick.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        pick.ItemSelected += _ => OnNamePicked();
        pickRow.AddChild(pick);
        groupPicker = UiTheme.Dropdown();
        groupPicker.CustomMinimumSize = new Vector2(124, 0);
        groupPicker.ItemSelected += _ => OnGroupPicked();
        pickRow.AddChild(groupPicker);
        levelPicker = UiTheme.Dropdown();
        levelPicker.CustomMinimumSize = new Vector2(96, 0);
        levelPicker.ItemSelected += _ => Refresh();
        pickRow.AddChild(levelPicker);

        summary = UiTheme.Text("Type an item name and press Search.", 12, UiTheme.TextLo);
        AddChild(summary);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = resultsSize,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(scroll);
        results = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        results.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(results);
    }

    public void FocusQuery() => query.GrabFocus();

    public void Run()
    {
        ItemSearch.Match(query.Text.Trim(), tradeableOnly, names, hits);

        pick.Clear();
        foreach (string name in names) pick.AddItem(name);
        if (names.Count > 0) pick.Selected = 0;

        foreach (string label in ItemSearch.Tabs)
        {
            if (!TabHasHits(label)) continue;
            SetTab(label);
            break;
        }
        OnNamePicked();
    }

    public void Refresh()
    {
        foreach (Node child in results.GetChildren()) child.QueueFree();
        hideTooltip?.Invoke();

        int levelIndex = levelPicker.Selected - 1;
        int wantedPlus = levelIndex >= 0 && levelIndex < levels.Count ? levels[levelIndex] : -1;

        var shown = new List<ItemSearchHit>();
        foreach (var hit in GroupHits())
        {
            if (wantedPlus >= 0 && hit.Plus != wantedPlus) continue;
            shown.Add(hit);
        }

        shown.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        for (int i = shown.Count - 1; i > 0; i--)
            if (shown[i].Id == shown[i - 1].Id) shown.RemoveAt(i);

        int listed = Math.Min(shown.Count, ItemSearch.ResultCap);
        for (int i = 0; i < listed; i++)
            results.AddChild(BuildRow(shown[i]));

        summary.Text = names.Count == 0
            ? "No item matches that name."
            : shown.Count switch
            {
                0 => $"Nothing on the {tab} tab for this item.",
                _ when shown.Count > listed => $"{shown.Count:n0} variants — showing the first {listed}.",
                1 => "1 variant.",
                _ => $"{shown.Count:n0} variants.",
            };
    }

    public void SelectTab(string label)
    {
        SetTab(label);
        OnNamePicked();
    }

    public void SetQuery(string text) => query.Text = text;

    internal void RegisterFirstUiPreview()
    {
        foreach (var hit in GroupHits()) { onAction(hit, 1); return; }
    }

    public void SelectPlus(int plus)
    {
        for (int i = 0; i < levels.Count; i++)
        {
            if (levels[i] != plus) continue;
            levelPicker.Selected = i + 1;
            Refresh();
            return;
        }
    }

    private void SetTab(string label)
    {
        tab = label;
        foreach (var (key, button) in tabButtons) button.ButtonPressed = key == label;
    }

    private bool TabHasHits(string label)
    {
        foreach (var hit in PickedHits())
            if (ItemSearch.TabOf(hit) == label) return true;
        return false;
    }

    private void OnNamePicked()
    {
        groups.Clear();
        groupPicker.Clear();
        groupPicker.AddItem("All");

        var foundGroups = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hit in TabHits()) foundGroups.Add(hit.VariantLabel);
        foundGroups.Remove("");

        foreach (string group in foundGroups)
        {
            groups.Add(group);
            groupPicker.AddItem(group);
        }
        groupPicker.Selected = 0;
        groupPicker.Visible = groups.Count > 0;
        OnGroupPicked();
    }

    private void OnGroupPicked()
    {
        levels.Clear();
        levelPicker.Clear();
        levelPicker.AddItem("Any +");

        var foundLevels = new SortedSet<int>();
        foreach (var hit in GroupHits()) foundLevels.Add(hit.Plus);

        foreach (int level in foundLevels)
        {
            levels.Add(level);
            levelPicker.AddItem($"+{level}");
        }
        levelPicker.Selected = 0;
        Refresh();
    }

    private IEnumerable<ItemSearchHit> TabHits()
    {
        foreach (var hit in PickedHits())
            if (ItemSearch.TabOf(hit) == tab) yield return hit;
    }

    private IEnumerable<ItemSearchHit> GroupHits()
    {
        int index = groupPicker.Selected - 1;
        string wanted = index >= 0 && index < groups.Count ? groups[index] : "";
        foreach (var hit in TabHits())
            if (wanted.Length == 0
                || string.Equals(hit.VariantLabel, wanted, StringComparison.OrdinalIgnoreCase))
                yield return hit;
    }

    private IReadOnlyList<ItemSearchHit> PickedHits()
    {
        int picked = pick.Selected;
        return picked >= 0 && picked < hits.Count ? hits[picked] : Array.Empty<ItemSearchHit>();
    }

    private Control BuildRow(ItemSearchHit hit)
    {
        int itemId = hit.Id;
        var row = UiTheme.RowPanel();
        row.CustomMinimumSize = new Vector2(0, 34);
        row.MouseEntered += () => showTooltip?.Invoke(itemId);
        row.MouseExited += () => hideTooltip?.Invoke();

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        row.AddChild(line);

        line.AddChild(new TextureRect
        {
            Texture = ItemIcons.Get(itemId),
            CustomMinimumSize = new Vector2(28, 28),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        });

        var name = UiTheme.Text(ItemData.DisplayName(itemId), 13,
            Config.TooltipColor(ItemGrade.ColorIndex(hit.Ext?.MagicOrRare ?? -1)));
        name.MouseFilter = MouseFilterEnum.Ignore;
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.ClipText = true;
        line.AddChild(name);

        string variant = hit.VariantLabel;
        if (variant.Length > 0)
        {
            var tag = UiTheme.Text(variant, 11, UiTheme.TextLo);
            tag.MouseFilter = MouseFilterEnum.Ignore;
            tag.CustomMinimumSize = new Vector2(86, 0);
            tag.HorizontalAlignment = HorizontalAlignment.Right;
            line.AddChild(tag);
        }

        SpinBox? quantity = null;
        if (showQuantity && hit.Def.Countable > 0)
        {
            quantity = UiTheme.NumberBox(1, 9_999, 1, 84);
            quantity.Value = 1;
            line.AddChild(quantity);
        }

        var action = new Button { Text = actionText, FocusMode = FocusModeEnum.None };
        action.AddThemeFontSizeOverride("font_size", 12);
        action.Pressed += () => onAction(hit, quantity == null ? 1 : (int)quantity.Value);
        line.AddChild(action);

        return row;
    }
}
