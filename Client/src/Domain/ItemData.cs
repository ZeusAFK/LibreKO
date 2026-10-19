namespace LibreKO.Domain;

public static class ItemData
{
    public sealed class Item
    {
        public int Id;
        public string Name = "";
        public string Desc = "";
        public int Cat;
        public int Kind;
        public int Race;
        public int Slot;
        public int Class;
        public int Value;
        public int MinDamage;
        public int Damage;
        public int Delay;
        public int Range;
        public int Weight;
        public int BuyPrice;
        public int SaleType;
        public int Ac;
        public int Countable;
        public int Effect1;
        public int Effect2;
        public int ReqLevel;
        public int ReqLevelMax;
        public int ReqLvl;
        public int ReqCls;
        public int ReqRank;
        public int ReqTitle;
        public int ReqStr;
        public int ReqSta;
        public int ReqDex;
        public int ReqInt;
        public int ReqCha;
        public int SellingGroup;
        public int ItemType;
        public int UnbindStones;
        public int Bound;
        public int Notice;
        public int Icon;
        public int Duration;

        public bool IsChargeItem => Kind == ChargeItemKind && Countable == 0;
    }

    private const int ChargeItemKind = 255;

    public static int ShownCount(Item? def, ItemSlot slot) =>
        def is { IsChargeItem: true } ? slot.Durability : slot.Count;

    public static int MaxDurability(Item? def, Ext? ext) => (def?.Duration ?? 0) + (ext?.DurationBonus ?? 0);

    public static short MaxDurabilityOf(int itemId) =>
        (short)Math.Min(MaxDurability(Get(itemId), ExtFor(itemId)), short.MaxValue);

    public static int CarriedUnits(Item? def, ItemSlot slot) =>
        def is { IsChargeItem: true } ? 1 : slot.Count;

    public sealed class Ext
    {
        public int Cat;
        public int Id;
        public string Name = "";
        public string Desc = "";
        public int Linked;
        public int GlowFx;
        public int Resrc;
        public int Icon;
        public int Special;
        public int PriceMultiply;
        public int AttackIntervalPercent;
        public int BonusDamage;
        public int BonusAc;
        public int BonusMaxHp;
        public int BonusMaxMp;
        public int BonusStr;
        public int BonusSta;
        public int BonusDex;
        public int BonusInt;
        public int BonusCha;
        public int BonusFireR;
        public int BonusColdR;
        public int BonusLightningR;
        public int BonusMagicR;
        public int BonusPoisonR;
        public int BonusCurseR;
        public int BonusHitrate;
        public int BonusEvasionrate;
        public int BonusDaggerAc;
        public int BonusJamadarAc;
        public int BonusSwordAc;
        public int BonusClubAc;
        public int BonusAxeAc;
        public int BonusSpearAc;
        public int BonusArrowAc;
        public int MagicOrRare;
        public int DurationBonus;
        public int ReqStrBonus;
        public int ReqStaBonus;
        public int ReqDexBonus;
        public int ReqIntBonus;
        public int ReqChaBonus;
        public int ReqLevelBonus;
        public int Plus;
        public int FireDamage;
        public int IceDamage;
        public int LightningDamage;
        public int PoisonDamage;
        public int HpDrain;
        public int MpDamage;
        public int MpDrain;
        public int Mirror;
        public int GradeClass;
        public int UnbindStones;
    }

    public sealed class SetBonus
    {
        public string Name = "";
        public int Ac, Hp, Mp;
        public int Str, Sta, Dex, Int, Cha;
        public int FireR, ColdR, LightningR, PoisonR, MagicR, CurseR;
        public int XpPct, NoahPct, ApPct, Ap;
        public int ApClass, ApClassPct, AcClass, AcClassPct;
        public int MaxWeight, Np;
    }

    public readonly record struct SkillOption(int Trigger, int SkillId, int Chance);

    public sealed class SellEntry
    {
        public int Id;
        public int Line;
        public int List;
    }

    public const int SaleTypeLow = 0;
    public const int SaleTypeFull = 1;
    public const int SaleTypeLowNoRepair = 2;
    private const int SellPriceDivisor = 6;
    private const int PremiumSellPriceDivisor = 4;

