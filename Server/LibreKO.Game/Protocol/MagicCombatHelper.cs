using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

internal readonly record struct MagicDefender(
    int Resistance,
    int DamageScale,
    int ResistanceSoftening,
    int DamageReduction,
    bool BlocksMagic,
    bool IsPlayer,
    int Armour,
    short ClassId = 0,
    short AcBonusClassType = 0,
    short AcBonusClassPercent = 0)
{
    private const int WarriorDamageScale = 445;
    private const int RogueDamageScale = 485;
    private const int MageDamageScale = 455;
    private const int PriestDamageScale = 400;
    private const int KurianDamageScale = 445;
    private const int PlayerResistanceSoftening = 510;
    private const int MageResistanceSoftening = 515;
    private const int NpcDamageScale = 555;
    private const int NpcResistanceSoftening = 515;
    private const int NoDamageReduction = 100;

    public static MagicDefender Of(UserSession target, MagicAttribute attribute)
    {
        var (scale, softening) = ScaleFor(target.Class);
        return new(
            ResistanceOf(target.Stats, attribute) + target.Stats.ResistanceBonus,
            scale,
            softening,
            target.MagicDamageReduction,
            target.BlockMagic,
            IsPlayer: true,
            target.Stats.TotalAc,
            target.Class,
            target.Stats.AcBonusClassType,
            target.Stats.AcBonusClassPercent);
    }

    public static MagicDefender Of(NpcInstance target, MagicAttribute attribute) => new(
        ResistanceOf(target, attribute),
        NpcDamageScale,
        NpcResistanceSoftening,
        NoDamageReduction,
        BlocksMagic: false,
        IsPlayer: false,
        target.TotalAc);

    private static (int Scale, int Softening) ScaleFor(short classId) =>
        ClassIdHelper.IsWarrior(classId) ? (WarriorDamageScale, PlayerResistanceSoftening)
        : ClassIdHelper.IsPriest(classId) ? (PriestDamageScale, PlayerResistanceSoftening)
        : ClassIdHelper.IsMage(classId) ? (MageDamageScale, MageResistanceSoftening)
        : ClassIdHelper.IsPortuKurian(classId) ? (KurianDamageScale, PlayerResistanceSoftening)
        : (RogueDamageScale, PlayerResistanceSoftening);

    private static int ResistanceOf(DerivedStats stats, MagicAttribute attribute) => attribute switch
    {
        MagicAttribute.Fire => Math.Max(0, (int)stats.FireR),
        MagicAttribute.Cold => Math.Max(0, (int)stats.ColdR),
        MagicAttribute.Lightning => Math.Max(0, (int)stats.LightningR),
        MagicAttribute.Magic => Math.Max(0, (int)stats.MagicR),
        MagicAttribute.Disease => Math.Max(0, (int)stats.DiseaseR),
        MagicAttribute.Poison => Math.Max(0, (int)stats.PoisonR),
        _ => 0,
    };

    private static int ResistanceOf(NpcInstance npc, MagicAttribute attribute) => attribute switch
    {
        MagicAttribute.Fire => Math.Max(0, (int)npc.FireR),
        MagicAttribute.Cold => Math.Max(0, (int)npc.ColdR),
        MagicAttribute.Lightning => Math.Max(0, (int)npc.LightningR),
        MagicAttribute.Magic => Math.Max(0, (int)npc.MagicR),
        MagicAttribute.Disease => Math.Max(0, (int)npc.CurseR),
        MagicAttribute.Poison => Math.Max(0, (int)npc.PoisonR),
        _ => 0,
    };
}

internal static class MagicCombatHelper
{
    internal const int PercentScale = 100;
    private const int MinimumLandedDamage = 1;
    private const int CharismaFloor = 86;
    private const float CharismaDivisor = 186f;
    private const int MagicAmountWeight = 3;
    private const int RandomRangeDivisor = 2;
    private const float RandomDamageShare = 0.1f;
    private const float BaseDamageShare = 0.85f;
    private const float WeaponBaseShare = 0.8f;
    private const float WeaponLevelDivisor = 60f;
    private const float ElementLevelDivisor = 30f;
    private const float StrengthLevelDivisor = 5.5f;
    private const int StaffElementThreshold = 80;
    private const float StaffElementBoost = 1.5f;
    private const int PlayerDivisor = 3;
    private const int KurianAttackDivisor = 5;
    private const int ArmourPenetration = 2;
    private const int ArmourSoftening = 240;
    private const int KurianScaleDivisor = 80;

    public static int GetSkillAttackPower(UserSession caster, bool isPlayerTarget)
    {
        var attackPower = caster.Stats.TotalHit * caster.AttackAmount;
        if (isPlayerTarget)
            attackPower = attackPower * caster.PlayerAttackAmount / PercentScale;

        return attackPower;
    }

    public static int GetMagicDamage(
        UserSession caster, UserSession target, int rawDamage, byte attribute,
        IGameDataService gameData)
    {
        var element = (MagicAttribute)attribute;
        return Calculate(caster, MagicDefender.Of(target, element), rawDamage, element, gameData);
    }

    public static int GetMagicDamage(
        UserSession caster, NpcInstance target, int rawDamage, byte attribute,
        IGameDataService gameData)
    {
        var element = (MagicAttribute)attribute;
        return Calculate(caster, MagicDefender.Of(target, element), rawDamage, element, gameData);
    }

