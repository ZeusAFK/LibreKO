using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _itemCombineLayer = null!;
    private HudWindow _itemCombinePanel = null!;
    private VBoxContainer _itemCombineList = null!;
    private bool _itemCombineShown;

    private void ItemCombineInit()
    {
        _itemCombineLayer = new CanvasLayer { Layer = 73 };
        AddChild(_itemCombineLayer);
        _itemCombinePanel = new HudWindow("itemcombine", "Item Combine", new Vector2(180, 120)) { Visible = false };
        _itemCombinePanel.Closed += CloseItemCombine;
        _itemCombineLayer.AddChild(_itemCombinePanel);
        var root = _itemCombinePanel.Body;
        root.AddThemeConstantOverride("separation", 6);
        root.AddChild(UiTheme.SectionTitle("Combine Items"));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(360, 320), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        _itemCombineList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _itemCombineList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_itemCombineList);

        Net.I.ItemCombineListEvent += OnItemCombineList;
    }

    private void ItemCombineDispose()
    {
        Net.I.ItemCombineListEvent -= OnItemCombineList;
    }

    private void ToggleItemCombine()
    {
        if (_itemCombineShown) { CloseItemCombine(); return; }
        _itemCombinePanel.Visible = true;
        _itemCombineShown = true;
        Net.I.SendItemCombineList();
    }

    private void CloseItemCombine()
    {
        if (!_itemCombineShown) return;
        _itemCombineShown = false;
        _itemCombinePanel.Visible = false;
    }

    private void OnItemCombineList(List<ItemCombineRecipe> list)
    {
        foreach (var c in _itemCombineList.GetChildren()) c.QueueFree();
        foreach (var r in list)
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);
            var name = UiTheme.Text(r.Name, 13, UiTheme.TextHi);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hb.AddChild(name);
            int recipeId = r.RecipeId;
            var btn = new Button { Text = "Combine", FocusMode = Control.FocusModeEnum.None };
            btn.Pressed += () => Net.I.SendItemCombine(recipeId);
            hb.AddChild(btn);
            _itemCombineList.AddChild(row);
        }
        if (_itemCombineList.GetChildCount() == 0)
        {
            var e = HudStyle.Label(13); e.Text = "No recipes available.";
            _itemCombineList.AddChild(e);
        }
    }
}