    public static bool PremiumSeller { get; set; }

    public static int SellUnit(int buyPrice, bool fullSale, bool premium)
    {
        int price = fullSale ? buyPrice : buyPrice / (premium ? PremiumSellPriceDivisor : SellPriceDivisor);
        return price < 1 ? 0 : price;
    }

    public const int NoTradeIdFirst = 900000001;
    public const int NoTradeIdLast = 999999999;

    public static bool IsNoTradeId(int id) => id is >= NoTradeIdFirst and <= NoTradeIdLast;
    private const int ExtBlockSpan = 1_000_000_000;
    private const int BaseIdSpan = 1000;

    public static class Race
    {
        public const int NoSale = 12;
        public const int Quest = 20;
        public const int NoTradeSellDestroy = 50;
        public const int TradeAfter72Hours = 69;
        public const int NoTradeDestroy = 70;
        public const int NoTradeDestroyAlt = 71;
        public const int NoTradeSellStore = 73;
        public const int OneTimeTrade = 74;
        public const int NoTradeSellDestroyAlt = 76;
        public const int NoTradeSellStoreAlt = 77;
    }

    public static class SlotType
    {
        public const int OneHand = 0;
        public const int RightOnly = 1;
        public const int LeftOnly = 2;
        public const int TwoHandRight = 3;
        public const int TwoHandLeft = 4;
        public const int Pauldron = 5;
        public const int Pants = 6;
        public const int Helmet = 7;
        public const int Gloves = 8;
        public const int Boots = 9;
        public const int Earring = 10;
        public const int Necklace = 11;
        public const int Ring = 12;
        public const int Belt = 14;
        public const int Pet = 20;
    }

    public static class Kind
    {
        public const int Shield = 60;
        public const int Earring = 91;
        public const int Necklace = 92;
        public const int Ring = 93;
        public const int Belt = 94;
    }

    public const int Effect2ExchangePiece = 251;
    public const string PlainUpgrade = "Upgrade";
    public const int MaxPlusJewel = 3;
    public const int MaxPlusGear = 10;
    public const int MaxPlusReverse = 21;

    public static class Rarity
    {
        public const int Regular = 0;
        public const int Magic = 1;
        public const int Rare = 2;
        public const int Craft = 3;
        public const int Unique = 4;
        public const int Upgrade = 5;
        public const int Event = 6;
        public const int Pet = 7;
        public const int Cospre = 8;
        public const int CospreTransparent = 9;
        public const int Reverse = 11;
        public const int ReverseUnique = 12;
    }

    private static readonly Dictionary<int, Item> items = new();
    private static readonly Dictionary<int, List<SellEntry>> sell = new();
    private static readonly Dictionary<int, int[]> pieces = new();
    private static readonly Dictionary<int, (int ItemId, int Count)> attendance = new();
    private static readonly Dictionary<int, (string Title, string Body)> help = new();
    private static readonly Dictionary<long, Ext> exts = new();
    private static readonly Dictionary<int, List<Ext>> extsByCat = new();
    private static readonly Dictionary<int, Ext> linkedExts = new();
    private static readonly HashSet<int> reverseCats = new();
    private static readonly HashSet<int> ambiguousLinkedExts = new();
    private static readonly Dictionary<int, string> texts = new();
    private static readonly Dictionary<int, SetBonus> setBonuses = new();
    private static readonly Dictionary<int, List<SkillOption>> skillOptions = new();
    private static readonly object LoadLock = new();
    private static volatile bool loaded;
    private static bool parsing;

    public static void PreloadInBackground()
    {
        if (!loaded) System.Threading.Tasks.Task.Run(EnsureLoaded);
    }

    internal static Action? Loader { get; set; }

    internal static void AddItem(Item item) => items[item.Id] = item;

    internal static void AddText(int id, string text) => texts[id] = text;

    internal static void AddPriceOverride(int itemId, int price) => priceOverrides[itemId] = price;

    internal static void AddSetBonus(int id, SetBonus bonus) => setBonuses[id] = bonus;

    internal static void AddSkillOptions(int itemId, List<SkillOption> options) => skillOptions[itemId] = options;

