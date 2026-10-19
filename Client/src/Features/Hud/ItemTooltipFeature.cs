using System.Globalization;
using System.Text;
using Godot;

namespace LibreKO.Features.Hud;

internal sealed partial class ItemTooltipFeature : IFeature, IHudTickFeature, IItemTooltips
{
    private readonly IWorldContext context;
    private readonly PetSession pet;
    private readonly SelfSession self;
    private readonly ZoneAbilitySession zoneAbility;

    public ItemTooltipFeature(IWorldContext context, PetSession pet, SelfSession self, ZoneAbilitySession zoneAbility)
    {
        this.context = context;
        this.pet = pet;
        this.self = self;
        this.zoneAbility = zoneAbility;
    }

    void IItemTooltips.Show(int absSlot, ItemSlot item, string note, ShopPricing? shop) => Show(absSlot, item, note, shop);
    void IItemTooltips.Hide() => Hide();
    void IItemTooltips.Hover(WarehouseCell cell) => HoverWarehouse(cell);
    void IItemTooltips.ShowItem(int itemId) => ShowItem(itemId);
    Control IItemTooltips.Hover(Control row, int itemId) => QuestRows.Hover(this, row, itemId);

    public void Dispose()
    {
    }

    internal TipView TipPanel { get; private set; } = null!;
    private ItemCell? hoverCell;

    private static int TooltipFontHeight => Config.TooltipHeight;

    private static bool TooltipBold => Config.TooltipBold;

    private static bool TooltipBack => Config.TooltipBack;

    internal readonly struct TooltipLine
    {
        public const int NoGauge = -1;

        public readonly string Text;
        public readonly Color Tint;
        public readonly HorizontalAlignment Align;
        public readonly bool Divider;
        public readonly int Gauge;

        public TooltipLine(string text, int color, HorizontalAlignment align = HorizontalAlignment.Left)
            : this(text, Config.TooltipColor(color), align)
        {
        }

        public TooltipLine(string text, Color tint, HorizontalAlignment align = HorizontalAlignment.Left,
            bool divider = false, int gauge = NoGauge)
        {
            Text = text; Tint = tint; Align = align; Divider = divider; Gauge = gauge;
        }

        public static TooltipLine Rule() => new("", Colors.White, HorizontalAlignment.Left, divider: true);

        public static TooltipLine Gauged(string text, Color tint, int percent) =>
            new(text, tint, HorizontalAlignment.Left, gauge: percent);
    }

    internal void RefreshHoverTooltip()
    {
        if (hoverCell == null) return;
        if (!context.MainWindows.CharTabOpen() || hoverCell.Current.IsEmpty)
            Hide();
        else
            Show(hoverCell.Slot, hoverCell.Current, context.BagCompanions.Current?.Note(hoverCell.Slot) ?? "");
    }
    private const int WornDurabilityPercent = 25;
    private const int TooltipColorPlain = 0;
    private const int TooltipColorBetter = 1;
    private const int TooltipColorWorse = 2;
    private const int TooltipColorResist = 3;
    private const int TooltipColorElement = 4;
    private const int TooltipColorMerchant = 4;
    private const int TooltipColorSkillOption = 5;
    private const int TooltipColorCospre = 6;
    private const int TooltipColorSetName = 7;
    private const int TooltipColorSetWorn = 8;
    private const int TooltipColorGmId = 8;
    private const int TooltipColorSealed = 9;
    private const int TooltipColorBind = 10;
    private const int TooltipColorHint = 10;
    private const int TooltipColorStones = 11;
    private const int TooltipColorDescription = 12;

    private const int TooltipNameFontExtra = 3;
    private const int TooltipSmallFontDrop = 1;
    private const float TooltipIconSide = 45f;
    private const float TooltipNameWrapWidth = 230f;
    private const float TooltipHeadGap = 6f;
    private const float TooltipGaugeWidth = 45f;
    private const float TooltipGaugeHeight = 7f;
    private const float TooltipCursorGap = 16f;
    private const float TooltipScreenMargin = 8f;
    internal const float TooltipPanelGap = 4f;
    private const int NoTooltipSlot = int.MinValue;

    private const string SelfNameToken = "<selfname>";
    private const int NowEquippedText = 10313;
    private const int CompareHintText = 18500;
    private const string CompareHintKey = "'Ctrl'";
    private const int HotkeyText = 4598;
    private const int HotkeySkillFloor = 490000;
    private const int PetNameText = 4586;
    private const int PetLevelText = 4587;
    private const int PetExpText = 4588;
    private const int PetSatisfactionText = 4589;
    private const float PetPercentScale = 100f;
    private const int CospreMarkerText = 2410;
    private const int ReduceText = 4554;
    private const int NoLevelLineMax = 100;
    private const int LevelRangeMaxBelow = 71;
    private const int MonsterDamageClass = 5;
    private const int MonsterDamageTakenText = 9616;
    private const float WeightScale = 0.1f;
    private const float SpeedPercentBase = 100f;
    private const float SpeedFastestMax = 89f;
    private const float SpeedFastMin = 90f;
    private const float SpeedFastMax = 110f;
    private const float SpeedNormalMin = 111f;
    private const float SpeedNormalMax = 130f;
    private const float SpeedSlowMin = 131f;
    private const float SpeedSlowMax = 150f;
    private const int SpeedFastestText = 4505;
    private const int SpeedFastText = 4502;
    private const int SpeedNormalText = 4503;
    private const int SpeedSlowText = 4504;
    private const int SpeedSlowestText = 4506;
    private static readonly Color SetNameColor = Color.Color8(210, 0, 0);
    private static readonly Color SetPieceMissingColor = Color.Color8(128, 128, 128);
    private static readonly Color GradeUniqueColor = Color.Color8(220, 199, 124);
    private static readonly Color GradeReverseUniqueColor = Color.Color8(255, 173, 77);
    private static readonly Color GradeUpgradeColor = Color.Color8(127, 255, 0);
    private static readonly Color GradeReverseColor = Color.Color8(255, 131, 166);
    private static readonly Color TooltipEquippedColor = Color.Color8(192, 192, 192);
    private static readonly Color DurabilityGaugeFill = Color.Color8(255, 138, 0);
    private static readonly Color DurabilityGaugeBack = new(0.94f, 0.98f, 1f, 0.27f);

