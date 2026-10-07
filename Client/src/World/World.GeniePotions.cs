using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    // Zero preserves the existing strongest-potion selection.
    private int _genieHpItem, _genieMpItem;
    private Button _genieHpSelect = null!, _genieMpSelect = null!;
    private PopupMenu _geniePotionPicker = null!;
    private int _geniePotionTarget;
    private double _geniePotionUiAt;

    private void BuildGeniePotionSelector(VBoxContainer parent, int target)
    {
        var button = new Button { Text = "Automatic (strongest)",
            CustomMinimumSize = new Vector2(210, 30), ExpandIcon = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            FocusMode = Control.FocusModeEnum.None };
        button.AddThemeConstantOverride("icon_max_width", 24);
        parent.AddChild(button);
        if (target == HealTarget.Hp) _genieHpSelect = button; else _genieMpSelect = button;
        button.Pressed += () => OpenGeniePotionPicker(target);
        if (_geniePotionPicker != null) return;
        _geniePotionPicker = new PopupMenu();
        _genieLayer.AddChild(_geniePotionPicker);
        _geniePotionPicker.IdPressed += id =>
        {
            if (_geniePotionTarget == HealTarget.Hp) _genieHpItem = (int)id; else _genieMpItem = (int)id;
            RefreshGeniePotionSelectors();
        };
    }

    private void OpenGeniePotionPicker(int target)
    {
        _geniePotionTarget = target;
        _geniePotionPicker.Clear();
        _geniePotionPicker.AddItem("Automatic (strongest)", 0);
        var seen = new HashSet<int>();
        for (int i = GridStart; i < Inv.Length; i++)
        {
            if (i >= InventoryConstants.CospreStart && i < InventoryConstants.MagicBagStart) continue;
            int id = Inv[i].ItemId;
            if (id <= 0 || !seen.Add(id) || ItemData.PotionHeal(id, target) <= 0 || CountInBackpack(id) <= 0) continue;
            _geniePotionPicker.AddIconItem(ItemData.Icon(id),
                $"{ItemData.DisplayName(id)} ({CountInBackpack(id)})", id);
        }
        var button = target == HealTarget.Hp ? _genieHpSelect : _genieMpSelect;
        _geniePotionPicker.Position = (Vector2I)button.GetGlobalRect().End;
        _geniePotionPicker.Popup();
    }

    private void RefreshGeniePotionSelectors()
    {
        RefreshGeniePotionSelector(_genieHpSelect, _genieHpItem);
        RefreshGeniePotionSelector(_genieMpSelect, _genieMpItem);
    }

    private void RefreshGeniePotionSelector(Button button, int id)
    {
        button.Icon = id > 0 ? ItemData.Icon(id) : null;
        button.Text = id > 0 ? $"{ItemData.DisplayName(id)} ({CountInBackpack(id)})" : "Automatic (strongest)";
        button.TooltipText = id > 0
            ? $"{ItemData.DisplayName(id)}\nUses only this potion. Stops when it runs out."
            : "Automatically chooses the strongest available potion.";
    }

    private void UseGeniePotion(int target)
    {
        int selected = target == HealTarget.Hp ? _genieHpItem : _genieMpItem;
        int id = selected == 0 ? BestPotion(target) : selected;
        if (id <= 0 || CountInBackpack(id) <= 0 || ItemData.PotionHeal(id, target) <= 0
            || !MaestroPotions.CanUse(id, Sheet.Gold)) return;
        UseHotItem(id);
    }
}
