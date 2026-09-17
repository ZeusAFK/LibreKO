using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;

namespace LibreKO.Common.Domain.Services;

public static class AbilityCalculator
{
    public const int MaxPlayerHp = 14000;

    private const byte SlotPauldron = 5;
    private const byte SlotPads = 6;
    private const byte SlotHelmet = 7;
    private const byte SlotGloves = 8;
    private const byte SlotBoots = 9;

    public static short CalculateMaxHp(Character character, CoefficientData coefficient)
    {
        return CalculateMaxHp(character.Level, character.Stamina, coefficient.Hp, 0);
    }

    public static short CalculateMaxHp(byte level, byte stamina, double hpCoeff, short itemMaxHpBonus)
    {
        var lv = (int)level;
        var sta = (int)stamina;

        var maxHp = (short)((hpCoeff * lv * lv * sta)
            + 0.1 * (lv * sta) + (sta / 5.0) + 20 + itemMaxHpBonus);

        if (maxHp > MaxPlayerHp) maxHp = MaxPlayerHp;
        if (maxHp < 20) maxHp = 20;
        return maxHp;
    }

    public static short CalculateMaxMp(Character character, CoefficientData coefficient)
    {
        return CalculateMaxMp(character.Level, character.Intelligence, character.Stamina, coefficient, 0);
    }

    public static short CalculateMaxMp(byte level, byte intelligence, byte stamina, CoefficientData coefficient, short itemMaxMpBonus)
    {
        if (coefficient.Mp != 0)
        {
            var lv = (int)level;
            var intel = (int)intelligence + 30;
            return (short)((coefficient.Mp * lv * lv * intel)
                + (0.1 * lv * 2 * intel) + (intel / 5.0) + 20 + itemMaxMpBonus);
        }

        if (coefficient.Sp != 0)
        {
            var lv = (int)level;
            var sta = (int)stamina;
            return (short)((coefficient.Sp * lv * lv * sta)
                + (0.1 * lv * sta) + (sta / 5.0) + itemMaxMpBonus);
        }

        return 0;
    }

