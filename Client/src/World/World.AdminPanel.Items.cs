using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private ItemSearchPanel _admItemSearch = null!;
    private bool _admItemsLoaded;

    private LineEdit _admCustomItemId = null!;
    private SpinBox _admCustomItemCount = null!;
    private Label _admItemPreviewLbl = null!;
    private TextureRect _admItemPreviewIcon = null!;

    private Control BuildAdminItemsTab()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(640, 0) };
        box.AddThemeConstantOverride("separation", 7);

        // 1. Direct Item Spawner By ID
        box.AddChild(UiTheme.SectionTitle("Direct Item Spawner", UiIcons.Get("system/bag")));

        var spawnerRow = new HBoxContainer();
        spawnerRow.AddThemeConstantOverride("separation", 6);
        box.AddChild(spawnerRow);

        _admCustomItemId = new LineEdit
        {
            PlaceholderText = "Item ID (e.g. 379021000)",
            CustomMinimumSize = new Vector2(200, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _admCustomItemId.TextChanged += OnAdminCustomItemIdChanged;
        spawnerRow.AddChild(_admCustomItemId);

        spawnerRow.AddChild(UiTheme.Text("Qty:", 12, UiTheme.TextLo));
        _admCustomItemCount = UiTheme.NumberBox(1, 9999, 1, 80);
        _admCustomItemCount.Value = 1;
        spawnerRow.AddChild(_admCustomItemCount);

        int[] countPresets = { 1, 10, 100, 999 };
        foreach (int cp in countPresets)
        {
            int val = cp;
            var cpBtn = new Button { Text = $"x{val}", FocusMode = Control.FocusModeEnum.None };
            cpBtn.AddThemeFontSizeOverride("font_size", 11);
            cpBtn.Pressed += () => _admCustomItemCount.Value = val;
            spawnerRow.AddChild(cpBtn);
        }

        var giveBtn = new Button { Text = "Give Item", FocusMode = Control.FocusModeEnum.None };
        giveBtn.Pressed += OnAdminGiveCustomItem;
        spawnerRow.AddChild(giveBtn);

        // Live Item Preview Row
        var previewRow = new HBoxContainer();
        previewRow.AddThemeConstantOverride("separation", 6);
        box.AddChild(previewRow);

        _admItemPreviewIcon = new TextureRect
        {
            CustomMinimumSize = new Vector2(24, 24),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Visible = false,
        };
        previewRow.AddChild(_admItemPreviewIcon);

        _admItemPreviewLbl = UiTheme.Text("Type an Item ID to preview and spawn directly.", 11, UiTheme.TextDim);
        previewRow.AddChild(_admItemPreviewLbl);

        box.AddChild(new HSeparator());

        // 2. Quick Presets Shelf (Knight Online Staples)
        box.AddChild(UiTheme.SectionTitle("Quick Consumables & Essentials", UiIcons.Get("game/chest")));

        var shelfFlow = new HFlowContainer();
        shelfFlow.AddThemeConstantOverride("h_separation", 4);
        shelfFlow.AddThemeConstantOverride("v_separation", 4);
        box.AddChild(shelfFlow);

        (string name, int itemId, int defaultQty)[] quickItems =
        {
            ("BUS (+0)", 379021000, 10),
            ("BES (+0)", 379025000, 10),
            ("Trina", 700002000, 1),
            ("1500 HP Scroll", 800013000, 10),
            ("300 AC Scroll", 800010000, 10),
            ("Speed Potion", 379129000, 20),
            ("HP Pot (Water of Life)", 389011000, 100),
            ("MP Pot (Potion of Soul)", 389020000, 100),
            ("Monster Stone", 900144000, 1),
            ("Blue Gem", 389199000, 5),
            ("Green Gem", 389201000, 5),
            ("Red Gem", 389197000, 5),
        };

        foreach (var (qName, qId, qDefQty) in quickItems)
        {
            int itmId = qId;
            int itmQty = qDefQty;
            var btn = new Button
            {
                Text = $"{qName} x{itmQty}",
                FocusMode = Control.FocusModeEnum.None,
                TooltipText = $"Click to spawn {qName} (ID: {itmId})",
            };
            btn.AddThemeFontSizeOverride("font_size", 11);
            btn.Pressed += () => OnAdminGiveItem(itmId, itmQty);
            btn.MouseEntered += () => ShowItemTooltip(-1, TooltipItem(itmId));
            btn.MouseExited += HideItemTooltip;
            shelfFlow.AddChild(btn);
        }

        box.AddChild(new HSeparator());

        // 3. Quick Search Chips & Full Search Panel
        var chipsRow = new HFlowContainer();
        chipsRow.AddThemeConstantOverride("h_separation", 4);
        chipsRow.AddThemeConstantOverride("v_separation", 4);
        box.AddChild(chipsRow);
        chipsRow.AddChild(UiTheme.Text("Quick Search:", 11, UiTheme.TextLo));

        (string chipLabel, string searchQuery, int targetPlus)[] searchChips =
        {
            ("Raptor (+8)", "Raptor", 8),
            ("Shard (+8)", "Shard", 8),
            ("Iron Impact (+8)", "Iron Impact", 8),
            ("Mirage Dagger (+8)", "Mirage Dagger", 8),
            ("Elixir Staff (+8)", "Elixir Staff", 8),
            ("Chitin Bow (+8)", "Chitin Bow", 8),
            ("Hell Breaker (+8)", "Hell Breaker", 8),
            ("Iron Necklace", "Iron Necklace", 0),
            ("Iron Belt", "Iron Belt", 0),
            ("Ring of Felankor", "Ring of the Felankor", 0),
            ("Chitin (+8)", "Chitin", 8),
            ("Chitin Shell (+8)", "Chitin Shell", 8),
        };

        foreach (var (cLabel, cQuery, cPlus) in searchChips)
        {
            string query = cQuery;
            int plus = cPlus;
            var chipBtn = new Button { Text = cLabel, FocusMode = Control.FocusModeEnum.None };
            chipBtn.AddThemeFontSizeOverride("font_size", 11);
            chipBtn.Pressed += () =>
            {
                _admItemSearch.SetQuery(query);
                _admItemSearch.Run();
                if (plus > 0) _admItemSearch.SelectPlus(plus);
            };
            chipsRow.AddChild(chipBtn);
        }

        _admItemSearch = new ItemSearchPanel(
            tradeableOnly: false,
            actionText: "Add",
            onAction: (hit, count) => OnAdminGiveItem(hit.Id, count),
            showTooltip: itemId => ShowItemTooltip(-1, TooltipItem(itemId)),
            hideTooltip: HideItemTooltip,
            resultsSize: new Vector2(640, 220));
        box.AddChild(_admItemSearch);

        return box;
    }

    private void OnAdminCustomItemIdChanged(string text)
    {
        if (_admItemPreviewLbl == null) return;
        if (!int.TryParse(text.Trim(), out int id) || id <= 0)
        {
            _admItemPreviewLbl.Text = "Type an Item ID to preview and spawn directly.";
            _admItemPreviewLbl.AddThemeColorOverride("font_color", UiTheme.TextDim);
            _admItemPreviewIcon.Visible = false;
            return;
        }

        var def = ItemData.Get(id);
        if (def != null || ItemData.DisplayName(id).Length > 0)
        {
            string name = ItemData.DisplayName(id);
            _admItemPreviewLbl.Text = $"{name} (ID: {id})";
            _admItemPreviewLbl.AddThemeColorOverride("font_color", UiTheme.GoldBright);
            var icon = ItemData.Icon(id);
            if (icon != null)
            {
                _admItemPreviewIcon.Texture = icon;
                _admItemPreviewIcon.Visible = true;
            }
            else
            {
                _admItemPreviewIcon.Visible = false;
            }
        }
        else
        {
            _admItemPreviewLbl.Text = $"Unknown or custom item ID ({id})";
            _admItemPreviewLbl.AddThemeColorOverride("font_color", UiTheme.Bad);
            _admItemPreviewIcon.Visible = false;
        }
    }

    private void OnAdminGiveCustomItem()
    {
        if (!int.TryParse(_admCustomItemId.Text.Trim(), out int id) || id <= 0)
        {
            SetAdminStatus("Enter a valid numeric Item ID.", true);
            return;
        }
        int count = (int)_admCustomItemCount.Value;
        OnAdminGiveItem(id, count);
    }

    private void OnAdminGiveItem(int itemId, int count)
    {
        Net.I.SendAdminGiveItem(itemId, count);
        SetAdminStatus($"Requesting {ItemData.DisplayName(itemId)} x{count}…", false);
    }
}