    internal static void AddSellGroup(int group, List<SellEntry> entries) => sell[group] = entries;

    internal static void AddPieceRewards(int pieceItemId, int[] rewards) => pieces[pieceItemId] = rewards;

    internal static void AddAttendance(int slot, int itemId, int count) => attendance[slot] = (itemId, count);

    internal static void AddHelp(int id, string title, string body) => help[id] = (title, body);

    internal static void AddExt(Ext e)
    {
        exts[ExtKey(e.Cat, e.Id)] = e;
        if (!extsByCat.TryGetValue(e.Cat, out var catList))
            extsByCat[e.Cat] = catList = new List<Ext>();
        catList.Add(e);
        if (e.Linked <= 0) return;
        if (linkedExts.ContainsKey(e.Linked))
        {
            linkedExts.Remove(e.Linked);
            ambiguousLinkedExts.Add(e.Linked);
        }
        else if (!ambiguousLinkedExts.Contains(e.Linked))
        {
            linkedExts[e.Linked] = e;
        }
    }

    internal static void IndexReverseCategories()
    {
        foreach (var (cat, list) in extsByCat)
        {
            int reverse = 0;
            foreach (var e in list)
                if (e.MagicOrRare is Rarity.Reverse or Rarity.ReverseUnique) reverse++;
            if (reverse * 2 > list.Count) reverseCats.Add(cat);
        }
    }

    public static int IconId(int itemId)
    {
        EnsureLoaded();
        var it = Get(itemId);
        if (it == null) return 0;
        int icon = it.Icon;
        if (ExtFor(itemId) is { Icon: > 0 } ext) icon = ext.Icon;
        return icon;
    }

    public static void EnsureLoaded()
    {
        if (loaded) return;
        lock (LoadLock)
        {
            if (loaded || parsing) return;
            parsing = true;
            try { Loader?.Invoke(); }
            finally { parsing = false; loaded = true; }
        }
    }

    private static readonly Dictionary<int, int> priceOverrides = new();

    public readonly record struct SetPiece(int SlotType, string Name);

    private static readonly Dictionary<int, List<SetPiece>> setPieces = new();
    private const int SetPieceRaceCap = 200;
    private const int SlotTypeSpan = 100;

    public static SetBonus? SetRow(int key)
    {
        EnsureLoaded();
        return setBonuses.TryGetValue(key, out var row) ? row : null;
    }

    public static IReadOnlyList<SetPiece> SetPieces(int race)
    {
        EnsureLoaded();
        if (setPieces.TryGetValue(race, out var cached)) return cached;
        var rows = new List<Item>();
        if (race < SetPieceRaceCap)
            foreach (var it in items.Values)
                if (it.Race == race) rows.Add(it);
        rows.Sort((a, b) => ((uint)a.Id).CompareTo((uint)b.Id));
        var seen = new HashSet<int>();
        var pieceList = new List<SetPiece>();
        foreach (var it in rows)
            if (seen.Add(it.Slot % SlotTypeSpan)) pieceList.Add(new SetPiece(it.Slot, it.Name));
        setPieces[race] = pieceList;
        return pieceList;
    }

    public static SetBonus? CospreBonus(int itemId)
    {
        EnsureLoaded();
        return setBonuses.TryGetValue(itemId, out var exact) ? exact
            : setBonuses.TryGetValue(BaseId(itemId), out var shared) ? shared : null;
    }

    public static IReadOnlyList<SkillOption> SkillOptions(int itemId)
    {
        EnsureLoaded();
        return skillOptions.TryGetValue(itemId, out var exact) ? exact
            : skillOptions.TryGetValue(BaseId(itemId), out var shared) ? shared : System.Array.Empty<SkillOption>();
    }

    public static (int ItemId, int Count) AttendanceReward(int slot)
    {
        EnsureLoaded();
        return attendance.TryGetValue(slot, out var r) ? r : (0, 0);
    }

    public static IReadOnlyList<int> PieceRewards(int pieceItemId)
    {
        EnsureLoaded();
        return pieces.TryGetValue(pieceItemId, out var r) ? r : (IReadOnlyList<int>)System.Array.Empty<int>();
    }