    private static int Calculate(
        UserSession caster, MagicDefender defender, int rawDamage, MagicAttribute attribute,
        IGameDataService gameData)
    {
        if (rawDamage <= 0 || defender.BlocksMagic)
            return 0;

        var totalHit = rawDamage;
        var magicAmount = 0;
        if (ClassIdHelper.IsMage(caster.Class))
        {
            var charisma = caster.Magic + caster.Stats.ChaBonus;
            magicAmount = Math.Max(charisma - CharismaFloor, 0) + caster.MagicAttackAmount;
            totalHit = (int)(rawDamage * charisma / CharismaDivisor);
        }

        var damage = defender.DamageScale * totalHit / (defender.Resistance + defender.ResistanceSoftening);
        if (ClassIdHelper.IsPortuKurian(caster.Class))
            damage = KurianScale(caster, defender, damage);

        var random = Random.Shared.Next(0, Math.Max(0, damage / RandomRangeDivisor) + 1);
        damage = (int)(random * RandomDamageShare + damage * BaseDamageShare) + magicAmount * MagicAmountWeight;
        damage += WeaponBonus(caster, defender, attribute, gameData);

        if (defender.DamageReduction < PercentScale)
            damage = damage * defender.DamageReduction / PercentScale;

        if (defender.IsPlayer)
            damage /= PlayerDivisor;

        return Math.Clamp(damage, MinimumLandedDamage, CombatUtils.MaxDamage);
    }

    private static int KurianScale(UserSession caster, MagicDefender defender, int damage)
    {
        var attack = caster.Stats.TotalHit * caster.AttackAmount / KurianAttackDivisor;
        var armour = Math.Max(0, defender.Armour);
        if (!defender.IsPlayer)
            return (int)(attack * ArmourPenetration / (armour + ArmourSoftening) * (damage / (float)KurianScaleDivisor));

        if (defender.AcBonusClassPercent > 0 && ClassIdHelper.IsSameJobGroup(caster.Class, (byte)defender.AcBonusClassType))
            armour = armour * (PercentScale + defender.AcBonusClassPercent) / PercentScale;
        if (caster.Stats.ApBonusClassPercent > 0
            && ClassIdHelper.IsSameJobGroup(defender.ClassId, (byte)caster.Stats.ApBonusClassType))
            attack = attack * (PercentScale + caster.Stats.ApBonusClassPercent) / PercentScale;

        return attack * ArmourPenetration / (armour + ArmourSoftening) * (damage / KurianScaleDivisor);
    }

    private static int WeaponBonus(
        UserSession caster, MagicDefender defender, MagicAttribute attribute, IGameDataService gameData)
    {
        var rightHand = ItemAt(caster, InventoryConstants.RightHand, gameData);
        var leftHand = ItemAt(caster, InventoryConstants.LeftHand, gameData);
        var weaponDamage = 0;
        var elementDamage = 0;

        if (!caster.WeaponsDisabled && rightHand?.Category == ItemKind.Staff && leftHand == null)
        {
            weaponDamage = rightHand.Damage + UserSessionMagicState.WeaponDamageBonus(caster);
            elementDamage = ElementalDamage(rightHand, attribute);
            if (elementDamage >= StaffElementThreshold)
                elementDamage = (int)(elementDamage * StaffElementBoost);
        }

        for (var slot = 0; slot < InventoryConstants.SlotMax; slot++)
        {
            if (slot == InventoryConstants.RightHand || slot == InventoryConstants.LeftHand)
                continue;

            if (ItemAt(caster, slot, gameData) is { } item)
                elementDamage += ElementalDamage(item, attribute);
        }

        var kurian = ClassIdHelper.IsPortuKurian(caster.Class);
        if (kurian && !caster.WeaponsDisabled)
            weaponDamage += (leftHand?.Damage ?? 0) + (rightHand?.Damage ?? 0) + UserSessionMagicState.WeaponDamageBonus(caster);

        if (kurian && defender.IsPlayer && IsDevil(caster) && IsHeavyWeapon(rightHand) && leftHand != null)
        {
            var strength = caster.Strength + caster.Stats.StrBonus;
            return (int)(Scaled(weaponDamage, WeaponLevelDivisor, caster.Level)
                         + strength * WeaponBaseShare + strength * caster.Level / StrengthLevelDivisor);
        }

        if (attribute == MagicAttribute.Magic)
            return 0;

        return (int)(Scaled(weaponDamage, WeaponLevelDivisor, caster.Level)
                     + Scaled(elementDamage, ElementLevelDivisor, caster.Level));
    }

    private static float Scaled(int amount, float levelDivisor, byte level) =>
        amount * WeaponBaseShare + amount * level / levelDivisor;

    private static ItemData? ItemAt(UserSession caster, int slot, IGameDataService gameData) =>
        caster.Inventory[slot] is { IsEmpty: false } item ? gameData.GetItem(item.ItemId) : null;

    private static bool IsDevil(UserSession caster) =>
        caster.ActiveBuffs.Values.Any(buff => buff.BuffType == BuffType.DevilTransform && !buff.IsExpired);

    private static bool IsHeavyWeapon(ItemData? item) => item?.Category is ItemKind.SwordOneHand
        or ItemKind.SwordTwoHand or ItemKind.AxeOneHand or ItemKind.AxeTwoHand or ItemKind.Mace
        or ItemKind.SpearOneHand or ItemKind.SpearTwoHand;

    private static int ElementalDamage(ItemData item, MagicAttribute attribute) => attribute switch
    {
        MagicAttribute.Fire => item.FireDamage,
        MagicAttribute.Cold => item.IceDamage,
        MagicAttribute.Lightning => item.LightningDamage,
        MagicAttribute.Poison => item.PoisonDamage,
        _ => 0,
    };

    public static Task SendMagicFailAsync(UserSession session, int skillId) =>
        session.Client.SendPacket(MagicProcessPacketWriter.CreateFail(skillId, session.CharacterId));
}
