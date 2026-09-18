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
    bool IsPlayer)
{
    private const int PlayerDamageScale = 485;
    private const int PlayerResistanceSoftening = 510;
    private const int NpcDamageScale = 555;
    private const int NpcResistanceSoftening = 515;
    private const int NoDamageReduction = 100;

    public static MagicDefender Of(UserSession target, MagicAttribute attribute) => new(
        ResistanceOf(target.Stats, attribute) + target.Stats.ResistanceBonus,
        PlayerDamageScale,
        PlayerResistanceSoftening,
        target.MagicDamageReduction,
        target.BlockMagic,
        IsPlayer: true);

    public static MagicDefender Of(NpcInstance target, MagicAttribute attribute) => new(
        ResistanceOf(target, attribute),
        NpcDamageScale,
        NpcResistanceSoftening,
        NoDamageReduction,
        BlocksMagic: false,
        IsPlayer: false);

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
    private const float BaseDamageShare = 0.85f;
    private const float RandomDamageShare = 0.3f;
    private const float CharismaDivisor = 102.5f;
    private const float ElementalBaseShare = 0.8f;
    private const int ElementalLevelDivisor = 60;
    private const int WarZoneDivisor = 3;
    private const int OpenWorldDivisor = 2;
    private const int NpcDivisor = 2;

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

        var totalHit = ScaleTotalHit(caster, rawDamage);
        var damage = defender.DamageScale * totalHit
            / (defender.Resistance + defender.ResistanceSoftening);

        var random = Random.Shared.Next(0, Math.Max(1, damage));
        damage = (int)(random * RandomDamageShare + damage * BaseDamageShare)
            + caster.MagicAttackAmount + PercentScale;

        if (attribute != MagicAttribute.Magic)
            damage += ElementalWeaponBonus(caster, attribute, gameData);

        if (defender.DamageReduction < PercentScale)
            damage = damage * defender.DamageReduction / PercentScale;

        damage = Math.Max(MinimumLandedDamage, damage / Divisor(caster, defender));
        return Math.Clamp(damage, 0, CombatUtils.MaxDamage);
    }

    private static int Divisor(UserSession caster, MagicDefender defender)
    {
        if (!defender.IsPlayer)
            return NpcDivisor;

        var isWarZone = BattleZoneManager.IsBattleZone(caster.ZoneId)
                     || BattleZoneManager.IsPvpZone(caster.ZoneId);
        return isWarZone ? WarZoneDivisor : OpenWorldDivisor;
    }

    private static int ScaleTotalHit(UserSession caster, int rawDamage)
    {
        var totalHit = rawDamage;
        if (ClassIdHelper.IsMage(caster.Class))
        {
            var charisma = caster.Magic + caster.Stats.ChaBonus;
            totalHit = (int)Math.Ceiling(rawDamage * charisma / CharismaDivisor);
        }

        return totalHit * (caster.MagicAttackAmount + PercentScale) / PercentScale;
    }

    private static int ElementalWeaponBonus(
        UserSession caster, MagicAttribute attribute, IGameDataService gameData)
    {
        var (staffDamage, attributeDamage) = GetStaffAndAttributeDamage(caster, attribute, gameData);
        return ElementalScaling(staffDamage, caster.Level)
             + ElementalScaling(attributeDamage, caster.Level);
    }

    private static int ElementalScaling(int amount, byte level) =>
        (int)(amount * ElementalBaseShare + amount * level / ElementalLevelDivisor);

    private static (int StaffDamage, int AttributeDamage) GetStaffAndAttributeDamage(
        UserSession caster, MagicAttribute attribute, IGameDataService gameData)
    {
        var staffDamage = 0;
        var attributeDamage = 0;

        if (caster.WeaponsDisabled)
            return (staffDamage, attributeDamage);

        var rightSlot = caster.Inventory[InventoryConstants.RightHand];
        var leftSlot = caster.Inventory[InventoryConstants.LeftHand];

        if (!rightSlot.IsEmpty && leftSlot.IsEmpty)
        {
            var rightHand = gameData.GetItem(rightSlot.ItemId);
            if (rightHand != null && rightHand.Category == ItemKind.Staff)
            {
                staffDamage = rightHand.Damage;
                attributeDamage = ElementalDamage(rightHand, attribute);
            }
        }

        for (var slot = 0; slot < InventoryConstants.SlotMax; slot++)
        {
            if (slot == InventoryConstants.RightHand || slot == InventoryConstants.LeftHand)
                continue;

            var itemSlot = caster.Inventory[slot];
            if (itemSlot.IsEmpty)
                continue;

            var item = gameData.GetItem(itemSlot.ItemId);
            if (item == null)
                continue;

            attributeDamage += ElementalDamage(item, attribute);
        }

        return (staffDamage, attributeDamage);
    }

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