    public static bool IsExchangePiece(int itemId)
    {
        EnsureLoaded();
        return pieces.ContainsKey(itemId);
    }

    public static (string Title, string Body) UiHelp(int id)
    {
        EnsureLoaded();
        return help.TryGetValue(id, out var entry) ? entry : ("", "");
    }

    public static IReadOnlyList<SellEntry> SellGroup(int group)
    {
        EnsureLoaded();
        return sell.TryGetValue(group, out var l) ? l : (IReadOnlyList<SellEntry>)System.Array.Empty<SellEntry>();
    }

    public static IEnumerable<Item> All()
    {
        EnsureLoaded();
        return items.Values;
    }

    public static IEnumerable<Ext> ExtsForCat(int cat)
    {
        EnsureLoaded();
        return extsByCat.TryGetValue(cat, out var l) ? l : (IEnumerable<Ext>)System.Array.Empty<Ext>();
    }

    public static IEnumerable<Ext> AllExts()
    {
        EnsureLoaded();
        return exts.Values;
    }

    public static Item? Get(int id)
    {
        EnsureLoaded();
        if (items.TryGetValue(id, out var it)) return it;
        int baseId = BaseId(id);
        return baseId != id && items.TryGetValue(baseId, out var b) ? b : null;
    }

    public static int BaseId(int id) => id / BaseIdSpan * BaseIdSpan;

    public static int ExtIdFor(int id) => id / ExtBlockSpan * 1000 + id % 1000;

    public static bool ExtAppliesTo(Item def, Ext ext) =>
        ext.Cat == def.Cat && ext.Id % 1000 != 0 && ext.Id / 1000 == def.Id / ExtBlockSpan;

    public static int VariantId(Item def, Ext ext) => def.Id + ext.Id % 1000;

    public static bool IsReverse(Item def, Ext? ext)
    {
        EnsureLoaded();
        return ext != null
            ? ext.MagicOrRare is Rarity.Reverse or Rarity.ReverseUnique
            : reverseCats.Contains(def.Cat);
    }

    public static int MaxPlus(Item def, Ext? ext)
    {
        if (IsReverse(def, ext)) return MaxPlusReverse;
        return def.Kind is Kind.Earring or Kind.Necklace or Kind.Ring or Kind.Belt
            ? MaxPlusJewel
            : MaxPlusGear;
    }

    public static Ext? ExtFor(int id)
    {
        EnsureLoaded();
        var def = Get(id);
        if (def == null) return null;
        int extId = ExtIdFor(id);
        if (extId % 1000 != 0 && exts.TryGetValue(ExtKey(def.Cat, extId), out var byKey))
            return byKey;
        return linkedExts.TryGetValue(id, out var byLinked) && byLinked.Cat == def.Cat ? byLinked : null;
    }

    public static Ext? ExtRow(int cat, int extId)
    {
        EnsureLoaded();
        return exts.TryGetValue(ExtKey(cat, extId), out var ext) ? ext : null;
    }

    public static int PriceMultiply(int id)
    {
        if (id % 1000 == 0) return 1;
        int mul = ExtFor(id)?.PriceMultiply ?? 0;
        return mul > 0 ? mul : 1;
    }

    public static int PotionHeal(int id, int healTarget)
    {
        if (Get(id) is not { Effect1: not 0 } def) return 0;
        if (SkillData.Get(def.Effect1) is not { } s) return 0;
        return PotionHealFor(s.Type1, s.DirectType, s.FirstDamage, healTarget);
    }

    internal static int PotionHealFor(int type, int directType, int amount, int healTarget)
    {
        if (type != MagicType.DotHeal || amount <= 0) return 0;
        bool restores = healTarget switch
        {
            HealTarget.Hp => directType is HealTarget.Hp or HealTarget.HpPurchase,
            HealTarget.Mp => directType is HealTarget.Mp or HealTarget.MpPurchase,
            _ => false,
        };
        return restores ? amount : 0;
    }

    public static int BuyPrice(int id) => priceOverrides.TryGetValue(id, out int price) ? price : RawBuyPrice(id);