    public static DerivedStats Calculate(
        byte level, byte strength, byte stamina, byte dexterity, byte intelligence,
        short classId, CoefficientData coefficient, ItemSlot[] inventory, IGameDataService gameData,
        byte[]? skillPoints = null, AchievementTitleData? title = null,
        RebirthBonus? rebirth = null)
    {
        var stats = new DerivedStats();

        // Phase 1: Accumulate item bonuses (SetSlotItemValue)
        short itemAc = 0;
        short itemHitrate = 100;
        short itemEvasionrate = 100;
        short itemMaxHp = 0, itemMaxMp = 0;
        short itemStrB = 0, itemStaB = 0, itemDexB = 0, itemIntB = 0, itemChaB = 0;
        if (title != null)
        {
            itemStrB += title.Strength;
            itemStaB += title.Hp;
            itemDexB += title.Dexterity;
            itemIntB += title.Intelligence;
            itemChaB += title.Magic;
        }
        if (rebirth != null)
        {
            itemStrB += rebirth.Strength;
            itemStaB += rebirth.Stamina;
            itemDexB += rebirth.Dexterity;
            itemIntB += rebirth.Intelligence;
            itemChaB += rebirth.Magic;
        }
        short fireR = 0, coldR = 0, lightningR = 0, magicR = 0, poisonR = 0, curseR = 0;
        short daggerR = 0, jamadarR = 0, swordR = 0, axeR = 0, maceR = 0, spearR = 0, bowR = 0;
        int totalWeight = 0;

        // Set item tracking: Race >= 100 items build a set ID from slot bitmask
        var setItems = new Dictionary<byte, int>(); // Race -> computed set ID
        var setTotals = new SetItemTotals();

        for (int i = 0; i < InventoryConstants.InventoryTotal; i++)
        {
            var slot = inventory[i];
            if (slot.IsEmpty) continue;

            var itemProto = gameData.GetItem(slot.ItemId);
            if (itemProto == null) continue;

            // Weight: all items contribute to weight
            totalWeight += itemProto.Weight * slot.Count;

            // Only equipped items (slots 0-13) and cospre (42-46) apply stat bonuses
            if (i >= InventoryConstants.SlotMax && i < InventoryConstants.CospreStart)
                continue; // inventory items don't give stat bonuses
            if (i >= InventoryConstants.MagicBagStart)
                continue; // magic bag items don't give stat bonuses

            var ac = (short)itemProto.Ac;
            if (slot.Durability == 0) ac /= 10; // broken items have reduced AC

            itemMaxHp += itemProto.MaxHpB;
            itemMaxMp += itemProto.MaxMpB;
            itemAc += ac;
            itemStrB += itemProto.StrB;
            itemStaB += itemProto.StaB;
            itemDexB += itemProto.DexB;
            itemIntB += itemProto.IntelB;
            itemChaB += itemProto.ChaB;
            itemHitrate += itemProto.Hitrate;
            itemEvasionrate += itemProto.Evasionrate;

            fireR += itemProto.FireR;
            coldR += itemProto.ColdR;
            lightningR += itemProto.LightningR;
            magicR += itemProto.MagicR;
            poisonR += itemProto.PoisonR;
            curseR += itemProto.CurseR;

            daggerR += itemProto.DaggerAc;
            jamadarR += itemProto.JamadarAc;
            swordR += itemProto.SwordAc;
            axeR += itemProto.AxeAc;
            maceR += itemProto.MaceAc;
            spearR += itemProto.SpearAc;
            bowR += itemProto.BowAc;

            if (itemProto.Category == ItemKind.Cospre)
            {
                var cospreSet = gameData.GetSetItem(itemProto.Num);
                if (cospreSet != null)
                    ApplySetItemBonus(cospreSet, setTotals, ref itemMaxHp, ref itemMaxMp,
                        ref itemStrB, ref itemStaB, ref itemDexB, ref itemIntB, ref itemChaB,
                        ref itemAc, ref fireR, ref coldR, ref lightningR, ref magicR, ref poisonR, ref curseR);
            }

            // Armor set items: Race >= 100, accumulate slot bitmask per race
            if (itemProto.Race >= 100)
            {
                if (!setItems.ContainsKey(itemProto.Race))
                    setItems[itemProto.Race] = itemProto.Race * 10000;

                setItems[itemProto.Race] += GetSetItemSlotBitmask(itemProto.Slot);
            }
        }

        // Apply set bonuses for each tracked armor set
        foreach (var (_, setIndex) in setItems)
        {
            var setItem = gameData.GetSetItem(setIndex);
            if (setItem == null) continue;

            ApplySetItemBonus(setItem, setTotals, ref itemMaxHp, ref itemMaxMp,
                ref itemStrB, ref itemStaB, ref itemDexB, ref itemIntB, ref itemChaB,
                ref itemAc, ref fireR, ref coldR, ref lightningR, ref magicR, ref poisonR, ref curseR);
        }

        stats.ApBonusClassType = setTotals.ApBonusClassType;
        stats.ApBonusClassPercent = setTotals.ApBonusClassPercent;
        stats.AcBonusClassType = setTotals.AcBonusClassType;
        stats.AcBonusClassPercent = setTotals.AcBonusClassPercent;
        stats.MaxWeightBonus = setTotals.MaxWeightBonus;
        stats.ItemExpBonusPercent = setTotals.ExpBonusPercent;
        stats.ItemCoinBonusPercent = setTotals.CoinBonusPercent;
        stats.ItemNpBonus = setTotals.NpBonus;

        // Phase 2: Calculate weapon damage and hit coefficient
        var (itemDamage, hitCoefficient) = CalculateEquippedWeaponStats(inventory, gameData, coefficient);

        // Phase 3: Compute derived stats
        int totalStr = strength + itemStrB;
        int totalDex = dexterity + itemDexB;

        // Max weight
        stats.MaxWeight = ((totalStr + level) * 50) + setTotals.MaxWeightBonus;

        // Attack power
        stats.TotalHit = CalculateTotalHitCore(
            level,
            strength,
            dexterity,
            classId,
            itemStrB,
            itemDexB,
            itemDamage,
            hitCoefficient);

        if (setTotals.ApBonusPercent > 0)
            stats.TotalHit = (ushort)Math.Min(
                ushort.MaxValue, stats.TotalHit * (100 + setTotals.ApBonusPercent) / 100);

        // Defense
        stats.TotalAc = (short)(coefficient.Ac * (level + itemAc));

        // HP/MP
        stats.MaxHp = CalculateMaxHp(level, stamina, coefficient.Hp, itemMaxHp);
        stats.MaxMp = CalculateMaxMp(level, intelligence, stamina, coefficient, itemMaxMp);

        // Item bonuses
        stats.StrBonus = itemStrB;
        stats.StaBonus = itemStaB;
        stats.DexBonus = itemDexB;
        stats.IntBonus = itemIntB;
        stats.ChaBonus = itemChaB;

        // Resistances
        stats.FireR = fireR;
        stats.ColdR = coldR;
        stats.LightningR = lightningR;
        stats.MagicR = magicR;
        stats.DiseaseR = curseR;
        stats.PoisonR = poisonR;

        // Weapon-type resistances
        stats.DaggerR = daggerR;
        stats.JamadarR = jamadarR;
        stats.SwordR = swordR;
        stats.AxeR = axeR;
        stats.MaceR = maceR;
        stats.SpearR = spearR;
        stats.BowR = bowR;

        stats.ItemWeight = totalWeight;

        // Hitrate / Evasionrate: (1 + coeff * level * dex) * itemRate/100
        // Formula: (1 + coeff.Hitrate * level * dex) * itemHitrate/100
        int totalDexForRate = dexterity + itemDexB;
        stats.TotalHitrate = (float)((1 + coefficient.Hitrate * level * totalDexForRate) * itemHitrate / 100.0);
        stats.TotalEvasionrate = (float)((1 + coefficient.Evasionrate * level * totalDexForRate) * itemEvasionrate / 100.0);
        if (stats.TotalHitrate < 1) stats.TotalHitrate = 1;
        if (stats.TotalEvasionrate < 1) stats.TotalEvasionrate = 1;

        if (skillPoints is { Length: >= MagicSkillTreeSlots })
            ApplyDefenceTreeBonuses(stats, classId, skillPoints, inventory, gameData);

        ApplyStatThresholdBonuses(stats, stamina, intelligence);

        if (title != null)
            ApplyTitleBonuses(stats, title);

        return stats;
    }

