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
    private const int TooltipColorGmItemId = 8;

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
            lines.Add(new TooltipLine($"{Localization.Loc.Tr("Item")} {Localization.Loc.Num(item.ItemId)}", 0));
            return lines;
        }

        var ext = ItemData.ExtFor(item.ItemId);
        int rarity = ext?.MagicOrRare ?? -1;
        lines.Add(new TooltipLine(ItemData.DisplayName(item.ItemId), ItemGrade.ColorIndex(rarity)));

        if (_isGm)
        {
            lines.Add(new TooltipLine($"{Localization.Loc.Tr("Item ID")}：{Localization.Loc.Num(item.ItemId)}", TooltipColorGmItemId));
        }

        string marker = RarityMarker(rarity);
        if (marker.Length > 0)
            lines.Add(new TooltipLine(marker, RarityMarkerColor(rarity), HorizontalAlignment.Right));

        string itemClass = ItemClassName(def.Kind);
        if (itemClass.Length > 0)
        {
            bool unusable = _selfClass > 0
                && (EquipRules.ForbidsKind(_selfClass, def.Kind) || RequirementClassFailed(def.ReqCls));
            lines.Add(unusable
                ? new TooltipLine($"{itemClass} {ItemData.Text(UnableToEquipText, "(Unable to equip)")}",
                    TooltipColorFailed, HorizontalAlignment.Right)
                : new TooltipLine(itemClass, 0, HorizontalAlignment.Right));
        }

        lines.Add(TooltipLine.Rule());

        if (item.IsLinked && Net.I.PetItems.TryGetValue(item.UniqueId, out var pet))
        {
            lines.Add(new TooltipLine(pet.Name, TooltipColorMerchant));
            lines.Add(new TooltipLine($"{Localization.Loc.Tr("Level")} {pet.Level}  {Localization.Loc.Tr("EXP")} {pet.ExpPercent / 100f:0.00}%", 0));
            lines.Add(new TooltipLine($"{Localization.Loc.Tr("Satisfaction")} {pet.Satisfaction / 100f:0.00}%", 0));
            lines.Add(TooltipLine.Rule());
        }

        int maxDurability = def.Duration + (ext?.DurationBonus ?? 0);
        if (maxDurability > 1 && !def.IsChargeItem)
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

        AddRateLine(lines, 4534, 4535, "Hit Rate", ext?.BonusHitrate ?? 0);
        AddRateLine(lines, 4515, 4516, "Dodging Rate", ext?.BonusEvasionrate ?? 0);

        AddStatLine(lines, absSlot, def, ext, 4521, "Strength Bonus", 0, ext?.BonusStr ?? 0,
            (_, e) => e?.BonusStr ?? 0, 4);
        AddStatLine(lines, absSlot, def, ext, 4802, "Health Bonus", 0, ext?.BonusSta ?? 0,
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

        AddExtensionEffects(lines, ext);

        AddStatLine(lines, absSlot, def, ext, 4530, "Defense Ability (Dagger)", 0, ext?.BonusDaggerAc ?? 0,
            (_, e) => e?.BonusDaggerAc ?? 0, 4);
        AddStatLine(lines, absSlot, def, ext, 4612, "Defense Ability (Jamadar)", 0, ext?.BonusJamadarAc ?? 0,
            (_, e) => e?.BonusJamadarAc ?? 0, 4);
        AddStatLine(lines, absSlot, def, ext, 4532, "Defense Ability (Sword)", 0, ext?.BonusSwordAc ?? 0,
            (_, e) => e?.BonusSwordAc ?? 0, 4);
        AddStatLine(lines, absSlot, def, ext, 4529, "Defense Ability (Club)", 0, ext?.BonusClubAc ?? 0,
            (_, e) => e?.BonusClubAc ?? 0, 4);
        AddStatLine(lines, absSlot, def, ext, 4528, "Defense Ability (Axe)", 0, ext?.BonusAxeAc ?? 0,
            (_, e) => e?.BonusAxeAc ?? 0, 4);
        AddStatLine(lines, absSlot, def, ext, 4531, "Defense Ability (Spear)", 0, ext?.BonusSpearAc ?? 0,
            (_, e) => e?.BonusSpearAc ?? 0, 4);
        AddStatLine(lines, absSlot, def, ext, 4527, "Defense Ability (Arrow)", 0, ext?.BonusArrowAc ?? 0,
            (_, e) => e?.BonusArrowAc ?? 0, 4);

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

        if (ext?.Mirror is > 0 and var mirror)
            lines.Add(new TooltipLine(FormatIntText(4555, "Repel Physical Attack", mirror), TooltipColorEffect));
        AddSkillOptions(lines, item.ItemId);

        if (def.Weight > 0)
            lines.Add(new TooltipLine(FormatFloatText(4553, "Weight", def.Weight / 10.0f), 0));

        if (ItemData.IsSellable(item.ItemId))
            lines.Add(new TooltipLine(
                FormatMoneyText(4552, "Selling Price", ItemData.SellPrice(item.ItemId)), 0));

        int shownCount = ItemData.ShownCount(def, item);
        if (shownCount > 1)
            lines.Add(new TooltipLine($"{Localization.Loc.Tr("Count")} {shownCount}", 0));

        if (def.ReqCls > 0)
            lines.Add(new TooltipLine(" -" + ItemData.Text(EquipRules.ClassNameTextId(def.ReqCls), UnknownClassName),
                RequirementClassFailed(def.ReqCls) ? 13 : 0));
        int reqLevel = EquipRules.RequiredLevel(def, ext);
        if (reqLevel > 0)
        {
            int color = Sheet.Level > 0 && Sheet.Level < reqLevel ? 13 : 0;
            string text = def.ReqLevelMax > reqLevel && def.ReqLevelMax <= RequiredLevelRangeCap
                ? FormatRangeText(4558, "Required Level", reqLevel, def.ReqLevelMax)
                : FormatIntText(4541, "Required Level", reqLevel);
            lines.Add(new TooltipLine(text, color));
        }
        var (reqStr, reqSta, reqDex, reqInt, reqCha) = EquipRules.RequiredStats(def, ext);
        AddRequirement(lines, 4544, "Required Strength", reqStr, Sheet.Str);
        AddRequirement(lines, 4543, "Required Health", reqSta, Sheet.Sta);
        AddRequirement(lines, 4538, "Required Dexterity", reqDex, Sheet.Dex);
        AddRequirement(lines, 4540, "Required Intelligence", reqInt, Sheet.Intel);
        AddRequirement(lines, 4537, "Required Magic Power", reqCha, Sheet.Mag);

        AddCospreBonus(lines, item.ItemId);

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
            description[0] = DescriptionMark + description[0];
            description[^1] += DescriptionMark;
            foreach (var text in description)
                lines.Add(new TooltipLine(text, 12, HorizontalAlignment.Center));
        }

        if (CanCompare(absSlot, def))
            lines.Add(new TooltipLine(
                ItemData.Text(18500, "[Enable Comparison by pressing the 'Ctrl' Key.]"),
                9, HorizontalAlignment.Center));

        int tradeNote = TradeNoteText(item.ItemId, def);
        if (tradeNote != 0)
        {
            lines.Add(TooltipLine.Rule());
            lines.Add(new TooltipLine(ItemData.Text(tradeNote, ""), TooltipColorFailed, HorizontalAlignment.Right));
        }

        return lines;
    }

    private static string RarityMarker(int rarity) => rarity switch
    {
        < 0 => ItemData.Text(2402, "Regular item"),
        ItemData.Rarity.Regular => ItemData.Text(2402, "Regular item"),
        ItemData.Rarity.Magic => ItemData.Text(2404, "Magic item"),
        ItemData.Rarity.Rare => ItemData.Text(2403, "Rare item"),
        ItemData.Rarity.Craft => ItemData.Text(2401, "Craft item"),
        ItemData.Rarity.Unique => ItemData.Text(2405, "Unique item"),
        ItemData.Rarity.Upgrade => ItemData.Text(2406, "Upgrade item"),
        ItemData.Rarity.Event => ItemData.Text(2407, "An event item"),
        ItemData.Rarity.Reverse => ItemData.Text(2409, "Reverse item"),
        ItemData.Rarity.ReverseUnique => ItemData.Text(2408, "Reverse unique item"),
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

    private const int UnableToEquipText = 3036;
    private const int TooltipColorFailed = 13;
    private const int TooltipColorEffect = 8;
    private const int RequiredLevelRangeCap = 70;
    private const string DescriptionMark = "*";
    private const int SkillOptionText = 4594;
    private const int SkillOptionOnAttackText = 4592;
    private const int SkillOptionOnDamageText = 4593;
    private const int SkillOptionOnDamageTrigger = 13;
    private const int NoTradeText = 4580;
    private const int QuestItemTradeText = 4581;
    private const int NoTradeCountableText = 4703;
    private const int NoTradeCountable = 2;
    private const int CospreWeightDivisor = 10;

    private static void AddExtensionEffects(List<TooltipLine> lines, ItemData.Ext? ext)
    {
        if (ext == null) return;
        AddElementLine(lines, 4508, "Flame Damage", ext.FireDamage);
        AddElementLine(lines, 4509, "Glacier Damage", ext.IceDamage);
        AddElementLine(lines, 4510, "Lightning Damage", ext.LightningDamage);
        AddElementLine(lines, 4511, "Poison Damage", ext.PoisonDamage);
        AddElementLine(lines, 4512, "HP Recovery", ext.HpDrain);
        AddElementLine(lines, 4514, "MP Recovery", ext.MpDrain);
        AddElementLine(lines, 4513, "MP Damage", ext.MpDamage);
    }

    private static void AddSkillOptions(List<TooltipLine> lines, int itemId)
    {
        foreach (var option in ItemData.SkillOptions(itemId))
        {
            string trigger = option.Trigger == SkillOptionOnDamageTrigger
                ? ItemData.Text(SkillOptionOnDamageText, "Damage")
                : ItemData.Text(SkillOptionOnAttackText, "Attack");
            string skill = SkillData.Get(option.SkillId)?.Name ?? option.SkillId.ToString();
            string template = ItemData.Text(SkillOptionText, "Skill Option : %s has %d%% probability to cast %s");
            lines.Add(new TooltipLine(FillSkillOption(template, trigger, option.Chance, skill), TooltipColorEffect));
        }
    }

    private static string FillSkillOption(string template, string trigger, int chance, string skill)
    {
        int first = template.IndexOf("%s", System.StringComparison.Ordinal);
        if (first >= 0) template = template[..first] + trigger + template[(first + 2)..];
        template = template.Replace("%d", chance.ToString()).Replace("%%", "%");
        int second = template.IndexOf("%s", System.StringComparison.Ordinal);
        return second >= 0 ? template[..second] + skill + template[(second + 2)..] : template;
    }

    private static void AddCospreBonus(List<TooltipLine> lines, int itemId)
    {
        if (ItemData.CospreBonus(itemId) is not { } bonus) return;
        int before = lines.Count;
        AddCospreLine(lines, 9600, bonus.NoahPct);
        AddCospreLine(lines, 9601, bonus.XpPct);
        AddCospreLine(lines, 9602, bonus.Hp);
        AddCospreLine(lines, 9603, bonus.Ac);
        AddCospreLine(lines, 9604, bonus.ApPct);
        AddCospreLine(lines, 45002, bonus.Ap);
        AddCospreLine(lines, 4521, bonus.Str);
        AddCospreLine(lines, 4802, bonus.Sta);
        AddCospreLine(lines, 4518, bonus.Dex);
        AddCospreLine(lines, 4520, bonus.Int);
        AddCospreLine(lines, 4517, bonus.Cha);
        AddCospreLine(lines, CospreDamageToText(bonus.ApClass), bonus.ApClassPct);
        AddCospreLine(lines, CospreDamageFromText(bonus.AcClass), bonus.AcClassPct);
        AddCospreLine(lines, 4610, bonus.MaxWeight / CospreWeightDivisor);
        AddCospreLine(lines, 4611, bonus.Np);
        if (lines.Count > before) lines.Insert(before, TooltipLine.Rule());
    }

    private static void AddCospreLine(List<TooltipLine> lines, int textId, int value)
    {
        if (textId == 0 || value == 0) return;
        lines.Add(new TooltipLine(FormatIntText(textId, "", value), TooltipColorEffect));
    }

    private static int CospreDamageToText(int classCode) => classCode switch
    {
        1 => 9605, 2 => 9606, 3 => 9607, 4 => 9608, 5 => 9615,
        _ => 0,
    };

    private static int CospreDamageFromText(int classCode) => classCode switch
    {
        1 => 9609, 2 => 9610, 3 => 9611, 4 => 9612, 7 => 9614,
        _ => 0,
    };

    private static int TradeNoteText(int itemId, ItemData.Item def) =>
        ItemData.IsNoTradeId(itemId) ? NoTradeText
        : def.Race == ItemData.QuestItemRace ? QuestItemTradeText
        : def.Countable == NoTradeCountable ? NoTradeCountableText
        : 0;

    private bool CanCompare(int absSlot, ItemData.Item def)
    {
        if (absSlot < GridStart) return false;
        int eq = Inv.ResolveEquipDest(def.Slot, ItemData.EquipSlotFor(def));
        return eq >= 0 && eq < Inv.Length && !Inv[eq].IsEmpty;
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
            <= 89 => 4505,
            <= 110 => 4502,
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
        bool failing = mine > 0 && mine < value;
        if (failing) text = $"{text} ({mine})";
        lines.Add(new TooltipLine(text, failing ? TooltipColorFailed : 0));
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

    private const string UnknownClassName = "Unknown Class";

    private bool RequirementClassFailed(int reqCls) =>
        reqCls > 0 && _selfClass > 0 && !EquipRules.ClassAllows(_selfClass, reqCls);

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

    private static void AddRateLine(List<TooltipLine> lines, int raiseTextId, int lowerTextId, string what, int percent)
    {
        if (percent == 0) return;
        lines.Add(percent > 0
            ? new TooltipLine(FormatIntText(raiseTextId, $"Increase {what} by %d%%", percent), 4)
            : new TooltipLine(FormatIntText(lowerTextId, $"Decrease {what} by %d%%", -percent), 4));
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

    private static bool IsWeaponItem(ItemData.Item def) => def.Slot is 0 or 1 or 3 or 4 && def.Kind != 60;

    private static Color TooltipColor(int idx) => Config.TooltipColor(idx);
}