    private CanvasLayer tipLayer = null!;
    internal TipView TipCompare { get; private set; } = null!;
    private int tipSlot = NoTooltipSlot;
    private ItemSlot tip;
    private string tipNote = "";
    private bool tipCompareHeld;
    private ShopPricing? tipShop;
    internal bool TooltipCompareForced { get; set; }

    private bool CompareKeyHeld() => TooltipCompareForced || KeyBinds.Held(KeyAction.CompareItems);

    public void HudTick(double delta, double nowSec) => UpdateInventoryTooltip();

    public void Build()
    {
        tipLayer = new CanvasLayer { Layer = UiLayers.Tooltips };
        context.Root.AddChild(tipLayer);
        TipPanel = new TipView("");
        tipLayer.AddChild(TipPanel);
        TipCompare = new TipView(ItemData.Text(NowEquippedText, "Now equipped"));
        tipLayer.AddChild(TipCompare);
    }

    internal void InventoryHover(ItemCell cell, bool entered)
    {
        if (!entered)
        {
            if (hoverCell == cell) Hide();
            return;
        }
        if (cell.Current.IsEmpty) return;
        hoverCell = cell;
        Show(cell.Slot, cell.Current, context.BagCompanions.Current?.Note(cell.Slot) ?? "");
    }

    public bool Visible => TipPanel is { Visible: true };

    internal void Show(int absSlot, ItemSlot item, string note = "", ShopPricing? shop = null)
    {
        if (TipPanel == null) return;
        tipSlot = absSlot;
        tip = item;
        tipNote = note;
        tipShop = shop;
        tipCompareHeld = CompareKeyHeld();

        int wornSlot = CompareTarget(absSlot, item);
        bool comparing = wornSlot != ItemTooltipRules.NoWornSlot && tipCompareHeld;
        var sheet = BuildTooltipSheet(absSlot, item, comparing ? self.Inventory[wornSlot] : null,
            wornSlot != ItemTooltipRules.NoWornSlot);
        if (note.Length > 0)
        {
            sheet.Lines.Add(TooltipLine.Rule());
            sheet.Lines.Add(new TooltipLine(note, TooltipColorMerchant));
        }
        TipPanel.Show(sheet);

        if (comparing)
        {
            var worn = BuildTooltipSheet(wornSlot, self.Inventory[wornSlot], null, false);
            worn.Equipped = true;
            TipCompare.Show(worn);
        }
        else TipCompare.Close();
        UpdateInventoryTooltip();
    }

    private int CompareTarget(int absSlot, ItemSlot item)
    {
        if (item.IsEmpty || IsWornOrCostumeSlot(absSlot)) return ItemTooltipRules.NoWornSlot;
        if (ItemData.Get(item.ItemId) is not { } def) return ItemTooltipRules.NoWornSlot;
        return ItemTooltipRules.CompareSlot(def.Slot, slot =>
            slot < 0 || slot >= self.Inventory.Length || self.Inventory[slot].IsEmpty ? null : ItemData.Get(self.Inventory[slot].ItemId)?.Kind ?? 0);
    }

    private static bool IsWornOrCostumeSlot(int absSlot) =>
        absSlot is >= 0 and < InventoryConstants.SlotMax
        || absSlot is >= InventoryConstants.CospreStart and < InventoryConstants.BagSlotStart;

    internal void Hide()
    {
        hoverCell = null;
        tipSlot = NoTooltipSlot;
        tipShop = null;
        TipPanel?.Close();
        TipCompare?.Close();
    }

    internal void UpdateInventoryTooltip()
    {
        if (TipPanel == null || !TipPanel.Visible) return;
        if (tipSlot != NoTooltipSlot && CompareKeyHeld() != tipCompareHeld)
        {
            Show(tipSlot, tip, tipNote, tipShop);
            return;
        }
        var vp = context.Root.GetViewport();
        if (vp == null) return;
        var screen = vp.GetVisibleRect().Size;
        var size = PanelSize(TipPanel);
        var main = TooltipPlacement.AroundCursor(vp.GetMousePosition(), size, screen,
            TooltipCursorGap, TooltipScreenMargin);
        TipPanel.Position = main;
        if (TipCompare.Visible)
            TipCompare.Position = TooltipPlacement.BesidePanel(main, size, PanelSize(TipCompare), screen,
                TooltipPanelGap, TooltipScreenMargin);
    }

    private static Vector2 PanelSize(Control panel)
    {
        var size = panel.Size;
        return size.X <= 1 || size.Y <= 1 ? panel.GetCombinedMinimumSize() : size;
    }