    public static int RawBuyPrice(int id) => (Get(id)?.BuyPrice ?? 0) * PriceMultiply(id);

    public static int SellPrice(int id)
    {
        var def = Get(id);
        return def == null ? 0 : SellUnit(BuyPrice(id), def.SaleType == SaleTypeFull, PremiumSeller);
    }

    public static bool IsSellable(int id)
    {
        var def = Get(id);
        return def != null && !IsNoTradeId(id) && def.Race != Race.Quest && SellPrice(id) > 0;
    }

    public static string DisplayName(int id)
    {
        var def = Get(id);
        if (def == null) return $"Item {id}";
        var ext = ExtFor(id);
        if (ext != null && UsesExtName(ext.MagicOrRare))
        {
            string extName = ext.Name.Trim();
            if (extName.Length > 0) return extName;
        }
        if (string.IsNullOrWhiteSpace(def.Name)) return $"Item {id}";
        int plus = ext == null ? 0 : UpgradeLevel(ext);
        return plus > 0 ? $"{def.Name}(+{plus})" : def.Name;
    }

    public static int UpgradeLevel(int id)
    {
        SplitUpgrade(DisplayName(id), out int plus);
        return plus;
    }

    public static string SplitUpgrade(string displayName, out int plus)
    {
        plus = 0;
        if (!displayName.EndsWith(')')) return displayName;
        int open = displayName.LastIndexOf("(+", System.StringComparison.Ordinal);
        if (open < 0) return displayName;
        if (!int.TryParse(displayName.AsSpan(open + 2, displayName.Length - open - 3), out int parsed))
            return displayName;
        plus = parsed;
        return displayName[..open].TrimEnd();
    }

    public static string Text(int id, string fallback)
    {
        EnsureLoaded();
        return texts.TryGetValue(id, out var text) && text.Length > 0 ? text : fallback;
    }

    public static int EquipSlotFor(Item it) => it.Slot switch
    {
        SlotType.OneHand or SlotType.RightOnly or SlotType.TwoHandRight => InventoryConstants.RightHand,
        SlotType.LeftOnly or SlotType.TwoHandLeft => InventoryConstants.LeftHand,
        SlotType.Pauldron => InventoryConstants.Breast,
        SlotType.Pants => InventoryConstants.Leg,
        SlotType.Helmet => InventoryConstants.Head,
        SlotType.Gloves => InventoryConstants.Glove,
        SlotType.Boots => InventoryConstants.Foot,
        SlotType.Earring => InventoryConstants.RightEar,
        SlotType.Necklace => InventoryConstants.Neck,
        SlotType.Ring => InventoryConstants.RightRing,
        SlotType.Belt => InventoryConstants.Waist,
        SlotType.Pet => InventoryConstants.Pet,
        _ => -1,
    };

    private static long ExtKey(int cat, int extId) => ((long)cat << 32) | (uint)extId;

    private static bool UsesExtName(int rarity) =>
        rarity is Rarity.Craft or Rarity.Unique or Rarity.Event or Rarity.ReverseUnique;

    private static int UpgradeLevel(Ext ext)
    {
        if (!TakesUpgradeSuffix(ext.MagicOrRare)) return 0;
        if (ext.MagicOrRare == Rarity.Reverse) return ext.Plus % 100;
        int tail = ext.Id % 10;
        if (tail != 0) return tail;
        return ext.MagicOrRare != Rarity.Regular ? 10 : 0;
    }

    private static bool TakesUpgradeSuffix(int rarity) =>
        rarity is not (Rarity.Unique or Rarity.Event or Rarity.ReverseUnique
                       or Rarity.Pet or Rarity.Cospre or Rarity.CospreTransparent);

    public static int GradeClass(Item def, Ext? ext) => def.ItemType + (ext?.GradeClass ?? 0);

    public static int UnbindStones(Item def, Ext? ext) => def.UnbindStones + (ext?.UnbindStones ?? 0);

    public static bool BindsOnEquip(int itemId) => Get(itemId)?.UnbindStones > 0;

    public static ItemFlag ShownFlag(ItemSlot slot) => IconVeil.ShownFlag(slot.State, BindsOnEquip(slot.ItemId));
}
