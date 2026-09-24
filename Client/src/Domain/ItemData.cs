using System.Collections.Generic;
using Godot;

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
        public int Grade;
        public int Bound;
        public int Notice;
        public int Icon;
        public int Duration;

        public bool IsChargeItem => Kind == ChargeItemKind && Countable == 0;
    }

    private const int ChargeItemKind = 255;

    public static int ShownCount(Item? def, ItemSlot slot) =>
        def is { IsChargeItem: true } ? slot.Durability : slot.Count;

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
        public int MagicOrRare;
        public int DurationBonus;
        public int ReqStrBonus;
        public int Plus;
        public int FireDamage;
        public int IceDamage;
        public int LightningDamage;
        public int PoisonDamage;
    }

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

    public const int NoTradeIdFirst = 900000001;
    public const int QuestItemRace = 20;
    private const int ExtBlockSpan = 1_000_000_000;

    public static class Kind
    {
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
        public const int Craft = 0;
        public const int Magic = 3;
        public const int Unique = 4;
        public const int Upgrade = 5;
        public const int Event = 6;
        public const int Pet = 7;
        public const int Cospre = 8;
        public const int CospreTransparent = 9;
        public const int Reverse = 11;
        public const int ReverseUnique = 12;
    }

    private static readonly Dictionary<int, Item> _items = new();
    private static readonly Dictionary<int, List<SellEntry>> _sell = new();
    private static readonly Dictionary<int, int[]> _pieces = new();
    private static readonly Dictionary<int, (int ItemId, int Count)> _attendance = new();
    private static readonly Dictionary<int, (string Title, string Body)> _help = new();
    private static readonly Dictionary<long, Ext> _exts = new();
    private static readonly Dictionary<int, List<Ext>> _extsByCat = new();
    private static readonly Dictionary<int, Ext> _linkedExts = new();
    private static readonly HashSet<int> _reverseCats = new();
    private static readonly HashSet<int> _ambiguousLinkedExts = new();
    private static readonly Dictionary<int, string> _texts = new();
    private static readonly Dictionary<int, Texture2D?> _iconCache = new();
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        using var f = Godot.FileAccess.Open("res://assets/items/items.json", Godot.FileAccess.ModeFlags.Read);
        if (f == null) { GD.PushWarning("[items] missing items.json (run tools/bake_items.py)"); return; }
        var parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) return;
        var d = parsed.AsGodotDictionary();
        LoadTexts(d);
        LoadExtensions(d);
        LoadSellGroups(d);
        LoadPieceRewards(d);
        LoadAttendance(d);
        LoadUiHelp(d);
        foreach (var key in d.Keys)
        {
            if (!int.TryParse(key.AsString(), out int id)) continue;
            var o = d[key].AsGodotDictionary();
            int reqLevel = Int(o, "reqLevel");
            if (reqLevel == 0) reqLevel = Int(o, "reqLvl");
            _items[id] = new Item
            {
                Id = id,
                Name = Str(o, "name"), Desc = Str(o, "desc"),
                Cat = Int(o, "cat"), Kind = Int(o, "kind"), Race = Int(o, "race"),
                Slot = Int(o, "slot"), Class = Int(o, "class"),
                Value = Int(o, "value"),
                MinDamage = Int(o, "minDamage"), Damage = Int(o, "damage"),
                Delay = Int(o, "delay"), Range = Int(o, "range"), Weight = Int(o, "weight"),
                BuyPrice = Int(o, "buyPrice"), SaleType = Int(o, "saleType"),
                Ac = Int(o, "ac"), Countable = Int(o, "countable"),
                Effect1 = Int(o, "effect1"), Effect2 = Int(o, "effect2"),
                ReqLevel = reqLevel, ReqLevelMax = Int(o, "reqLevelMax"), ReqLvl = reqLevel,
                ReqRank = Int(o, "reqRank"), ReqTitle = Int(o, "reqTitle"),
                ReqStr = Int(o, "reqStr"), ReqSta = Int(o, "reqSta"), ReqDex = Int(o, "reqDex"),
                ReqInt = Int(o, "reqInt"), ReqCha = Int(o, "reqCha"),
                SellingGroup = Int(o, "sellingGroup"), ReqCls = Int(o, "reqCls"), ItemType = Int(o, "itemType"),
                Grade = Int(o, "grade"), Bound = Int(o, "bound"), Notice = Int(o, "notice"),
                Icon = Int(o, "icon"), Duration = Int(o, "duration"),
            };
        }
    }

    private static void LoadTexts(Godot.Collections.Dictionary root)
    {
        if (!root.TryGetValue("_texts", out var tv) || tv.VariantType != Variant.Type.Dictionary) return;
        var texts = tv.AsGodotDictionary();
        foreach (var key in texts.Keys)
            if (int.TryParse(key.AsString(), out int id))
                _texts[id] = texts[key].AsString();
    }

    private static void LoadExtensions(Godot.Collections.Dictionary root)
    {
        if (!root.TryGetValue("_ext", out var ev) || ev.VariantType != Variant.Type.Dictionary) return;
        var cats = ev.AsGodotDictionary();
        foreach (var catKey in cats.Keys)
        {
            if (!int.TryParse(catKey.AsString(), out int cat)) continue;
            var rows = cats[catKey].AsGodotDictionary();
            foreach (var extKey in rows.Keys)
            {
                if (!int.TryParse(extKey.AsString(), out int extId)) continue;
                var o = rows[extKey].AsGodotDictionary();
                var e = new Ext
                {
                    Cat = cat, Id = extId,
                    Name = Str(o, "name"), Desc = Str(o, "desc"),
                    Linked = Int(o, "linked"), GlowFx = Int(o, "glowFx"),
                    Resrc = Int(o, "resrc"), Icon = Int(o, "icon"), Special = Int(o, "special"),
                    PriceMultiply = Int(o, "priceMultiply"),
                    BonusDamage = Int(o, "bonusDamage"), BonusAc = Int(o, "bonusAc"),
                    BonusMaxHp = Int(o, "bonusMaxHp"), BonusMaxMp = Int(o, "bonusMaxMp"),
                    BonusStr = Int(o, "bonusStr"), BonusSta = Int(o, "bonusSta"),
                    BonusDex = Int(o, "bonusDex"), BonusInt = Int(o, "bonusInt"), BonusCha = Int(o, "bonusCha"),
                    BonusFireR = Int(o, "bonusFireR"), BonusColdR = Int(o, "bonusColdR"),
                    BonusLightningR = Int(o, "bonusLightningR"), BonusMagicR = Int(o, "bonusMagicR"),
                    BonusPoisonR = Int(o, "bonusPoisonR"), BonusCurseR = Int(o, "bonusCurseR"),
                    MagicOrRare = Int(o, "magicOrRare"),
                    DurationBonus = Int(o, "durationBonus"),
                    ReqStrBonus = Int(o, "reqStrBonus"),
                    Plus = Int(o, "plus"),
                    FireDamage = Int(o, "fireDamage"), IceDamage = Int(o, "iceDamage"),
                    LightningDamage = Int(o, "lightningDamage"), PoisonDamage = Int(o, "poisonDamage"),
                };
                _exts[ExtKey(cat, extId)] = e;
                if (!_extsByCat.TryGetValue(cat, out var catList))
                    _extsByCat[cat] = catList = new List<Ext>();
                catList.Add(e);
                if (e.Linked > 0)
                {
                    if (_linkedExts.ContainsKey(e.Linked))
                    {
                        _linkedExts.Remove(e.Linked);
                        _ambiguousLinkedExts.Add(e.Linked);
                    }
                    else if (!_ambiguousLinkedExts.Contains(e.Linked))
                    {
                        _linkedExts[e.Linked] = e;
                    }
                }
            }
        }

        foreach (var (cat, list) in _extsByCat)
        {
            int reverse = 0;
            foreach (var e in list)
                if (e.MagicOrRare is Rarity.Reverse or Rarity.ReverseUnique) reverse++;
            if (reverse * 2 > list.Count) _reverseCats.Add(cat);
        }
    }

    private static void LoadSellGroups(Godot.Collections.Dictionary root)
    {
        if (!root.TryGetValue("_sell", out var sv) || sv.VariantType != Variant.Type.Dictionary) return;
        var groups = sv.AsGodotDictionary();
        foreach (var key in groups.Keys)
        {
            if (!int.TryParse(key.AsString(), out int group)) continue;
            var arr = groups[key].AsGodotArray();
            var list = new List<SellEntry>(arr.Count);
            foreach (var e in arr)
            {
                var t = e.AsGodotArray();
                if (t.Count < 3) continue;
                list.Add(new SellEntry { Id = t[0].AsInt32(), Line = t[1].AsInt32(), List = t[2].AsInt32() });
            }
            _sell[group] = list;
        }
    }

    private static void LoadPieceRewards(Godot.Collections.Dictionary root)
    {
        if (!root.TryGetValue("_pieces", out var pv) || pv.VariantType != Variant.Type.Dictionary) return;
        var pieces = pv.AsGodotDictionary();
        foreach (var key in pieces.Keys)
        {
            if (!int.TryParse(key.AsString(), out int pieceId)) continue;
            var arr = pieces[key].AsGodotArray();
            var rewards = new int[arr.Count];
            for (int i = 0; i < arr.Count; i++) rewards[i] = arr[i].AsInt32();
            if (rewards.Length > 0) _pieces[pieceId] = rewards;
        }
    }

    private static void LoadAttendance(Godot.Collections.Dictionary root)
    {
        if (!root.TryGetValue("_attendance", out var av) || av.VariantType != Variant.Type.Dictionary) return;
        var slots = av.AsGodotDictionary();
        foreach (var key in slots.Keys)
        {
            if (!int.TryParse(key.AsString(), out int slot)) continue;
            var arr = slots[key].AsGodotArray();
            if (arr.Count < 2) continue;
            _attendance[slot] = (arr[0].AsInt32(), arr[1].AsInt32());
        }
    }

    public static (int ItemId, int Count) AttendanceReward(int slot)
    {
        EnsureLoaded();
        return _attendance.TryGetValue(slot, out var r) ? r : (0, 0);
    }

    public static IReadOnlyList<int> PieceRewards(int pieceItemId)
    {
        EnsureLoaded();
        return _pieces.TryGetValue(pieceItemId, out var r) ? r : (IReadOnlyList<int>)System.Array.Empty<int>();
    }

    public static bool IsExchangePiece(int itemId)
    {
        EnsureLoaded();
        return _pieces.ContainsKey(itemId);
    }

    private static void LoadUiHelp(Godot.Collections.Dictionary root)
    {
        if (!root.TryGetValue("_help", out var hv) || hv.VariantType != Variant.Type.Dictionary) return;
        var entries = hv.AsGodotDictionary();
        foreach (var key in entries.Keys)
        {
            if (!int.TryParse(key.AsString(), out int id)) continue;
            var pair = entries[key].AsGodotArray();
            if (pair.Count < 2) continue;
            _help[id] = (pair[0].AsString(), pair[1].AsString());
        }
    }

    public static (string Title, string Body) UiHelp(int id)
    {
        EnsureLoaded();
        return _help.TryGetValue(id, out var entry) ? entry : ("", "");
    }

    public static IReadOnlyList<SellEntry> SellGroup(int group)
    {
        EnsureLoaded();
        return _sell.TryGetValue(group, out var l) ? l : (IReadOnlyList<SellEntry>)System.Array.Empty<SellEntry>();
    }

    public static IEnumerable<Item> All()
    {
        EnsureLoaded();
        return _items.Values;
    }

    public static IEnumerable<Ext> ExtsForCat(int cat)
    {
        EnsureLoaded();
        return _extsByCat.TryGetValue(cat, out var l) ? l : (IEnumerable<Ext>)System.Array.Empty<Ext>();
    }

    public static IEnumerable<Ext> AllExts()
    {
        EnsureLoaded();
        return _exts.Values;
    }

    public static Item? Get(int id)
    {
        EnsureLoaded();
        if (_items.TryGetValue(id, out var it)) return it;
        int baseId = id / 1000 * 1000;
        return baseId != id && _items.TryGetValue(baseId, out var b) ? b : null;
    }

    public static int ExtIdFor(int id) => id / ExtBlockSpan * 1000 + id % 1000;

    public static bool ExtAppliesTo(Item def, Ext ext) =>
        ext.Cat == def.Cat && ext.Id % 1000 != 0 && ext.Id / 1000 == def.Id / ExtBlockSpan;

    public static int VariantId(Item def, Ext ext) => def.Id + ext.Id % 1000;

    public static bool IsReverse(Item def, Ext? ext)
    {
        EnsureLoaded();
        return ext != null
            ? ext.MagicOrRare is Rarity.Reverse or Rarity.ReverseUnique
            : _reverseCats.Contains(def.Cat);
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
        if (extId % 1000 != 0 && _exts.TryGetValue(ExtKey(def.Cat, extId), out var byKey))
            return byKey;
        return _linkedExts.TryGetValue(id, out var byLinked) ? byLinked : null;
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
        if (SkillData.Get(def.Effect1) is not { Type1: MagicType.DotHeal } s) return 0;
        return s.DirectType == healTarget && s.FirstDamage > 0 ? s.FirstDamage : 0;
    }

    public static int BuyPrice(int id) => (Get(id)?.BuyPrice ?? 0) * PriceMultiply(id);

    public static int SellPrice(int id)
    {
        var def = Get(id);
        if (def == null) return 0;
        int price = def.BuyPrice * PriceMultiply(id);
        if (def.SaleType != SaleTypeFull) price /= SellPriceDivisor;
        return price < 1 ? 0 : price;
    }

    public static bool IsSellable(int id)
    {
        var def = Get(id);
        return def != null && id < NoTradeIdFirst && def.Race != QuestItemRace && SellPrice(id) > 0;
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
        return _texts.TryGetValue(id, out var text) && text.Length > 0 ? text : fallback;
    }

    public static Texture2D Icon(int id)
    {
        EnsureLoaded();
        var it = Get(id);
        if (it == null) return Placeholder();
        int icon = it.Icon;
        if (ExtFor(id) is { Icon: > 0 } ext) icon = ext.Icon;
        if (icon == 0) return Placeholder();
        if (_iconCache.TryGetValue(icon, out var cached)) return cached ?? Placeholder();
        string path = $"res://assets/items/icons/{icon}.png";
        Texture2D? tex = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        if (tex != null) tex = CropToArt(tex);
        _iconCache[icon] = tex;
        return tex ?? Placeholder();
    }

    private static Texture2D CropToArt(Texture2D tex)
    {
        Image? img;
        try { img = tex.GetImage(); } catch { return tex; }
        if (img == null) return tex;
        if (img.IsCompressed() && img.Decompress() != Error.Ok) return tex;

        var used = img.GetUsedRect();
        int iw = img.GetWidth(), ih = img.GetHeight();
        if (used.Size.X <= 0 || used.Size.Y <= 0) return tex;
        if (used.Size.X >= iw - 2 && used.Size.Y >= ih - 2) return tex;

        // KO art sits flush in the cell's top-left, so the pad has to fit the room EVERY side has.
        int room = Mathf.Min(
            Mathf.Min(used.Position.X, used.Position.Y),
            Mathf.Min(iw - used.End.X, ih - used.End.Y));
        int pad = Mathf.Min(Mathf.RoundToInt(Mathf.Max(used.Size.X, used.Size.Y) * 0.07f), Mathf.Max(0, room));
        int side = Mathf.Min(Mathf.Max(used.Size.X, used.Size.Y) + pad * 2, Mathf.Min(iw, ih));
        var center = used.Position + used.Size / 2;
        var sq = new Rect2I(
            Mathf.Clamp(center.X - side / 2, 0, iw - side),
            Mathf.Clamp(center.Y - side / 2, 0, ih - side),
            side, side);
        if (sq.Size.X <= 0 || sq.Size.Y <= 0) return tex;
        return new AtlasTexture { Atlas = tex, Region = new Rect2(sq.Position, sq.Size), FilterClip = true };
    }

    private static Texture2D? _placeholder;

    private static Texture2D Placeholder()
    {
        if (_placeholder != null) return _placeholder;
        const int s = 64;
        var img = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
        var bg = new Color(0.15f, 0.16f, 0.19f);
        var border = new Color(0.40f, 0.42f, 0.48f);
        var fg = new Color(0.58f, 0.60f, 0.66f);
        img.Fill(bg);
        for (int i = 0; i < s; i++)
        {
            img.SetPixel(i, 0, border); img.SetPixel(i, s - 1, border);
            img.SetPixel(0, i, border); img.SetPixel(s - 1, i, border);
        }
        byte[] glyph = { 0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b00000, 0b00100 };
        const int gw = 5, gh = 7, scale = 6;
        int ox = (s - gw * scale) / 2, oy = (s - gh * scale) / 2;
        for (int r = 0; r < gh; r++)
            for (int c = 0; c < gw; c++)
                if ((glyph[r] >> (gw - 1 - c) & 1) != 0)
                    for (int dy = 0; dy < scale; dy++)
                        for (int dx = 0; dx < scale; dx++)
                            img.SetPixel(ox + c * scale + dx, oy + r * scale + dy, fg);
        _placeholder = ImageTexture.CreateFromImage(img);
        return _placeholder;
    }

    public static int EquipSlotFor(Item it) => it.Slot switch
    {
        0 or 1 or 3 => 6,
        2 or 4 => 8,
        5 => 4,
        6 => 10,
        7 => 1,
        8 => 12,
        9 => 13,
        10 => 0,
        11 => 3,
        12 => 9,
        13 => 5,
        14 => 7,
        _ => -1,
    };

    private static long ExtKey(int cat, int extId) => ((long)cat << 32) | (uint)extId;

    private static bool UsesExtName(int rarity) =>
        rarity is Rarity.Magic or Rarity.Unique or Rarity.Event or Rarity.ReverseUnique;

    private static int UpgradeLevel(Ext ext)
    {
        if (!TakesUpgradeSuffix(ext.MagicOrRare)) return 0;
        if (ext.MagicOrRare == Rarity.Reverse) return ext.Plus % 100;
        int tail = ext.Id % 10;
        if (tail != 0) return tail;
        return ext.MagicOrRare != Rarity.Craft ? 10 : 0;
    }

    private static bool TakesUpgradeSuffix(int rarity) =>
        rarity is not (Rarity.Unique or Rarity.Event or Rarity.ReverseUnique
                       or Rarity.Pet or Rarity.Cospre or Rarity.CospreTransparent);

    private static string Str(Godot.Collections.Dictionary o, string k) => o.ContainsKey(k) ? o[k].AsString() : "";
    private static int Int(Godot.Collections.Dictionary o, string k) =>
        o.ContainsKey(k) && o[k].VariantType != Variant.Type.Nil ? o[k].AsInt32() : 0;
}