    private TooltipSheet BuildTooltipSheet(int absSlot, ItemSlot item, ItemSlot? compareWith, bool hasCounterpart)
    {
        var sheet = new TooltipSheet { ItemId = item.ItemId, Count = ItemData.Get(item.ItemId) is { } d ? ItemData.ShownCount(d, item) : 1 };
        var lines = sheet.Lines;
        var def = ItemData.Get(item.ItemId);
        if (def == null)
        {
            sheet.Name = $"Item {item.ItemId}";
            sheet.NameColor = Config.TooltipColor(TooltipColorPlain);
            return sheet;
        }

        var ext = ItemData.ExtFor(item.ItemId);
        int rarity = ext?.MagicOrRare ?? -1;
        var nameColor = TooltipNameColor(rarity, def.Race);
        sheet.Name = ItemData.DisplayName(item.ItemId).Replace(SelfNameToken, self.LastEnter.Name ?? "");
        sheet.NameColor = nameColor;

        if (context.Self.IsGm)
            lines.Add(new TooltipLine($"Item ID: {item.ItemId}", TooltipColorGmId));

        string marker = RarityMarker(rarity);
        if (marker.Length > 0)
            lines.Add(new TooltipLine(marker, nameColor, HorizontalAlignment.Right));

        string itemClass = ClassName(def.Kind);
        if (itemClass.Length > 0)
        {
            bool unusable = self.Class > 0
                && (EquipRules.ForbidsKind(self.Class, def.Kind) || RequirementClassFailed(def.ReqCls));
            lines.Add(unusable
                ? new TooltipLine($"{itemClass} {ItemData.Text(UnableToEquipText, "(Unable to equip)")}",
                    TooltipColorWorse, HorizontalAlignment.Right)
                : new TooltipLine(itemClass, TooltipColorPlain, HorizontalAlignment.Right));
        }

        int maxDurability = ItemData.MaxDurability(def, ext);
        if (maxDurability > 1 && !def.IsChargeItem)
        {
            int current = item.Durability;
            int percent = current * 100 / maxDurability;
            if (percent == 0 && current > 0) percent = 1;
            lines.Add(TooltipLine.Gauged(
                $"{StatLabel(4614, "Durability")} : {current} / {maxDurability} ({percent}%)",
                Config.TooltipColor(percent < WornDurabilityPercent ? TooltipColorWorse : TooltipColorPlain),
                Mathf.Clamp(percent, 0, 100)));
        }

        lines.Add(TooltipLine.Rule());

        if (item.IsLinked && def.Kind == CharacterSeal.RingKind && pet.Items.TryGetValue(item.UniqueId, out var ring))
        {
            lines.Add(new TooltipLine(ItemData.Text(PetNameText, "Name : %s").Replace("%s", ring.Name), TooltipColorPlain));
            string level = FormatIntText(PetLevelText, "LV", ring.Level);
            lines.Add(new TooltipLine(ring.Tail != 0 ? $"{level} / {ring.Tail}" : level, TooltipColorPlain));
            lines.Add(new TooltipLine(FormatWholeText(PetExpText, "EXP", ring.ExpPercent / PetPercentScale) + "%", TooltipColorPlain));
            lines.Add(new TooltipLine($"{ItemData.Text(CharacterSeal.RaceLabelText, "Race :")} {StarterStats.RaceName(ring.Satisfaction)}", TooltipColorPlain));
            lines.Add(new TooltipLine($"{ItemData.Text(CharacterSeal.JobLabelText, "Job :")} {CharacterSealFeature.SealedClassName(ring.Attack)}", TooltipColorPlain));
            lines.Add(TooltipLine.Rule());
        }
        else if (item.IsLinked && pet.Items.TryGetValue(item.UniqueId, out var petItemInfo))
        {
            lines.Add(new TooltipLine(ItemData.Text(PetNameText, "Name : %s").Replace("%s", petItemInfo.Name), TooltipColorPlain));
            lines.Add(new TooltipLine(FormatIntText(PetLevelText, "LV", petItemInfo.Level), TooltipColorPlain));
            lines.Add(new TooltipLine(FormatWholeText(PetExpText, "EXP", petItemInfo.ExpPercent / PetPercentScale), TooltipColorPlain));
            lines.Add(new TooltipLine(FormatWholeText(PetSatisfactionText, "Satisfaction rate",
                petItemInfo.Satisfaction / PetPercentScale), TooltipColorPlain));
            lines.Add(TooltipLine.Rule());
        }

        if (tipShop is { } shop && absSlot < 0)
        {
            long price = ShopTooltipPrice.Purchase(ItemData.BuyPrice(item.ItemId), def.SaleType,
                zoneAbility.CurrentZoneAbility.Tariff, shop.LoyaltyShop);
            lines.Add(new TooltipLine(FormatMoneyText(ShopTooltipPrice.PurchasingPriceText, "Purchasing Price", price),
                price > shop.Wallet ? TooltipColorWorse : TooltipColorPlain));
        }
        else if (ItemData.IsSellable(item.ItemId))
            lines.Add(new TooltipLine(
                FormatMoneyText(ShopTooltipPrice.SellingPriceText, "Selling Price", ItemData.SellPrice(item.ItemId)), TooltipColorPlain));

        var other = compareWith is { IsEmpty: false } worn ? ItemData.Get(worn.ItemId) : null;
        var otherExt = other != null ? ItemData.ExtFor(compareWith!.Value.ItemId) : null;
        if (IsWeapon(def))
        {
            AddAttackLine(lines, def, ext, other, otherExt);
            AddSpeedLine(lines, def, ext, other, otherExt);
            if (def.Range > 0)
                lines.Add(new TooltipLine(FormatFloatText(4507, "Effective Range", def.Range / Coord.TenthsPerUnit), TooltipColorPlain));
        }
        else
            AddDefenseLine(lines, def, ext, other, otherExt);

        AddRateLine(lines, 4534, 4535, "Hit Rate", ext?.BonusHitrate ?? 0);
        AddRateLine(lines, 4515, 4516, "Dodging Rate", ext?.BonusEvasionrate ?? 0);

        AddBonusLine(lines, 4521, "Strength Bonus", ext?.BonusStr ?? 0, TooltipColorBetter);
        AddBonusLine(lines, 4802, "Health Bonus", ext?.BonusSta ?? 0, TooltipColorBetter);
        AddBonusLine(lines, 4518, "Dexterity Bonus", ext?.BonusDex ?? 0, TooltipColorBetter);
        AddBonusLine(lines, 4520, "Intelligence Bonus", ext?.BonusInt ?? 0, TooltipColorBetter);
        AddBonusLine(lines, 4517, "Magic Power Bonus", ext?.BonusCha ?? 0, TooltipColorBetter);
        AddBonusLine(lines, 4519, "HP Bonus", ext?.BonusMaxHp ?? 0, TooltipColorBetter);
        AddBonusLine(lines, 4522, "MP Bonus", ext?.BonusMaxMp ?? 0, TooltipColorBetter);

        AddExtensionEffects(lines, ext);

        AddBonusLine(lines, 4530, "Defense Ability (Dagger)", ext?.BonusDaggerAc ?? 0, TooltipColorResist);
        AddBonusLine(lines, 4612, "Defense Ability (Jamadar)", ext?.BonusJamadarAc ?? 0, TooltipColorResist);
        AddBonusLine(lines, 4532, "Defense Ability (Sword)", ext?.BonusSwordAc ?? 0, TooltipColorResist);
        AddBonusLine(lines, 4529, "Defense Ability (Club)", ext?.BonusClubAc ?? 0, TooltipColorResist);
        AddBonusLine(lines, 4528, "Defense Ability (Axe)", ext?.BonusAxeAc ?? 0, TooltipColorResist);
        AddBonusLine(lines, 4531, "Defense Ability (Spear)", ext?.BonusSpearAc ?? 0, TooltipColorResist);
        AddBonusLine(lines, 4527, "Defense Ability (Arrow)", ext?.BonusArrowAc ?? 0, TooltipColorResist);

        AddBonusLine(lines, 4548, "Resistance to Flame", ext?.BonusFireR ?? 0, TooltipColorResist);
        AddBonusLine(lines, 4549, "Resistance to Glacier", ext?.BonusColdR ?? 0, TooltipColorResist);
        AddBonusLine(lines, 4547, "Resistance to Lightning", ext?.BonusLightningR ?? 0, TooltipColorResist);
        AddBonusLine(lines, 4550, "Resistance to Magic", ext?.BonusMagicR ?? 0, TooltipColorResist);
        AddBonusLine(lines, 4551, "Resistance to Poison", ext?.BonusPoisonR ?? 0, TooltipColorResist);
        AddBonusLine(lines, 4546, "Resistance to Curse", ext?.BonusCurseR ?? 0, TooltipColorResist);

        if (ext?.Mirror is > 0 and var mirror)
            lines.Add(new TooltipLine(FormatIntText(4555, "Repel Physical Attack", mirror), TooltipColorResist));
        AddSkillOptions(lines, item.ItemId);

        AddRequirements(lines, def, ext, rarity);
        AddSetBlock(lines, absSlot, def);
        AddCospreBonus(lines, item.ItemId);
        AddExpiry(lines, item.ExpiresAt);
        AddGradeAndNotes(lines, item, def, ext, rarity);

        if (def.Effect1 >= HotkeySkillFloor)
            lines.Add(new TooltipLine(ItemData.Text(HotkeyText, "You can register it on Hot Key Window and use."),
                TooltipColorHint));
        if (hasCounterpart)
            lines.Add(new TooltipLine(CompareHint(), TooltipColorHint, HorizontalAlignment.Center));

        if (!ItemTooltipRules.IsSetRace(def.Race))
        {
            var description = new List<string>(ItemText.DescriptionLines(def.Desc));
            if (description.Count > 0)
            {
                lines.Add(TooltipLine.Rule());
                description[0] = DescriptionMark + description[0];
                description[^1] += DescriptionMark;
                foreach (var text in description)
                    lines.Add(new TooltipLine(text, TooltipColorDescription, HorizontalAlignment.Center));
            }
        }

        int tradeNote = ItemTooltipRules.TradeNote(item.ItemId, def.Race);
        if (tradeNote != 0)
        {
            lines.Add(TooltipLine.Rule());
            lines.Add(new TooltipLine(ItemData.Text(tradeNote, ""), TooltipColorWorse, HorizontalAlignment.Right));
        }

        return sheet;
    }

