using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace LibreKO;

public partial class World : Node3D
{
    private const int ItemTooltipLayer = 85;
    private const int WornDurabilityPercent = 25;
    private const int TooltipColorWorn = 2;
    private const int TooltipColorMerchant = 4;

    private CanvasLayer _itemTipLayer = null!;

    private void BuildItemTooltip()
    {
        _itemTipLayer = new CanvasLayer { Layer = ItemTooltipLayer };
        AddChild(_itemTipLayer);

        _itemTipPanel = new PanelContainer
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 250,
        };
        var bg = new StyleBoxFlat
        {
            BgColor = new Color(0.031f, 0.027f, 0.024f, 0.97f),
            BorderColor = new Color(0.40f, 0.33f, 0.19f, 0.92f),
            ShadowColor = new Color(0, 0, 0, 0.60f),
            ShadowSize = 8,
        };
        bg.SetBorderWidthAll(1);
        bg.SetCornerRadiusAll(2);
        _itemTipPanel.AddThemeStyleboxOverride("panel", bg);
        _itemTipLayer.AddChild(_itemTipPanel);

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 10, 8, 10, 9);
        _itemTipPanel.AddChild(margin);

        _itemTipLines = new VBoxContainer { CustomMinimumSize = new Vector2(232, 0) };
        _itemTipLines.AddThemeConstantOverride("separation", 1);
        margin.AddChild(_itemTipLines);
    }

    private void InventoryHover(ItemCell cell, bool entered)
    {
        if (!entered)
        {
            if (_hoverCell == cell) HideItemTooltip();
            return;
        }
        if (cell.Current.IsEmpty) return;
        _hoverCell = cell;
        ShowItemTooltip(cell.Slot, cell.Current);
    }

    public bool ItemTooltipVisible => _itemTipPanel is { Visible: true };

    private void ShowItemTooltip(int absSlot, ItemSlot item, string note = "")
    {
        if (_itemTipPanel == null || _itemTipLines == null) return;
        ClearTooltipLines();
        var lines = BuildTooltipLines(absSlot, item);
        if (note.Length > 0)
        {
            lines.Add(TooltipLine.Rule());
            lines.Add(new TooltipLine(note, TooltipColorMerchant));
        }
        foreach (var line in lines)
        {
            if (line.Divider)
            {
                _itemTipLines.AddChild(new TextureRect
                {
                    Texture = UiTheme.Divider(),
                    CustomMinimumSize = new Vector2(0, 3),
                    StretchMode = TextureRect.StretchModeEnum.Scale,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                });
                continue;
            }

            var label = HudStyle.Label(TooltipFontHeight, line.Align);
            label.Text = line.Text;
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            label.AutowrapMode = TextServer.AutowrapMode.Off;
            label.AddThemeColorOverride("font_color", TooltipColor(line.Color));
            if (TooltipBold) label.AddThemeConstantOverride("font_embolden", 1);
            _itemTipLines.AddChild(label);
        }
        _itemTipPanel.Visible = true;
        UpdateInventoryTooltip();
    }

    private static ItemSlot TooltipItem(int itemId, int count = 1)
    {
        var def = ItemData.Get(itemId);
        int durability = (def?.Duration ?? 0) + (ItemData.ExtFor(itemId)?.DurationBonus ?? 0);
        return new ItemSlot
        {
            ItemId = itemId,
            Count = (short)count,
            Durability = (short)Mathf.Min(durability, short.MaxValue),
        };
    }

    private void HideItemTooltip()
    {
        _hoverCell = null;
        if (_itemTipPanel == null) return;
        _itemTipPanel.Visible = false;
        ClearTooltipLines();
    }

    private void ClearTooltipLines()
    {
        if (_itemTipLines == null) return;
        foreach (Node child in _itemTipLines.GetChildren())
        {
            _itemTipLines.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void UpdateInventoryTooltip()
    {
        if (_itemTipPanel == null || !_itemTipPanel.Visible) return;
        var vp = GetViewport();
        if (vp == null) return;
        var viewport = vp.GetVisibleRect().Size;
        var p = vp.GetMousePosition() + new Vector2(16, 16);
        var size = _itemTipPanel.Size;
        if (size.X <= 1 || size.Y <= 1) size = _itemTipPanel.GetCombinedMinimumSize();
        p.X = Mathf.Clamp(p.X, 8, Mathf.Max(8, viewport.X - size.X - 8));
        p.Y = Mathf.Clamp(p.Y, 8, Mathf.Max(8, viewport.Y - size.Y - 8));
        _itemTipPanel.Position = p;
    }

    private List<TooltipLine> BuildTooltipLines(int absSlot, ItemSlot item)
    {
        var lines = new List<TooltipLine>();
        var def = ItemData.Get(item.ItemId);
        if (def == null)
        {
            lines.Add(new TooltipLine($"Item {item.ItemId}", 0));
            return lines;
        }

        var ext = ItemData.ExtFor(item.ItemId);
        int rarity = ext?.MagicOrRare ?? -1;
        lines.Add(new TooltipLine(ItemData.DisplayName(item.ItemId), ItemGrade.ColorIndex(rarity)));

        string marker = RarityMarker(rarity);
        if (marker.Length > 0)
            lines.Add(new TooltipLine(marker, RarityMarkerColor(rarity), HorizontalAlignment.Right));

        string itemClass = ItemClassName(def.Kind);
        if (itemClass.Length > 0)
            lines.Add(new TooltipLine(itemClass, 0, HorizontalAlignment.Right));

        lines.Add(TooltipLine.Rule());

        int maxDurability = def.Duration + (ext?.DurationBonus ?? 0);
        if (maxDurability > 1)
        {
            int current = item.Durability;
            int percent = current * 100 / maxDurability;
            if (percent == 0 && current > 0) percent = 1;
            lines.Add(new TooltipLine(
                $"{StatLabel(4614, "Durability")} : {current} / {maxDurability} ({percent}%)",
                percent < WornDurabilityPercent ? TooltipColorWorn : 0));
        }

        if (IsWeaponItem(def))
        {
            int attack = def.Damage > 0 ? def.Damage : def.Value;
            AddStatLine(lines, absSlot, def, ext, 4525, "Attack Power", attack, ext?.BonusDamage ?? 0,
                (d, e) => (d.Damage > 0 ? d.Damage : d.Value) + (e?.BonusDamage ?? 0));
            AddSpeedLine(lines, def.Delay);
            if (def.Range > 0)
                lines.Add(new TooltipLine(FormatFloatText(4507, "Effective Range", def.Range / 10.0f), 0));
        }
        else
        {
            int defense = def.Ac > 0 ? def.Ac : def.Value;
            AddStatLine(lines, absSlot, def, ext, 4526, "Defense Ability", defense, ext?.BonusAc ?? 0,
                (d, e) => (d.Ac > 0 ? d.Ac : d.Value) + (e?.BonusAc ?? 0));
        }

        AddStatLine(lines, absSlot, def, ext, 4521, "Strength Bonus", 0, ext?.BonusStr ?? 0,
            (_, e) => e?.BonusStr ?? 0, 4);
        AddStatLine(lines, absSlot, def, ext, 0, "Health Bonus", 0, ext?.BonusSta ?? 0,
            (_, e) => e?.BonusSta ?? 0, 4);
        AddStatLine(lines, absSlot, def, ext, 4518, "Dexterity Bonus", 0, ext?.BonusDex ?? 0,
            (_, e) => e?.BonusDex ?? 0, 4);
        AddStatLine(lines, absSlot, def, ext, 4520, "Intelligence Bonus", 0, ext?.BonusInt ?? 0,
            (_, e) => e?.BonusInt ?? 0, 4);
        AddStatLine(lines, absSlot, def, ext, 4517, "Magic Power Bonus", 0, ext?.BonusCha ?? 0,
            (_, e) => e?.BonusCha ?? 0, 4);
        AddStatLine(lines, absSlot, def, ext, 4519, "HP Bonus", 0, ext?.BonusMaxHp ?? 0,
            (_, e) => e?.BonusMaxHp ?? 0, 4);
        AddStatLine(lines, absSlot, def, ext, 4522, "MP Bonus", 0, ext?.BonusMaxMp ?? 0,
            (_, e) => e?.BonusMaxMp ?? 0, 4);

        AddSpecialLine(lines, ext);

        AddStatLine(lines, absSlot, def, ext, 4548, "Resistance to Flame", 0, ext?.BonusFireR ?? 0,
            (_, e) => e?.BonusFireR ?? 0, 3);
        AddStatLine(lines, absSlot, def, ext, 4549, "Resistance to Glacier", 0, ext?.BonusColdR ?? 0,
            (_, e) => e?.BonusColdR ?? 0, 3);
        AddStatLine(lines, absSlot, def, ext, 4547, "Resistance to Lightning", 0, ext?.BonusLightningR ?? 0,
            (_, e) => e?.BonusLightningR ?? 0, 3);
        AddStatLine(lines, absSlot, def, ext, 4550, "Resistance to Magic", 0, ext?.BonusMagicR ?? 0,
            (_, e) => e?.BonusMagicR ?? 0, 3);
        AddStatLine(lines, absSlot, def, ext, 4551, "Resistance to Poison", 0, ext?.BonusPoisonR ?? 0,
            (_, e) => e?.BonusPoisonR ?? 0, 3);
        AddStatLine(lines, absSlot, def, ext, 4546, "Resistance to Curse", 0, ext?.BonusCurseR ?? 0,
            (_, e) => e?.BonusCurseR ?? 0, 3);

        if (def.Weight > 0)
            lines.Add(new TooltipLine(FormatFloatText(4553, "Weight", def.Weight / 10.0f), 0));

        if (ItemData.IsSellable(item.ItemId))
            lines.Add(new TooltipLine(
                FormatMoneyText(4552, "Selling Price", ItemData.SellPrice(item.ItemId)), 0));

        if (item.Count > 1)
            lines.Add(new TooltipLine($"Count {item.Count}", 0));

        if (def.ReqLevel > 0)
        {
            int color = Sheet.Level > 0 && Sheet.Level < def.ReqLevel ? 13 : 0;
            string text = def.ReqLevelMax > def.ReqLevel && def.ReqLevelMax < 100
                ? FormatRangeText(4558, "Required Level", def.ReqLevel, def.ReqLevelMax)
                : FormatIntText(4541, "Required Level", def.ReqLevel);
            lines.Add(new TooltipLine(text, color));
        }
        if (def.ReqCls > 0)
            lines.Add(new TooltipLine(FormatIntText(4542, "Required Class", def.ReqCls),
                RequirementClassFailed(def.ReqCls) ? 13 : 0));
        AddRequirement(lines, 4544, "Required Strength", def.ReqStr + (ext?.ReqStrBonus ?? 0), Sheet.Str);
        AddRequirement(lines, 4543, "Required Health", def.ReqSta, Sheet.Sta);
        AddRequirement(lines, 4538, "Required Dexterity", def.ReqDex, Sheet.Dex);
        AddRequirement(lines, 4540, "Required Intelligence", def.ReqInt, Sheet.Intel);
        AddRequirement(lines, 4537, "Required Magic Power", def.ReqCha, Sheet.Mag);

        string grade = RarityGradeLine(rarity);
        if (grade.Length > 0)
        {
            lines.Add(TooltipLine.Rule());
            lines.Add(new TooltipLine(grade, 4, HorizontalAlignment.Center));
        }

        var description = new List<string>(SplitDescription(def.Desc));
        if (description.Count > 0)
        {
            if (grade.Length == 0) lines.Add(TooltipLine.Rule());
            foreach (var text in description)
                lines.Add(new TooltipLine(text, 12, HorizontalAlignment.Center));
        }

        lines.Add(new TooltipLine(
            ItemData.Text(18500, "[Enable Comparison by pressing the 'Ctrl' Key.]"),
            9, HorizontalAlignment.Center));

        return lines;
    }

    private static string RarityMarker(int rarity) => rarity switch
    {
        < 0 => ItemData.Text(2402, "Regular item"),
        0 => ItemData.Text(2401, "Craft item"),
        1 => ItemData.Text(2403, "Rare item"),
        2 => ItemData.Text(2403, "Rare item"),
        3 => ItemData.Text(2404, "Magic item"),
        4 => ItemData.Text(2405, "Unique item"),
        5 => ItemData.Text(2406, "Upgrade item"),
        6 => ItemData.Text(2407, "An event item"),
        11 => ItemData.Text(2409, "Reverse item"),
        12 => ItemData.Text(2408, "Reverse unique item"),
        _ => "",
    };

    private static string RarityGradeLine(int rarity) => rarity switch
    {
        4 => ItemData.Text(4585, "Item Grade : Unique"),
        11 => "",
        12 => ItemData.Text(4584, "Item Grade : Reverse unique item"),
        _ => "",
    };

    private static int RarityMarkerColor(int rarity) => rarity switch
    {
        11 => 13,
        12 => Config.RarityNameReverseUnique,
        _ => 0,
    };

    private static string ItemClassName(int kind)
    {
        int textId = kind switch
        {
            11 or 12 => 2514, 21 => 2529, 22 => 2530,
            31 => 2507, 32 => 2508,
            41 or 43 => 2520, 42 => 2521,
            51 => 2527, 52 or 61 or 62 or 63 => 2522,
            60 => 2526, 70 => 2510, 71 => 2511, 80 => 2512,
            91 => 2515, 92 => 2501, 93 => 2524, 94 => 2509,
            95 => 2513, 96 => 2518, 97 => 2523, 98 => 2525,
            100 => 2519, 101 => 2537, 110 => 2528, 120 => 2506, 130 => 2517,
            140 => 2539, 150 => 2534, 151 => 2535, 181 => 2538,
            200 => 2540, 210 => 2505, 220 => 2504, 230 => 2502, 240 => 2503,
            252 => 2536,
            _ => 0,
        };
        return textId == 0 ? "" : ItemData.Text(textId, "");
    }

    private void AddStatLine(
        List<TooltipLine> lines,
        int absSlot,
        ItemData.Item def,
        ItemData.Ext? ext,
        int textId,
        string fallback,
        int baseValue,
        int bonusValue,
        System.Func<ItemData.Item, ItemData.Ext?, int> selector,
        int plainColor = 0)
    {
        if (baseValue == 0 && bonusValue == 0) return;
        int total = baseValue + bonusValue;
        int color = CompareColor(absSlot, def, total, selector);
        if (color == 0) color = plainColor;
        lines.Add(new TooltipLine($"{StatLabel(textId, fallback)} : {total}", color));
    }

    private void AddSpecialLine(List<TooltipLine> lines, ItemData.Ext? ext)
    {
        if (ext == null) return;

        AddElementLine(lines, 4508, "Flame Damage", ext.FireDamage);
        AddElementLine(lines, 4509, "Glacier Damage", ext.IceDamage);
        AddElementLine(lines, 4510, "Lightning Damage", ext.LightningDamage);
        AddElementLine(lines, 4511, "Poison Damage", ext.PoisonDamage);

        if (ext.Special == 0) return;
        if (ext.FireDamage != 0 || ext.IceDamage != 0 || ext.LightningDamage != 0 || ext.PoisonDamage != 0)
            return;

        int textId = SpecialTextId(ext.Name);
        if (textId == 0) return;
        string fallback = textId switch
        {
            4512 => "HP Recovery",
            4513 => "MP Damage",
            _ => "Special Effect",
        };
        lines.Add(new TooltipLine($"{StatLabel(textId, fallback)} : {ext.Special}", 8));
    }

    private static void AddElementLine(List<TooltipLine> lines, int textId, string fallback, int value)
    {
        if (value == 0) return;
        lines.Add(new TooltipLine($"{StatLabel(textId, fallback)} : {value}", 8));
    }

    private static void AddSpeedLine(List<TooltipLine> lines, int delay)
    {
        if (delay <= 0) return;
        int textId = delay switch
        {
            <= 100 => 4505,
            <= 111 => 4502,
            <= 130 => 4503,
            <= 150 => 4504,
            _ => 4506,
        };
        lines.Add(new TooltipLine(ItemData.Text(textId, "Attack Speed"), 0));
    }

    private static void AddRequirement(List<TooltipLine> lines, int textId, string fallback, int value, int mine)
    {
        if (value <= 0) return;
        string text = FormatIntText(textId, fallback, value);
        if (mine > 0) text = $"{text} ({mine})";
        lines.Add(new TooltipLine(text, mine > 0 && mine < value ? 13 : 0));
    }

    private int CompareColor(int absSlot, ItemData.Item def, int value, System.Func<ItemData.Item, ItemData.Ext?, int> selector)
    {
        if (absSlot < GridStart) return 0;
        int eq = Inv.ResolveEquipDest(def.Slot, ItemData.EquipSlotFor(def));
        if (eq < 0 || eq >= Inv.Length || Inv[eq].IsEmpty) return 0;
        var equipped = ItemData.Get(Inv[eq].ItemId);
        if (equipped == null) return 0;
        int other = selector(equipped, ItemData.ExtFor(Inv[eq].ItemId));
        if (value == other) return 0;
        return value > other ? 1 : 2;
    }

    private bool RequirementClassFailed(int reqCls)
    {
        if (reqCls <= 0 || _selfClass <= 0) return false;
        if (reqCls == _selfClass) return false;
        int reqFamily = ClassFamily(reqCls);
        int selfFamily = ClassFamily(_selfClass);
        if (reqFamily > 0 && selfFamily > 0) return reqFamily != selfFamily;
        int reqBase = reqCls / 10, selfBase = _selfClass / 10;
        return reqBase > 0 && selfBase > 0 && reqBase != selfBase;
    }

    private static int ClassFamily(int cls) => cls switch
    {
        1 or 5 or 6 or 13 or 14 or 15 => 1,
        2 or 7 or 8 => 2,
        3 or 9 or 10 => 3,
        4 or 11 or 12 => 4,
        _ => cls >= 100 ? ClassFamily(cls % 100) : 0,
    };

    private static IEnumerable<string> SplitDescription(string desc)
    {
        desc = StripTags(desc).Replace('|', '\n').Replace("\r", "");
        foreach (var raw in desc.Split('\n'))
        {
            string s = raw.Trim();
            if (s.Length > 0) yield return s;
        }
    }

    private static string StripTags(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new System.Text.StringBuilder(value.Length);
        bool inTag = false;
        foreach (char ch in value)
        {
            if (ch == '<') { inTag = true; continue; }
            if (ch == '>' && inTag) { inTag = false; continue; }
            if (!inTag) sb.Append(ch);
        }
        return sb.ToString().Replace("@#", "#").Replace("@", "");
    }

    private static string FormatBaseBonus(int baseValue, int bonusValue)
    {
        if (baseValue != 0 && bonusValue > 0) return $"{baseValue}(+{bonusValue})";
        if (baseValue != 0 && bonusValue < 0) return $"{baseValue}({bonusValue})";
        return (baseValue + bonusValue).ToString();
    }

    private static string FormatIntText(int textId, string fallback, int value)
    {
        string s = ItemData.Text(textId, $"{fallback} : %d");
        return s.Replace("%d", value.ToString()).Replace("%s", "").Replace("%%", "%").Trim();
    }

    private static string FormatRangeText(int textId, string fallback, int a, int b)
    {
        string s = ItemData.Text(textId, $"{fallback} : %d ~ %d");
        int first = s.IndexOf("%d", System.StringComparison.Ordinal);
        if (first >= 0)
        {
            s = s.Remove(first, 2).Insert(first, a.ToString());
            int second = s.IndexOf("%d", first + 1, System.StringComparison.Ordinal);
            if (second >= 0)
                s = s.Remove(second, 2).Insert(second, b.ToString());
        }
        return s.Replace("%s", "").Replace("%%", "%").Trim();
    }

    private static string FormatMoneyText(int textId, string fallback, long value)
    {
        string s = ItemData.Text(textId, $"{fallback} : %s");
        string v = value.ToString("n0", CultureInfo.InvariantCulture);
        return s.Replace("%s", v).Replace("%d", v).Trim();
    }

    private static string FormatFloatText(int textId, string fallback, float value)
    {
        string s = ItemData.Text(textId, $"{fallback} : %.2f");
        string v = value.ToString("0.00", CultureInfo.InvariantCulture);
        return s.Replace("%.2f", v).Replace("%f", v).Trim();
    }

    private static string StatLabel(int textId, string fallback)
    {
        if (textId <= 0) return fallback;
        string s = ItemData.Text(textId, fallback);
        int fmt = s.IndexOf('%');
        if (fmt >= 0) s = s[..fmt];
        return s.Replace(":", "").Trim();
    }

    private static int SpecialTextId(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("flame") || n.Contains("fire")) return 4508;
        if (n.Contains("frozen") || n.Contains("glacier") || n.Contains("frost") || n.Contains("ice")) return 4509;
        if (n.Contains("lightning")) return 4510;
        if (n.Contains("poison") || n.Contains("viper")) return 4511;
        if (n.Contains("vampire")) return 4512;
        if (n.Contains("mp damage")) return 4513;
        return 0;
    }

    private static bool IsWeaponItem(ItemData.Item def) => def.Slot is 0 or 1 or 3 or 4 && def.Kind != 60;

    private static Color TooltipColor(int idx) => Config.TooltipColor(idx);
}
