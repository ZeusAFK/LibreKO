using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class ItemSearchPanel : VBoxContainer
{
    private readonly bool _tradeableOnly;
    private readonly bool _showQuantity;
    private readonly string _actionText;
    private readonly Action<ItemSearchHit, int> _onAction;
    private readonly Action<int>? _showTooltip;
    private readonly Action? _hideTooltip;

    private readonly List<string> _names = new();
    private readonly List<List<ItemSearchHit>> _hits = new();
    private readonly List<string> _groups = new();
    private readonly List<int> _levels = new();
    private readonly Dictionary<string, Button> _tabButtons = new();
    private string _tab = ItemSearch.TabBasic;

    private LineEdit _query = null!;
    private OptionButton _pick = null!, _group = null!, _level = null!;
    private VBoxContainer _results = null!;
    private Label _summary = null!;

    public ItemSearchPanel(
        bool tradeableOnly,
        string actionText,
        Action<ItemSearchHit, int> onAction,
        Action<int>? showTooltip = null,
        Action? hideTooltip = null,
        Vector2 resultsSize = default,
        bool showQuantity = true)
    {
        _tradeableOnly = tradeableOnly;
        _showQuantity = showQuantity;
        _actionText = actionText;
        _onAction = onAction;
        _showTooltip = showTooltip;
        _hideTooltip = hideTooltip;

        AddThemeConstantOverride("separation", 6);
        if (resultsSize == default) resultsSize = new Vector2(470, 320);
        CustomMinimumSize = new Vector2(resultsSize.X, 0);

        AddChild(UiTheme.SectionTitle("Item search", UiIcons.Get("system/bag")));

        var findRow = new HBoxContainer();
        findRow.AddThemeConstantOverride("separation", 5);
        AddChild(findRow);
        _query = new LineEdit
        {
            PlaceholderText = "item name",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _query.TextSubmitted += _ => Run();
        findRow.AddChild(_query);
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
            _tabButtons[key] = button;
            tabBar.AddChild(button);
        }
        _tabButtons[_tab].ButtonPressed = true;

        var pickRow = new HBoxContainer();
        pickRow.AddThemeConstantOverride("separation", 5);
        AddChild(pickRow);
        _pick = UiTheme.Dropdown();
        _pick.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _pick.ItemSelected += _ => OnNamePicked();
        pickRow.AddChild(_pick);
        _group = UiTheme.Dropdown();
        _group.CustomMinimumSize = new Vector2(124, 0);
        _group.ItemSelected += _ => OnGroupPicked();
        pickRow.AddChild(_group);
        _level = UiTheme.Dropdown();
        _level.CustomMinimumSize = new Vector2(96, 0);
        _level.ItemSelected += _ => Refresh();
        pickRow.AddChild(_level);

        _summary = UiTheme.Text("Type an item name and press Search.", 12, UiTheme.TextLo);
        AddChild(_summary);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = resultsSize,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(scroll);
        _results = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _results.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_results);
    }

    public void FocusQuery() => _query.GrabFocus();

    public void Run()
    {
        ItemSearch.Match(_query.Text.Trim(), _tradeableOnly, _names, _hits);

        _pick.Clear();
        foreach (string name in _names) _pick.AddItem(name);
        if (_names.Count > 0) _pick.Selected = 0;

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
        foreach (Node child in _results.GetChildren()) child.QueueFree();
        _hideTooltip?.Invoke();

        int levelIndex = _level.Selected - 1;
        int wantedPlus = levelIndex >= 0 && levelIndex < _levels.Count ? _levels[levelIndex] : -1;

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
            _results.AddChild(BuildRow(shown[i]));

        _summary.Text = _names.Count == 0
            ? "No item matches that name."
            : shown.Count switch
            {
                0 => $"Nothing on the {_tab} tab for this item.",
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

    public void SetQuery(string text) => _query.Text = text;

    internal void RegisterFirstUiPreview()
    {
        foreach (var hit in GroupHits()) { _onAction(hit, 1); return; }
    }

    public void SelectPlus(int plus)
    {
        for (int i = 0; i < _levels.Count; i++)
        {
            if (_levels[i] != plus) continue;
            _level.Selected = i + 1;
            Refresh();
            return;
        }
    }

    private void SetTab(string label)
    {
        _tab = label;
        foreach (var (key, button) in _tabButtons) button.ButtonPressed = key == label;
    }

    private bool TabHasHits(string label)
    {
        foreach (var hit in PickedHits())
            if (ItemSearch.TabOf(hit) == label) return true;
        return false;
    }

    private void OnNamePicked()
    {
        _groups.Clear();
        _group.Clear();
        _group.AddItem("All");

        var groups = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hit in TabHits()) groups.Add(hit.VariantLabel);
        groups.Remove("");

        foreach (string group in groups)
        {
            _groups.Add(group);
            _group.AddItem(group);
        }
        _group.Selected = 0;
        _group.Visible = _groups.Count > 0;
        OnGroupPicked();
    }

    private void OnGroupPicked()
    {
        _levels.Clear();
        _level.Clear();
        _level.AddItem("Any +");

        var levels = new SortedSet<int>();
        foreach (var hit in GroupHits()) levels.Add(hit.Plus);

        foreach (int level in levels)
        {
            _levels.Add(level);
            _level.AddItem($"+{level}");
        }
        _level.Selected = 0;
        Refresh();
    }

    private IEnumerable<ItemSearchHit> TabHits()
    {
        foreach (var hit in PickedHits())
            if (ItemSearch.TabOf(hit) == _tab) yield return hit;
    }

    private IEnumerable<ItemSearchHit> GroupHits()
    {
        int index = _group.Selected - 1;
        string wanted = index >= 0 && index < _groups.Count ? _groups[index] : "";
        foreach (var hit in TabHits())
            if (wanted.Length == 0
                || string.Equals(hit.VariantLabel, wanted, StringComparison.OrdinalIgnoreCase))
                yield return hit;
    }

    private IReadOnlyList<ItemSearchHit> PickedHits()
    {
        int picked = _pick.Selected;
        return picked >= 0 && picked < _hits.Count ? _hits[picked] : Array.Empty<ItemSearchHit>();
    }

    private Control BuildRow(ItemSearchHit hit)
    {
        int itemId = hit.Id;
        var row = UiTheme.RowPanel();
        row.CustomMinimumSize = new Vector2(0, 34);
        row.MouseEntered += () => _showTooltip?.Invoke(itemId);
        row.MouseExited += () => _hideTooltip?.Invoke();

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        row.AddChild(line);

        line.AddChild(new TextureRect
        {
            Texture = ItemData.Icon(itemId),
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
        if (_showQuantity && hit.Def.Countable > 0)
        {
            quantity = UiTheme.NumberBox(1, 9_999, 1, 84);
            quantity.Value = 1;
            line.AddChild(quantity);
        }

        var action = new Button { Text = _actionText, FocusMode = FocusModeEnum.None };
        action.AddThemeFontSizeOverride("font_size", 12);
        action.Pressed += () => _onAction(hit, quantity == null ? 1 : (int)quantity.Value);
        line.AddChild(action);

        return row;
    }
}