    private static string CompareHint()
    {
        string hint = ItemData.Text(CompareHintText, "[Enable Comparison by pressing the 'Ctrl' Key.]");
        string key = KeyBinds.Get(KeyAction.CompareItems).Text;
        return key == "Ctrl" ? hint : hint.Replace(CompareHintKey, $"'{key}'");
    }

    private static Color TooltipNameColor(int rarity, int race) =>
        ItemTooltipRules.IsSetRace(race)
        ? rarity == ItemData.Rarity.Reverse ? Config.TooltipColor(Config.RarityNameReverse) : SetNameColor
        : Config.TooltipColor(ItemGrade.ColorIndex(rarity));

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
        ItemData.Rarity.Cospre => ItemData.Text(CospreMarkerText, ""),
        ItemData.Rarity.Reverse => ItemData.Text(2409, "Reverse item"),
        ItemData.Rarity.ReverseUnique => ItemData.Text(2408, "Reverse unique item"),
        _ => "",
    };

    private static string ClassName(int kind)
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

    private static int AttackOf(ItemData.Item def, ItemData.Ext? ext) =>
        (def.Damage > 0 ? def.Damage : def.Value) + (ext?.BonusDamage ?? 0);

    private static int DefenseOf(ItemData.Item def, ItemData.Ext? ext) =>
        (def.Ac > 0 ? def.Ac : def.Value) + (ext?.BonusAc ?? 0);

    private static void AddAttackLine(List<TooltipLine> lines, ItemData.Item def, ItemData.Ext? ext,
        ItemData.Item? other, ItemData.Ext? otherExt)
    {
        int attack = AttackOf(def, ext);
        if (attack == 0) return;
        string text = $"{StatLabel(4525, "Attack Power")} : {attack}";
        if (other == null) { lines.Add(new TooltipLine(text, TooltipColorPlain)); return; }
        int delta = attack - AttackOf(other, otherExt);
        lines.Add(delta > 0 ? new TooltipLine($"{text}(+{delta})", TooltipColorBetter)
            : new TooltipLine(text, delta < 0 ? TooltipColorWorse : TooltipColorPlain));
    }

    private static void AddDefenseLine(List<TooltipLine> lines, ItemData.Item def, ItemData.Ext? ext,
        ItemData.Item? other, ItemData.Ext? otherExt)
    {
        int defense = DefenseOf(def, ext);
        if (defense == 0) return;
        string text = $"{StatLabel(4526, "Defense Ability")} : {defense}";
        if (other == null) { lines.Add(new TooltipLine(text, TooltipColorPlain)); return; }
        int delta = defense - DefenseOf(other, otherExt);
        lines.Add(delta switch
        {
            > 0 => new TooltipLine($"{text}(+{delta})", TooltipColorBetter),
            < 0 => new TooltipLine($"{text}({delta})", TooltipColorWorse),
            _ => new TooltipLine(text, TooltipColorPlain),
        });
    }

    private static float SpeedOf(ItemData.Item def, ItemData.Ext? ext)
    {
        float percent = ext is { AttackIntervalPercent: > 0 } e ? e.AttackIntervalPercent : SpeedPercentBase;
        return percent / SpeedPercentBase * def.Delay;
    }

    private static void AddSpeedLine(List<TooltipLine> lines, ItemData.Item def, ItemData.Ext? ext,
        ItemData.Item? other, ItemData.Ext? otherExt)
    {
        float speed = SpeedOf(def, ext);
        if (speed == 0f) return;
        int color = TooltipColorPlain;
        if (other != null)
        {
            float worn = SpeedOf(other, otherExt);
            color = worn > speed ? TooltipColorBetter : speed > worn ? TooltipColorWorse : TooltipColorPlain;
        }
        lines.Add(new TooltipLine(ItemData.Text(SpeedBandText(speed), "Attack Speed"), color));
    }

    private static int SpeedBandText(float speed) =>
        speed is >= 0f and <= SpeedFastestMax ? SpeedFastestText
        : speed is >= SpeedFastMin and <= SpeedFastMax ? SpeedFastText
        : speed is >= SpeedNormalMin and <= SpeedNormalMax ? SpeedNormalText
        : speed is >= SpeedSlowMin and <= SpeedSlowMax ? SpeedSlowText
        : SpeedSlowestText;

    private static void AddBonusLine(List<TooltipLine> lines, int textId, string fallback, int value, int color)
    {
        if (value == 0) return;
        lines.Add(new TooltipLine($"{StatLabel(textId, fallback)} : {value}", color));
    }

    private const int UnableToEquipText = 3036;
    private const string DescriptionMark = "*";
    private const int SkillOptionText = 4594;
    private const int SkillOptionOnAttackText = 4592;
    private const int SkillOptionOnDamageText = 4593;
    private const int SkillOptionOnDamageTrigger = 13;
    private const int CospreWeightDivisor = 10;

    private static void AddExtensionEffects(List<TooltipLine> lines, ItemData.Ext? ext)
    {
        if (ext == null) return;
        AddBonusLine(lines, 4508, "Flame Damage", ext.FireDamage, TooltipColorElement);
        AddBonusLine(lines, 4509, "Glacier Damage", ext.IceDamage, TooltipColorElement);
        AddBonusLine(lines, 4510, "Lightning Damage", ext.LightningDamage, TooltipColorElement);
        AddBonusLine(lines, 4511, "Poison Damage", ext.PoisonDamage, TooltipColorElement);
        AddBonusLine(lines, 4512, "HP Recovery", ext.HpDrain, TooltipColorBetter);
        AddBonusLine(lines, 4514, "MP Recovery", ext.MpDrain, TooltipColorBetter);
        AddBonusLine(lines, 4513, "MP Damage", ext.MpDamage, TooltipColorBetter);
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
            lines.Add(new TooltipLine(FillSkillOption(template, trigger, option.Chance, skill), TooltipColorSkillOption));
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

    private void AddRequirements(List<TooltipLine> lines, ItemData.Item def, ItemData.Ext? ext, int rarity)
    {
        if (def.ReqCls > 0)
            lines.Add(new TooltipLine(" -" + ItemData.Text(EquipRules.ClassNameTextId(def.ReqCls), UnknownClassName),
                TooltipColorPlain));
        if (def.Weight > 0)
            lines.Add(new TooltipLine(FormatFloatText(4553, "Weight", def.Weight * WeightScale), TooltipColorPlain));

        if (def.ReqLevelMax != NoLevelLineMax && def.ReqLevel > 0)
        {
            int bonus = ext?.ReqLevelBonus ?? 0;
            int low = def.ReqLevel + bonus;
            int mine = self.Sheet.Level;
            if (def.ReqLevelMax < LevelRangeMaxBelow)
            {
                int high = def.ReqLevelMax + bonus;
                bool outside = mine > 0 && (mine < low || mine > high);
                string text = FormatRangeText(4558, "Required Level", low, high);
                lines.Add(new TooltipLine(outside ? $"{text}({mine})" : text, outside ? TooltipColorWorse : TooltipColorPlain));
            }
            else if (low != 1)
            {
                bool below = mine > 0 && mine < low;
                string text = FormatIntText(4541, "Required Level", low);
                lines.Add(new TooltipLine(below ? $"{text}({mine})" : text, below ? TooltipColorWorse : TooltipColorPlain));
            }
        }

        string reduce = ReducedRequirements(ext, rarity) ? ItemData.Text(ReduceText, "(Reduce)") : "";
        var (reqStr, reqSta, reqDex, reqInt, reqCha) = EquipRules.RequiredStats(def, ext);
        AddRequirement(lines, 4544, "Required Strength", reqStr, self.Sheet.Str, reduce);
        AddRequirement(lines, 4543, "Required Health", reqSta, self.Sheet.Sta, reduce);
        AddRequirement(lines, 4538, "Required Dexterity", reqDex, self.Sheet.Dex, reduce);
        AddRequirement(lines, 4540, "Required Intelligence", reqInt, self.Sheet.Intel, reduce);
        AddRequirement(lines, 4537, "Required Magic Power", reqCha, self.Sheet.Mag, reduce);
    }

    private static bool ReducedRequirements(ItemData.Ext? ext, int rarity) =>
        ext != null
        && rarity is ItemData.Rarity.Unique or ItemData.Rarity.Upgrade or ItemData.Rarity.Reverse or ItemData.Rarity.ReverseUnique
        && (ext.ReqStrBonus < 0 || ext.ReqStaBonus < 0 || ext.ReqDexBonus < 0 || ext.ReqIntBonus < 0 || ext.ReqChaBonus < 0);

    private static void AddRequirement(List<TooltipLine> lines, int textId, string fallback, int value, int mine,
        string reduce)
    {
        if (value <= 0) return;
        string text = ItemData.Text(textId, $"{fallback} : %d %s")
            .Replace("%d", value.ToString()).Replace("%s", reduce).TrimEnd();
        bool failing = mine > 0 && mine < value;
        lines.Add(failing ? new TooltipLine($"{text}({mine})", TooltipColorWorse) : new TooltipLine(text, TooltipColorPlain));
    }

    private void AddSetBlock(List<TooltipLine> lines, int absSlot, ItemData.Item def)
    {
        if (!ItemTooltipRules.IsSetRace(def.Race) || absSlot is < 0 or >= InventoryConstants.SlotMax) return;
        var wornSlots = new List<int>();
        int wornPieces = 0;
        for (int slot = 0; slot < InventoryConstants.SlotMax; slot++)
        {
            if (self.Inventory[slot].IsEmpty || ItemData.Get(self.Inventory[slot].ItemId) is not { } worn || worn.Race != def.Race) continue;
            wornSlots.Add(slot);
            wornPieces |= ItemTooltipRules.PieceBit(worn.Slot);
        }
        if (ItemData.SetRow(ItemTooltipRules.SetKey(def.Race, wornSlots)) is not { } row) return;

        lines.Add(TooltipLine.Rule());
        if (row.ApClass != MonsterDamageClass && row.Name.Length > 0)
            lines.Add(new TooltipLine(row.Name, TooltipColorSetName));
        foreach (var piece in ItemData.SetPieces(def.Race))
        {
            bool wearing = (wornPieces & ItemTooltipRules.PieceBit(piece.SlotType)) != 0;
            lines.Add(wearing
                ? new TooltipLine(piece.Name, TooltipColorSetWorn)
                : new TooltipLine(piece.Name, SetPieceMissingColor));
        }
        AddSetLine(lines, 4519, row.Hp, TooltipColorBetter);
        AddSetLine(lines, 4522, row.Mp, TooltipColorBetter);
        AddSetLine(lines, 4521, row.Str, TooltipColorBetter);
        AddSetLine(lines, 4802, row.Sta, TooltipColorBetter);
        AddSetLine(lines, 4518, row.Dex, TooltipColorBetter);
        AddSetLine(lines, 4520, row.Int, TooltipColorBetter);
        AddSetLine(lines, 4517, row.Cha, TooltipColorBetter);
        AddSetLine(lines, 4610, (int)(row.MaxWeight * WeightScale), TooltipColorBetter);
        AddSetLine(lines, 4611, row.Np, TooltipColorBetter);
        AddSetLine(lines, 4526, row.Ac, TooltipColorBetter);
        AddSetLine(lines, 4548, row.FireR, TooltipColorResist);
        AddSetLine(lines, 4549, row.ColdR, TooltipColorResist);
        AddSetLine(lines, 4547, row.LightningR, TooltipColorResist);
        AddSetLine(lines, 4550, row.MagicR, TooltipColorResist);
        AddSetLine(lines, 4551, row.PoisonR, TooltipColorResist);
        AddSetLine(lines, 4546, row.CurseR, TooltipColorResist);
        if (row.AcClass == MonsterDamageClass)
            AddSetLine(lines, MonsterDamageTakenText, row.AcClassPct, TooltipColorResist);
    }

    private static void AddSetLine(List<TooltipLine> lines, int textId, int value, int color)
    {
        if (value == 0) return;
        lines.Add(new TooltipLine(FormatIntText(textId, "", value), color));
    }

    private void AddGradeAndNotes(List<TooltipLine> lines, ItemSlot item, ItemData.Item def, ItemData.Ext? ext, int rarity)
    {
        var grade = ItemTooltipRules.Grade(rarity, ItemData.GradeClass(def, ext), item.ItemId, ext?.Id ?? 0);
        var notes = new List<ItemTooltipRules.Note>(ItemTooltipRules.Notes((byte)ItemData.ShownFlag(item), def.Race));
        if (grade == null && notes.Count == 0) return;
        lines.Add(TooltipLine.Rule());
        if (grade is { } g)
            foreach (string part in ItemTooltipRules.SplitGradeText(ItemData.Text(g.TextId, "")))
                lines.Add(new TooltipLine(part, GradeColor(g.Tint), HorizontalAlignment.Center));
        foreach (var note in notes)
        {
            string text = ItemData.Text(note.TextId, "");
            if (note.WithStones) text = text.Replace("%d", ItemData.UnbindStones(def, ext).ToString());
            lines.Add(new TooltipLine(text, NoteColor(note.Tint), HorizontalAlignment.Center));
        }
    }

    private static Color GradeColor(ItemTooltipRules.GradeTint tint) => tint switch
    {
        ItemTooltipRules.GradeTint.ReverseUnique => GradeReverseUniqueColor,
        ItemTooltipRules.GradeTint.Upgrade => GradeUpgradeColor,
        ItemTooltipRules.GradeTint.Reverse => GradeReverseColor,
        _ => GradeUniqueColor,
    };

    private static Color NoteColor(ItemTooltipRules.NoteTint tint) => Config.TooltipColor(tint switch
    {
        ItemTooltipRules.NoteTint.Stones => TooltipColorStones,
        ItemTooltipRules.NoteTint.Sealed or ItemTooltipRules.NoteTint.TradeOnce => TooltipColorSealed,
        ItemTooltipRules.NoteTint.TradeDone => TooltipColorWorse,
        _ => TooltipColorBind,
    });

    private static void AddExpiry(List<TooltipLine> lines, int expiresAt)
    {
        if (expiresAt <= 0) return;
        lines.Add(new TooltipLine(UiKit.ExpirationText(expiresAt), TooltipColorPlain));
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
        AddCospreLine(lines, Domain.CospreDamageText.Dealt(bonus.ApClass), bonus.ApClassPct);
        AddCospreLine(lines, Domain.CospreDamageText.Received(bonus.AcClass), bonus.AcClassPct);
        AddCospreLine(lines, 4610, bonus.MaxWeight / CospreWeightDivisor);
        AddCospreLine(lines, 4611, bonus.Np);
        if (lines.Count > before) lines.Insert(before, TooltipLine.Rule());
    }

    private static void AddCospreLine(List<TooltipLine> lines, int textId, int value)
    {
        if (textId == 0 || value == 0) return;
        lines.Add(new TooltipLine(FormatIntText(textId, "", value), TooltipColorCospre));
    }

    private const string UnknownClassName = "Unknown Class";

    private bool RequirementClassFailed(int reqCls) =>
        reqCls > 0 && self.Class > 0 && !EquipRules.ClassAllows(self.Class, reqCls);

    private static void AddRateLine(List<TooltipLine> lines, int raiseTextId, int lowerTextId, string what, int percent)
    {
        if (percent == 0) return;
        lines.Add(percent > 0
            ? new TooltipLine(FormatIntText(raiseTextId, $"Increase {what} by %d%%", percent), TooltipColorPlain)
            : new TooltipLine(FormatIntText(lowerTextId, $"Decrease {what} by %d%%", -percent), TooltipColorPlain));
    }

    private static string FormatIntText(int textId, string fallback, int value)
    {
        string s = ItemData.Text(textId, $"{fallback} : %d");
        return s.Replace("%d", value.ToString()).Replace("%s", "").Replace("%%", "%").Trim();
    }

    private static string FormatWholeText(int textId, string fallback, float value)
    {
        string s = ItemData.Text(textId, $"{fallback} : %2.f");
        return s.Replace("%2.f", Mathf.RoundToInt(value).ToString()).Trim();
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

    private static bool IsWeapon(ItemData.Item def) =>
        def.Slot is ItemData.SlotType.OneHand or ItemData.SlotType.RightOnly or ItemData.SlotType.TwoHandRight
            or ItemData.SlotType.TwoHandLeft
        && def.Kind != ItemData.Kind.Shield;

    internal static Color TooltipColor(int idx) => Config.TooltipColor(idx);

    internal sealed class TooltipSheet
    {
        public int ItemId;
        public int Count;
        public string Name = "";
        public Color NameColor = Colors.White;
        public bool Equipped;
        public readonly List<TooltipLine> Lines = new();

        public string Signature()
        {
            var sb = new StringBuilder();
            sb.Append(ItemId).Append('|').Append(Count).Append('|').Append(Name).Append('|')
                .Append(NameColor.ToRgba32()).Append('|').Append(Equipped);
            foreach (var line in Lines)
                sb.Append('\n').Append(line.Divider ? "-" : line.Text).Append('|').Append(line.Tint.ToRgba32())
                    .Append('|').Append((int)line.Align).Append('|').Append(line.Gauge);
            return sb.ToString();
        }
    }

    internal sealed partial class TipView : PanelContainer
    {
        private readonly Label equipped;
        private readonly TextureRect icon;
        private readonly UpgradeBadge plus;
        private readonly Label count;
        private readonly Label name;
        private readonly VBoxContainer lines;
        private string signature = "";

        public TipView(string equippedText)
        {
            Visible = false;
            MouseFilter = MouseFilterEnum.Ignore;
            ZIndex = 250;
            var bg = new StyleBoxFlat
            {
                BgColor = new Color(0.031f, 0.027f, 0.024f, 0.97f),
                BorderColor = new Color(0.40f, 0.33f, 0.19f, 0.92f),
                ShadowColor = new Color(0, 0, 0, 0.60f),
                ShadowSize = 8,
            };
            bg.SetBorderWidthAll(1);
            bg.SetCornerRadiusAll(2);
            AddThemeStyleboxOverride("panel", bg);

            var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
            UiTheme.Margins(margin, 10, 8, 10, 9);
            AddChild(margin);
            var body = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            body.AddThemeConstantOverride("separation", 1);
            margin.AddChild(body);

            equipped = HudStyle.Label(TooltipFontHeight, HorizontalAlignment.Center);
            equipped.Text = equippedText;
            equipped.AddThemeColorOverride("font_color", TooltipEquippedColor);
            equipped.Visible = false;
            body.AddChild(equipped);

            var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            head.AddThemeConstantOverride("separation", (int)TooltipHeadGap);
            body.AddChild(head);
            icon = new TextureRect
            {
                CustomMinimumSize = new Vector2(TooltipIconSide, TooltipIconSide),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            head.AddChild(icon);
            plus = UpgradeBadge.Attach(icon);
            count = HudStyle.Label(TooltipFontHeight, HorizontalAlignment.Right);
            count.VerticalAlignment = VerticalAlignment.Bottom;
            count.MouseFilter = MouseFilterEnum.Ignore;
            count.SetAnchorsPreset(LayoutPreset.FullRect);
            icon.AddChild(count);
            name = HudStyle.Label(TooltipFontHeight + TooltipNameFontExtra);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.AddThemeConstantOverride("font_embolden", 1);
            name.MouseFilter = MouseFilterEnum.Ignore;
            head.AddChild(name);

            lines = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            lines.AddThemeConstantOverride("separation", 1);
            body.AddChild(lines);
            MinimumSizeChanged += () => Callable.From(ResetSize).CallDeferred();
        }

        public void Show(TooltipSheet sheet)
        {
            string signature = sheet.Signature();
            if (Visible && signature == this.signature) return;
            this.signature = signature;

            equipped.Visible = sheet.Equipped;
            icon.Texture = sheet.ItemId != 0 ? ItemIcons.Get(sheet.ItemId) : null;
            plus.Set(sheet.ItemId);
            count.Text = sheet.Count > 1 ? sheet.Count.ToString() : "";
            name.Text = sheet.Name;
            FitName();
            name.AddThemeColorOverride("font_color", sheet.NameColor);

            foreach (Node child in lines.GetChildren())
            {
                lines.RemoveChild(child);
                child.QueueFree();
            }
            foreach (var line in sheet.Lines)
                lines.AddChild(LineControl(line));
            Visible = true;
            ResetSize();
        }

        private void FitName()
        {
            var font = name.GetThemeFont("font");
            int size = name.GetThemeFontSize("font_size");
            bool wrap = font.GetStringSize(name.Text, HorizontalAlignment.Left, -1, size).X > TooltipNameWrapWidth;
            name.AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off;
            name.CustomMinimumSize = new Vector2(wrap ? TooltipNameWrapWidth : 0, 0);
        }

        public void Close()
        {
            Visible = false;
            signature = "";
        }

        private static Control LineControl(TooltipLine line)
        {
            if (line.Divider)
                return new TextureRect
                {
                    Texture = UiTheme.Divider(),
                    CustomMinimumSize = new Vector2(0, 3),
                    StretchMode = TextureRect.StretchModeEnum.Scale,
                    MouseFilter = MouseFilterEnum.Ignore,
                };

            var label = HudStyle.Label(line.Gauge >= 0 ? TooltipFontHeight - TooltipSmallFontDrop : TooltipFontHeight, line.Align);
            label.Text = line.Text;
            label.MouseFilter = MouseFilterEnum.Ignore;
            label.AutowrapMode = TextServer.AutowrapMode.Off;
            label.AddThemeColorOverride("font_color", line.Tint);
            if (TooltipBold) label.AddThemeConstantOverride("font_embolden", 1);
            if (line.Gauge < 0) return label;

            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(label);
            row.AddChild(new DurabilityGauge(line.Gauge));
            return row;
        }
    }

    internal sealed partial class DurabilityGauge : Control
    {
        private readonly int percent;

        public DurabilityGauge(int percent)
        {
            this.percent = percent;
            CustomMinimumSize = new Vector2(TooltipGaugeWidth, TooltipGaugeHeight);
            SizeFlagsVertical = SizeFlags.ShrinkCenter;
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            var rect = new Rect2(Vector2.Zero, new Vector2(TooltipGaugeWidth, TooltipGaugeHeight));
            DrawRect(rect, DurabilityGaugeBack);
            DrawRect(new Rect2(Vector2.Zero, new Vector2(TooltipGaugeWidth * percent / 100f, TooltipGaugeHeight)),
                DurabilityGaugeFill);
        }
    }

    internal void ShowItem(int itemId) =>
        Show(-1, new ItemSlot { ItemId = itemId, Count = 1, Durability = ItemData.MaxDurabilityOf(itemId) });

    internal void HoverWarehouse(WarehouseCell cell)
    {
        if (cell.Item.IsEmpty) Hide();
        else Show(cell.TipSlot, cell.Item);
    }
}
