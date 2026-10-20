using Godot;

namespace LibreKO.Content;

internal static class ItemDataLoader
{
    internal static void Load()
    {
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
        LoadSetBonuses(d);
        LoadSkillOptions(d);
        LoadPriceOverrides();
        foreach (var key in d.Keys)
        {
            if (!int.TryParse(key.AsString(), out int id)) continue;
            var o = d[key].AsGodotDictionary();
            int reqLevel = Int(o, "reqLevel");
            if (reqLevel == 0) reqLevel = Int(o, "reqLvl");
            ItemData.AddItem(new ItemData.Item
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
                UnbindStones = Int(o, "grade"), Bound = Int(o, "bound"), Notice = Int(o, "notice"),
                Icon = Int(o, "icon"), Duration = Int(o, "duration"),
            });
        }

        // Offline Merchant Voucher (700111000)
        ItemData.AddItem(new ItemData.Item
        {
            Id = 700111000,
            Name = "Voucher of Offline Merchant",
            Desc = "Offline Merchant Voucher. Allows deploying a Slave Merchant for 1 hour.",
            Kind = 97,
            Slot = 15,
            Weight = 1,
            Duration = 1,
            BuyPrice = 50000000,
            Countable = 1,
            ReqLevel = 0,
            ReqLevelMax = 100,
            Icon = 22,
        });
    }

    private static void LoadTexts(Godot.Collections.Dictionary root)
    {
        if (!root.TryGetValue("_texts", out var tv) || tv.VariantType != Variant.Type.Dictionary) return;
        var rawTexts = tv.AsGodotDictionary();
        foreach (var key in rawTexts.Keys)
            if (int.TryParse(key.AsString(), out int id))
                ItemData.AddText(id, rawTexts[key].AsString());
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
                var e = new ItemData.Ext
                {
                    Cat = cat, Id = extId,
                    Name = Str(o, "name"), Desc = Str(o, "desc"),
                    Linked = Int(o, "linked"), GlowFx = Int(o, "glowFx"),
                    Resrc = Int(o, "resrc"), Icon = Int(o, "icon"), Special = Int(o, "special"),
                    PriceMultiply = Int(o, "priceMultiply"),
                    AttackIntervalPercent = RawInt(o, ExtAttackIntervalColumn),
                    BonusDamage = Int(o, "bonusDamage"), BonusAc = Int(o, "bonusAc"),
                    BonusMaxHp = Int(o, "bonusMaxHp"), BonusMaxMp = Int(o, "bonusMaxMp"),
                    BonusStr = Int(o, "bonusStr"), BonusSta = Int(o, "bonusSta"),
                    BonusDex = Int(o, "bonusDex"), BonusInt = Int(o, "bonusInt"), BonusCha = Int(o, "bonusCha"),
                    BonusFireR = Int(o, "bonusFireR"), BonusColdR = Int(o, "bonusColdR"),
                    BonusLightningR = Int(o, "bonusLightningR"), BonusMagicR = Int(o, "bonusMagicR"),
                    BonusPoisonR = Int(o, "bonusPoisonR"), BonusCurseR = Int(o, "bonusCurseR"),
                    BonusHitrate = Int(o, "bonusHitrate"), BonusEvasionrate = Int(o, "bonusEvasionrate"),
                    BonusDaggerAc = Int(o, "bonusDaggerAc"), BonusJamadarAc = Int(o, "bonusJamadarAc"),
                    BonusSwordAc = Int(o, "bonusSwordAc"), BonusClubAc = Int(o, "bonusClubAc"),
                    BonusAxeAc = Int(o, "bonusAxeAc"), BonusSpearAc = Int(o, "bonusSpearAc"),
                    BonusArrowAc = Int(o, "bonusArrowAc"),
                    MagicOrRare = Int(o, "magicOrRare"),
                    DurationBonus = Int(o, "durationBonus"),
                    ReqStrBonus = Int(o, "reqStrBonus"),
                    ReqStaBonus = Int(o, "reqStaBonus"),
                    ReqDexBonus = Int(o, "reqDexBonus"),
                    ReqIntBonus = Int(o, "reqIntBonus"),
                    ReqChaBonus = Int(o, "reqChaBonus"),
                    ReqLevelBonus = Int(o, "reqLevelBonus"),
                    Plus = Int(o, "plus"),
                    FireDamage = Int(o, "fireDamage"), IceDamage = Int(o, "iceDamage"),
                    LightningDamage = Int(o, "lightningDamage"), PoisonDamage = Int(o, "poisonDamage"),
                    HpDrain = Int(o, "hpDrain"), MpDamage = Int(o, "mpDamage"),
                    MpDrain = Int(o, "mpDrain"), Mirror = Int(o, "mirror"),
                    GradeClass = RawInt(o, ExtGradeClassColumn),
                    UnbindStones = RawInt(o, ExtUnbindStonesColumn),
                };
                ItemData.AddExt(e);
            }
        }

        ItemData.IndexReverseCategories();
    }

    private static void LoadSetBonuses(Godot.Collections.Dictionary root)
    {
        if (!root.TryGetValue("_sets", out var sv) || sv.VariantType != Variant.Type.Dictionary) return;
        var rows = sv.AsGodotDictionary();
        foreach (var key in rows.Keys)
        {
            if (!int.TryParse(key.AsString(), out int id)) continue;
            var o = rows[key].AsGodotDictionary();
            ItemData.AddSetBonus(id, new ItemData.SetBonus
            {
                Name = Str(o, "name"),
                Ac = Int(o, "ac"), Hp = Int(o, "hp"), Mp = Int(o, "mp"),
                Str = Int(o, "str"), Sta = Int(o, "sta"), Dex = Int(o, "dex"), Int = Int(o, "int"), Cha = Int(o, "cha"),
                FireR = Int(o, "fireR"), ColdR = Int(o, "coldR"), LightningR = Int(o, "lightningR"),
                PoisonR = Int(o, "poisonR"), MagicR = Int(o, "magicR"), CurseR = Int(o, "curseR"),
                XpPct = Int(o, "xpPct"), NoahPct = Int(o, "noahPct"), ApPct = Int(o, "apPct"), Ap = Int(o, "ap"),
                ApClass = Int(o, "apClass"), ApClassPct = Int(o, "apClassPct"),
                AcClass = Int(o, "acClass"), AcClassPct = Int(o, "acClassPct"),
                MaxWeight = Int(o, "maxWeight"), Np = Int(o, "np"),
            });
        }
    }

    private static void LoadSkillOptions(Godot.Collections.Dictionary root)
    {
        if (!root.TryGetValue("_ops", out var ov) || ov.VariantType != Variant.Type.Dictionary) return;
        var rows = ov.AsGodotDictionary();
        foreach (var key in rows.Keys)
        {
            if (!int.TryParse(key.AsString(), out int id)) continue;
            var list = new List<ItemData.SkillOption>();
            foreach (var entry in rows[key].AsGodotArray())
            {
                var a = entry.AsGodotArray();
                if (a.Count >= 3) list.Add(new ItemData.SkillOption(a[0].AsInt32(), a[1].AsInt32(), a[2].AsInt32()));
            }
            if (list.Count > 0) ItemData.AddSkillOptions(id, list);
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
            var list = new List<ItemData.SellEntry>(arr.Count);
            foreach (var e in arr)
            {
                var t = e.AsGodotArray();
                if (t.Count < 3) continue;
                list.Add(new ItemData.SellEntry { Id = t[0].AsInt32(), Line = t[1].AsInt32(), List = t[2].AsInt32() });
            }
            ItemData.AddSellGroup(group, list);
        }
    }

    private static void LoadPieceRewards(Godot.Collections.Dictionary root)
    {
        if (!root.TryGetValue("_pieces", out var pv) || pv.VariantType != Variant.Type.Dictionary) return;
        var rawPieces = pv.AsGodotDictionary();
        foreach (var key in rawPieces.Keys)
        {
            if (!int.TryParse(key.AsString(), out int pieceId)) continue;
            var arr = rawPieces[key].AsGodotArray();
            var rewards = new int[arr.Count];
            for (int i = 0; i < arr.Count; i++) rewards[i] = arr[i].AsInt32();
            if (rewards.Length > 0) ItemData.AddPieceRewards(pieceId, rewards);
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
            ItemData.AddAttendance(slot, arr[0].AsInt32(), arr[1].AsInt32());
        }
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
            ItemData.AddHelp(id, pair[0].AsString(), pair[1].AsString());
        }
    }

    private static void LoadPriceOverrides()
    {
        using var f = Godot.FileAccess.Open("res://assets/items/price_overrides.json", Godot.FileAccess.ModeFlags.Read);
        if (f == null) return;
        var parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) return;
        var d = parsed.AsGodotDictionary();
        foreach (var key in d.Keys)
            if (int.TryParse(key.AsString(), out int id)) ItemData.AddPriceOverride(id, d[key].AsInt32());
    }

    private static string Str(Godot.Collections.Dictionary o, string k) => o.ContainsKey(k) ? o[k].AsString() : "";
    private static int Int(Godot.Collections.Dictionary o, string k) =>
        o.ContainsKey(k) && o[k].VariantType != Variant.Type.Nil ? o[k].AsInt32() : 0;

    private const string ExtAttackIntervalColumn = "9";
    private const string ExtGradeClassColumn = "30";
    private const string ExtUnbindStonesColumn = "55";

    private static int RawInt(Godot.Collections.Dictionary o, string column) =>
        o.ContainsKey("raw") && o["raw"].VariantType == Variant.Type.Dictionary
            ? Int(o["raw"].AsGodotDictionary(), column)
            : 0;
}