    public static ushort CalculateTotalHitWithWeaponDamageBonus(
        byte level,
        byte strength,
        byte dexterity,
        short classId,
        CoefficientData coefficient,
        ItemSlot[] inventory,
        IGameDataService gameData,
        short itemStrBonus,
        short itemDexBonus,
        short flatWeaponDamageBonus)
    {
        var (itemDamage, hitCoefficient) = CalculateEquippedWeaponStats(inventory, gameData, coefficient, flatWeaponDamageBonus);
        var totalHit = CalculateTotalHitCore(
            level,
            strength,
            dexterity,
            classId,
            itemStrBonus,
            itemDexBonus,
            itemDamage,
            hitCoefficient);

        if (flatWeaponDamageBonus > 0 && totalHit < ushort.MaxValue)
            totalHit++;

        return totalHit;
    }

    private static bool IsWarrior(short classId) => classId / 100 is 1 or 5;

    private static bool CheckSkillPoint(byte[] skillPoints, int skillIndex, int min, int max)
    {
        if (skillIndex < 0 || skillIndex >= skillPoints.Length) return false;
        return skillPoints[skillIndex] >= min && skillPoints[skillIndex] <= max;
    }

    private const int MagicSkillTreeSlots = 9;

    private static void ApplyStatThresholdBonuses(DerivedStats stats, byte stamina, byte intelligence)
    {
        if (stamina > StatBonusThreshold)
            stats.TotalAc += (short)(stamina - StatBonusThreshold);

        if (intelligence > StatBonusThreshold)
            stats.ResistanceBonus += (short)((intelligence - StatBonusThreshold) / 2);
    }

    private static void ApplyDefenceTreeBonuses(
        DerivedStats stats, short classId, byte[] skillPoints,
        ItemSlot[] inventory, IGameDataService gameData)
    {
        const int ProSkill2 = 6; // Defense skill tree index

        if (IsWarrior(classId))
        {
            int defenseBonus = 0;
            int resistanceBonus = 0;

            // Passive defense bonus based on defense skill tree points
            if (CheckSkillPoint(skillPoints, ProSkill2, 70, 83))
                defenseBonus = 50; // Could be 60 with level 70 skill quest (CheckExistEvent(51, 2))
            else if (CheckSkillPoint(skillPoints, ProSkill2, 55, 69))
                defenseBonus = 50;
            else if (CheckSkillPoint(skillPoints, ProSkill2, 35, 54))
                defenseBonus = 40;
            else if (CheckSkillPoint(skillPoints, ProSkill2, 15, 34))
                defenseBonus = 30;
            else if (CheckSkillPoint(skillPoints, ProSkill2, 5, 14))
                defenseBonus = 20;

            // Passive resistance bonus based on defense skill tree points
            if (CheckSkillPoint(skillPoints, ProSkill2, 40, 83))
                resistanceBonus = 90;
            else if (CheckSkillPoint(skillPoints, ProSkill2, 20, 39))
                resistanceBonus = 60;
            else if (CheckSkillPoint(skillPoints, ProSkill2, 10, 19))
                resistanceBonus = 30;

            // Bonuses halved if no shield equipped
            var leftHand = GetEquippedItemProto(inventory, InventoryConstants.LeftHand, gameData);
            if (leftHand == null || !leftHand.IsShield())
            {
                defenseBonus /= 2;
                resistanceBonus /= 2;
            }

            stats.TotalAc += (short)(defenseBonus * stats.TotalAc / 100);
            stats.ResistanceBonus += (short)resistanceBonus;
        }
    }

