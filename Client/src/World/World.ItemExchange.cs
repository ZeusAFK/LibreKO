using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _itemExchangeLayer = null!;
    private HudWindow _itemExchangePanel = null!;
    private VBoxContainer _itemExchangeList = null!;
    private bool _itemExchangeShown;

    private void ItemExchangeInit()
    {
        _itemExchangeLayer = new CanvasLayer { Layer = 64 };
        AddChild(_itemExchangeLayer);
        _itemExchangePanel = new HudWindow("itemexchange", "Item Exchange", new Vector2(200, 130)) { Visible = false };
        _itemExchangePanel.Closed += CloseItemExchange;
        _itemExchangeLayer.AddChild(_itemExchangePanel);
        var root = _itemExchangePanel.Body;
        root.AddThemeConstantOverride("separation", 6);
        root.AddChild(UiTheme.SectionTitle("Item Exchange"));
        root.AddChild(UiTheme.Text("Turn in materials for a reward item.", 12, UiTheme.TextLo));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(380, 320), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        _itemExchangeList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _itemExchangeList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_itemExchangeList);

        Net.I.ItemExchangeListEvent += OnItemExchangeList;
    }

    private void ItemExchangeDispose()
    {
        Net.I.ItemExchangeListEvent -= OnItemExchangeList;
    }

    private void ToggleItemExchange()
    {
        if (_itemExchangeShown) { CloseItemExchange(); return; }
        _itemExchangePanel.Visible = true;
        _itemExchangeShown = true;
        Net.I.SendItemExchangeList();
    }

    private void CloseItemExchange()
    {
        if (!_itemExchangeShown) return;
        _itemExchangeShown = false;
        _itemExchangePanel.Visible = false;
    }

    private void OnItemExchangeList(List<ItemExchangeRecipe> list)
    {
        foreach (var c in _itemExchangeList.GetChildren()) c.QueueFree();
        foreach (var r in list)
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var vb = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            vb.AddThemeConstantOverride("separation", 2);
            row.AddChild(vb);

            var titleRow = new HBoxContainer(); titleRow.AddThemeConstantOverride("separation", 8);
            vb.AddChild(titleRow);
            var name = UiTheme.Text(r.Name, 13, UiTheme.TextHi);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            titleRow.AddChild(name);
            int recipeId = r.RecipeId;
            var btn = new Button { Text = "Exchange", FocusMode = Control.FocusModeEnum.None };
            btn.Pressed += () => Net.I.SendItemExchange(recipeId);
            titleRow.AddChild(btn);

            string inName = ItemData.DisplayName(r.InputItemId);
            string outName = ItemData.DisplayName(r.OutputItemId);
            vb.AddChild(UiTheme.Text($"Give {r.InputCount} x {inName}  ->  {outName}", 12, UiTheme.TextLo));

            _itemExchangeList.AddChild(row);
        }
        if (_itemExchangeList.GetChildCount() == 0)
        {
            var e = HudStyle.Label(13); e.Text = "No exchanges available.";
            _itemExchangeList.AddChild(e);
        }
    }

}
