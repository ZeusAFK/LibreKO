using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;

namespace LibreKO.Game.Protocol;

internal static class MagicBuffClassifier
{
    private const short NeutralPercent = 100;

    public static bool IsBuff(MagicType4Data type4Data) =>
        (BuffType)type4Data.BuffType switch
        {
            BuffType.None => true,

            BuffType.HpMp => type4Data.MaxHP > 0
                || type4Data.MaxMP > 0
                || (type4Data.MaxHPPct >= NeutralPercent && type4Data.MaxMPPct >= NeutralPercent),

            BuffType.Ac or BuffType.WeaponAc => type4Data.Ac == 0 && type4Data.AcPct > 0
                ? type4Data.AcPct >= NeutralPercent
                : type4Data.Ac >= 0,

            BuffType.Damage => type4Data.Attack >= NeutralPercent,
            BuffType.AttackSpeed => type4Data.AttackSpeed >= NeutralPercent,
            BuffType.Speed => type4Data.Speed >= NeutralPercent,
            BuffType.MagicPower => type4Data.MagicAttack >= NeutralPercent,
            BuffType.WeaponDamage => type4Data.Attack > 0,

            BuffType.Stats or BuffType.BattleCry => type4Data.Str >= 0
                && type4Data.Sta >= 0
                && type4Data.Dex >= 0
                && type4Data.Intel >= 0
                && type4Data.Cha >= 0,

            BuffType.Accuracy => type4Data.HitRate >= NeutralPercent
                && type4Data.AvoidRate >= NeutralPercent,

            BuffType.Size
                or BuffType.Jackpot
                or BuffType.Resistances
                or BuffType.Experience
                or BuffType.Weight
                or BuffType.Loyalty
                or BuffType.NoahBonus
                or BuffType.PremiumMerchant
                or BuffType.AttackSpeedArmor
                or BuffType.DamageDouble
                or BuffType.InstantMagic
                or BuffType.MageArmor
                or BuffType.ProhibitInvis
                or BuffType.ResisAndMagicDmg
                or BuffType.TripleAcHalfSpeed
                or BuffType.BlockCurse
                or BuffType.BlockCurseReflect
                or BuffType.ManaAbsorb
                or BuffType.FragmentOfManes
                or BuffType.VariousEffects
                or BuffType.PassionOfSoul
                or BuffType.FirmDetermination
                or BuffType.Armored
                or BuffType.UnknownExperience
                or BuffType.AttackRangeArmor
                or BuffType.MirrorDamageParty
                or BuffType.LoyaltyAmount
                or BuffType.BlockPhysicalDamage
                or BuffType.BlockMagicalDamage
                or BuffType.UnknownPotion
                or BuffType.InvisibilityPotion
                or BuffType.GodsBlessing
                or BuffType.HelpCompensation
                or BuffType.Fishing
                or BuffType.ImirRoars
                or BuffType.LogosHorns
                or BuffType.SnowmanTiti
                or BuffType.IncreaseAttack
                or BuffType.DevilTransform
                or BuffType.BlessOfTemple
                or BuffType.HellFireDragon
                or BuffType.NpDropNoah
                or BuffType.DivideArmor
                or BuffType.RewardMask
                or BuffType.MagicSpell => true,

            _ => false
        };
}