    private const byte StatBonusThreshold = 100;

    private static ItemData? GetEquippedItemProto(ItemSlot[] inventory, int slot, IGameDataService gameData)
    {
        if (slot < 0 || slot >= InventoryConstants.SlotMax) return null;
        var item = inventory[slot];
        if (item.IsEmpty) return null;
        return gameData.GetItem(item.ItemId);
    }

    private static (ushort ItemDamage, float HitCoefficient) CalculateEquippedWeaponStats(
        ItemSlot[] inventory,
        IGameDataService gameData,
        CoefficientData coefficient,
        short flatWeaponDamageBonus = 0)
    {
        ushort itemDamage = 0;
        float hitCoefficient = 0f;

        var rightHand = GetEquippedItemProto(inventory, InventoryConstants.RightHand, gameData);
        if (rightHand != null)
        {
            hitCoefficient = GetWeaponCoefficient(rightHand.Category, coefficient);
            itemDamage += (ushort)Math.Max(0, (int)rightHand.Damage + flatWeaponDamageBonus);
        }

        var leftHand = GetEquippedItemProto(inventory, InventoryConstants.LeftHand, gameData);
        if (leftHand != null)
        {
            if (leftHand.IsBow())
            {
                hitCoefficient = (float)coefficient.Bow;
                itemDamage = (ushort)Math.Max(0, (int)leftHand.Damage + flatWeaponDamageBonus);
            }
            else if (!leftHand.IsShield())
            {
                itemDamage += (ushort)(Math.Max(0, (int)leftHand.Damage + flatWeaponDamageBonus) / 2);
            }
        }

        if (itemDamage < 3)
            itemDamage = 3;

        return (itemDamage, hitCoefficient);
    }

    private static ushort CalculateTotalHitCore(
        byte level,
        byte strength,
        byte dexterity,
        short classId,
        short itemStrBonus,
        short itemDexBonus,
        ushort itemDamage,
        float hitCoefficient)
    {
        int tempStr = strength;
        int tempDex = dexterity;

        uint baseAP = 0;
        uint additionalAP = 3;
        if (tempStr > 150)
            baseAP = (uint)(tempStr - 150);
        if (tempStr == 160)
            baseAP--;

        int totalStr = tempStr + itemStrBonus;
        int totalDex = tempDex + itemDexBonus;

        bool isRogue = classId / 100 == 5 || classId / 100 == 7; // rogue classes: 5xx, 7xx
        uint apStat = isRogue ? (uint)totalDex : (uint)totalStr;
        if (!isRogue)
            additionalAP += baseAP;

        var totalHit = (ushort)((0.005f * itemDamage * (apStat + 40)) + (hitCoefficient * itemDamage * level * apStat));
        return (ushort)(totalHit + additionalAP);
    }

    private static float GetWeaponCoefficient(ItemKind kind, CoefficientData coeff) => kind switch
    {
        ItemKind.Dagger => (float)coeff.ShortSword,
        ItemKind.Jamadhar => (float)coeff.Jamadar,
        ItemKind.SwordOneHand or ItemKind.SwordTwoHand => (float)coeff.Sword,
        ItemKind.AxeOneHand or ItemKind.AxeTwoHand => (float)coeff.Axe,
        ItemKind.ClubOneHand or ItemKind.ClubTwoHand => (float)coeff.Club,
        ItemKind.SpearOneHand or ItemKind.SpearTwoHand => (float)coeff.Spear,
        ItemKind.Mace => (float)coeff.Pole,
        ItemKind.Staff => (float)coeff.Staff,
        ItemKind.Bow or ItemKind.Crossbow or ItemKind.LongBow or ItemKind.Launcher => (float)coeff.Bow,
        _ => 0f,
    };

    private static int GetSetItemSlotBitmask(byte slot) => slot switch
    {
        SlotHelmet => 2,
        SlotPauldron => 16,
        SlotPads => 512,
        SlotGloves => 2048,
        SlotBoots => 4096,
        _ => 0
    };

    private sealed class SetItemTotals
    {
        public short MaxWeightBonus;
        public short ApBonusPercent;
        public short ExpBonusPercent;
        public short CoinBonusPercent;
        public short NpBonus;
        public short ApBonusClassType;
        public short ApBonusClassPercent;
        public short AcBonusClassType;
        public short AcBonusClassPercent;
    }

    private static void ApplySetItemBonus(
        SetItemData setItem,
        SetItemTotals totals,
        ref short itemMaxHp, ref short itemMaxMp,
        ref short itemStrB, ref short itemStaB, ref short itemDexB, ref short itemIntB, ref short itemChaB,
        ref short itemAc,
        ref short fireR, ref short coldR, ref short lightningR, ref short magicR, ref short poisonR, ref short curseR)
    {
        itemMaxHp += setItem.HPBonus;
        itemMaxMp += setItem.MPBonus;
        itemStrB += setItem.StrengthBonus;
        itemStaB += setItem.StaminaBonus;
        itemDexB += setItem.DexterityBonus;
        itemIntB += setItem.IntelBonus;
        itemChaB += setItem.CharismaBonus;
        itemAc += setItem.ACBonus;
        fireR += setItem.FlameResistance;
        coldR += setItem.GlacierResistance;
        lightningR += setItem.LightningResistance;
        magicR += setItem.MagicResistance;
        poisonR += setItem.PoisonResistance;
        curseR += setItem.CurseResistance;
        totals.MaxWeightBonus += setItem.MaxWeightBonus;
        totals.ApBonusPercent += setItem.APBonusPercent;
        totals.ExpBonusPercent += setItem.XPBonusPercent;
        totals.CoinBonusPercent += setItem.CoinBonusPercent;
        totals.NpBonus += setItem.NPBonus;
        if (setItem.APBonusClassPercent > 0 && SetItemData.IsJobGroup(setItem.APBonusClassType))
        {
            totals.ApBonusClassType = setItem.APBonusClassType;
            totals.ApBonusClassPercent += setItem.APBonusClassPercent;
        }
        if (setItem.ACBonusClassPercent > 0 && SetItemData.IsJobGroup(setItem.ACBonusClassType))
        {
            totals.AcBonusClassType = setItem.ACBonusClassType;
            totals.AcBonusClassPercent += setItem.ACBonusClassPercent;
        }
    }

    public static void ApplyTitleBonuses(DerivedStats stats, AchievementTitleData title)
    {
        stats.TotalHit = (ushort)Math.Max(0, stats.TotalHit + title.Attack);
        stats.TotalAc += title.Defence;
        stats.FireR += title.FireResist;
        stats.ColdR += title.IceResist;
        stats.LightningR += title.LightResist;
        stats.MagicR += title.MagicResist;
        stats.DiseaseR += title.CurseResist;
        stats.PoisonR += title.PoisonResist;
        stats.DaggerR += title.ShortSwordAc;
        stats.JamadarR += title.JamadarAc;
        stats.SwordR += title.SwordAc;
        stats.AxeR += title.AxeAc;
        stats.MaceR += title.BlowAc;
        stats.SpearR += title.SpearAc;
        stats.BowR += title.ArrowAc;
    }

}

public class DerivedStats
{
    public ushort TotalHit { get; set; }
    public short TotalAc { get; set; }
    public int MaxWeight { get; set; }
    public short MaxHp { get; set; }
    public short MaxMp { get; set; }
    public short StrBonus { get; set; }
    public short StaBonus { get; set; }
    public short DexBonus { get; set; }
    public short IntBonus { get; set; }
    public short ChaBonus { get; set; }
    public short FireR { get; set; }
    public short ColdR { get; set; }
    public short LightningR { get; set; }
    public short MagicR { get; set; }
    public short DiseaseR { get; set; }
    public short PoisonR { get; set; }
    public int ItemWeight { get; set; }
    public float TotalHitrate { get; set; }
    public float TotalEvasionrate { get; set; }
    public short ResistanceBonus { get; set; }
    public short MaxWeightBonus { get; set; }
    public short ApBonusClassType { get; set; }
    public short ApBonusClassPercent { get; set; }
    public short AcBonusClassType { get; set; }
    public short AcBonusClassPercent { get; set; }
    public short ItemExpBonusPercent { get; set; }
    public short ItemCoinBonusPercent { get; set; }
    public short ItemNpBonus { get; set; }
    public short DaggerR { get; set; }
    public short JamadarR { get; set; }
    public short SwordR { get; set; }
    public short AxeR { get; set; }
    public short MaceR { get; set; }
    public short SpearR { get; set; }
    public short BowR { get; set; }
}
