using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol;

namespace LibreKO.Game.World;

internal static class UserSessionMagicState
{
    public const int SavedMagicIdMin = 500001;

    private const byte StatusBuffMagicType = 4;

    public static bool SurvivesDeath(UserSession session, int magicId, ActiveBuff buff, IGameDataService gameData) =>
        magicId >= SavedMagicIdMin
        || ((gameData.GetMagic(magicId)?.UseItem ?? 0) != 0 && buff.CasterId == session.CharacterId);

    public static StatBonus BuffStatBonus(UserSession session)
    {
        short str = 0, sta = 0, dex = 0, intel = 0, cha = 0;
        foreach (var buff in session.ActiveBuffs.Values)
        {
            if (buff.IsExpired)
                continue;
            str += buff.BonusStr;
            sta += buff.BonusSta;
            dex += buff.BonusDex;
            intel += buff.BonusIntel;
            cha += buff.BonusCha;
        }
        return new StatBonus(str, sta, dex, intel, cha);
    }

    public static void ApplyBuffBonuses(UserSession session, IGameDataService gameData, CoefficientData coefficient)
    {
        ResetBuffFlags(session);

        // Percentage AC multiplier (default 100 = no change).
        // Various buff types modify this; applied to TotalAc after the loop.
        int acPct = 100;
        short flatAcBonus = 0;
        short flatWeaponDamageBonus = 0;
        short magicAttackBonus = 0;

        foreach (var buff in session.ActiveBuffs.Values)
        {
            if (buff.IsExpired)
                continue;

            // AC handling: per-type via two channels: percentage and flat.
            // Percentage is applied first, flat is added at damage time.
            // We apply both here since .NET recalculates from scratch each time.
            switch (buff.BuffType)
            {
                case BuffType.Ac:
                case BuffType.WeaponAc:
                    // If no flat AC and has percentage → percentage; else → flat
                    if (buff.BonusAc == 0 && buff.BonusAcPct > 0)
                        acPct += buff.BonusAcPct - 100;
                    else
                        flatAcBonus += buff.BonusAc;
                    break;
                case BuffType.AttackSpeedArmor:
                    // Flat AC + attack speed delta
                    flatAcBonus += buff.BonusAc;
                    break;
                case BuffType.TripleAcHalfSpeed:
                    // +300% to AC multiplier (100+300=400 → 4x AC)
                    acPct += 300;
                    break;
                case BuffType.KaulTransformation:
                    // Hardcoded +500 flat AC
                    flatAcBonus += 500;
                    break;
                case BuffType.AttackRangeArmor:
                    // Hardcoded +100 flat AC
                    flatAcBonus += 100;
                    break;
                case BuffType.WeaponDamage:
                    flatWeaponDamageBonus += buff.BonusAttack;
                    break;
                case BuffType.ReduceTarget:
                case BuffType.Undead:
                    // Debuff: adjusts AC percentage (typically reduces AC)
                    if (buff.BonusAcPct > 0)
                        acPct += buff.BonusAcPct - 100;
                    break;
                default:
                    // Generic fallback: apply flat AC for buff types not explicitly handled above.
                    // Data naturally has Ac=0 for types that don't use it, so this is safe.
                    flatAcBonus += buff.BonusAc;
                    break;
            }

            // MaxHP/MaxMP only apply when the buff type is HP_MP (canonical
            // gates BUFF_TYPE_HP_MP = 50 the same way).
            if (buff.BuffType == BuffType.HpMp)
            {
                session.Stats.MaxHp = ApplyBuffResourceBonus(session.Stats.MaxHp, buff.BonusMaxHp, buff.BonusMaxHpPct);
                session.Stats.MaxMp = ApplyBuffResourceBonus(session.Stats.MaxMp, buff.BonusMaxMp, buff.BonusMaxMpPct);
            }

            // Resistances (data naturally has 0 for non-resistance buff types)
            session.Stats.FireR += buff.BonusFireR;
            session.Stats.ColdR += buff.BonusColdR;
            session.Stats.LightningR += buff.BonusLightningR;
            session.Stats.MagicR += buff.BonusMagicR;
            session.Stats.PoisonR += buff.BonusPoisonR;
            session.Stats.DiseaseR += buff.BonusDiseaseR;

            if (buff.BonusMagicAttack != 0)
                magicAttackBonus += (short)(buff.BonusMagicAttack - 100);

            ApplyBuffTypeFlags(session, buff);
        }

        // Apply AC: percentage first, then flat
        if (acPct <= 0)
            session.Stats.TotalAc = 0;
        else if (acPct != 100)
            session.Stats.TotalAc = (short)(session.Stats.TotalAc * acPct / 100);

        session.Stats.TotalAc += flatAcBonus;

        session.MagicAttackAmount = magicAttackBonus;

        if (flatWeaponDamageBonus > 0)
        {
            session.Stats.TotalHit = AbilityCalculator.CalculateTotalHitWithWeaponDamageBonus(
                session.Level,
                session.Strength,
                session.Dexterity,
                session.Class,
                coefficient,
                session.Inventory,
                gameData,
                session.Stats.StrBonus,
                session.Stats.DexBonus,
                flatWeaponDamageBonus);
        }
    }

    public static byte[] SerializeSavedMagic(UserSession session)
    {
        var activeBuffs = session.ActiveBuffs
            .Where(entry => !entry.Value.IsExpired)
            .ToArray();

        if (activeBuffs.Length == 0)
            return [];

        var data = new byte[2 + activeBuffs.Length * 17];
        BitConverter.TryWriteBytes(data.AsSpan(0), (short)activeBuffs.Length);

        var offset = 2;
        foreach (var (magicId, buff) in activeBuffs)
        {
            BitConverter.TryWriteBytes(data.AsSpan(offset), magicId);
            BitConverter.TryWriteBytes(data.AsSpan(offset + 4), buff.CasterId);
            data[offset + 8] = (byte)buff.BuffType;
            BitConverter.TryWriteBytes(data.AsSpan(offset + 9), buff.SpecialAmount);

            var remainingMs = (int)Math.Max(0, (buff.ExpireTicks - DateTime.UtcNow.Ticks) / TimeSpan.TicksPerMillisecond);
            BitConverter.TryWriteBytes(data.AsSpan(offset + 13), remainingMs);
            offset += 17;
        }

        return data;
    }

    public static List<KeyValuePair<int, ActiveBuff>> LiveStatusBuffs(
        UserSession session, IGameDataService gameData) =>
        session.ActiveBuffs
            .Where(entry => !entry.Value.IsExpired
                && gameData.GetMagic(entry.Key)?.PrimaryType
                    is MagicSkillType.Buff or MagicSkillType.Transform)
            .ToList();

    public static int DropVolatileMagic(UserSession session, IGameDataService gameData)
    {
        var dropped = 0;
        foreach (var (magicId, buff) in session.ActiveBuffs)
        {
            if (SurvivesDeath(session, magicId, buff, gameData))
                continue;

            if (session.ActiveBuffs.TryRemove(magicId, out _))
                dropped++;
        }

        dropped += session.ActiveOverTimeEffects.Count;
        session.ActiveOverTimeEffects.Clear();
        session.CastingSkillId = 0;
        session.CastReadyTicks = 0;
        session.CastCommitTicks = 0;
        session.CastExpireTicks = 0;
        RebuildSpecialStates(session, gameData);
        session.RecalculateStatsWithBuffs(gameData);
        return dropped;
    }

    public static void RebuildSpecialStates(UserSession session, IGameDataService gameData)
    {
        session.Invisibility = InvisibilityType.None;
        session.TransformId = 0;

        short transformId = 0;
        var invisibility = InvisibilityType.None;

        foreach (var magicId in session.ActiveBuffs.Keys)
        {
            var magic = gameData.GetMagic(magicId);
            if (magic == null)
                continue;

            switch (magic.PrimaryType)
            {
                case MagicSkillType.Transform
                    when MagicTypeLookup.TryResolve(gameData.MagicType6Table, magic, magicId, out var type6Data):
                    if (type6Data.TransformId > 0)
                        transformId = type6Data.TransformId;
                    break;

            }

            if (magic.HasType(MagicSkillType.Stealth)
                && MagicTypeLookup.TryResolve(gameData.MagicType9Table, magic, magicId, out var type9Data)
                && (MagicStealthType)type9Data.StateChange
                    is MagicStealthType.DispelOnMove or MagicStealthType.DispelOnAttack)
            {
                invisibility = StealthRules.InvisibilityOf(gameData, magicId, (MagicStealthType)type9Data.StateChange);
            }
        }

        session.Invisibility = invisibility;
        session.TransformId = transformId;
    }

    public static void LoadSavedMagic(UserSession session, byte[]? data, IGameDataService gameData)
    {
        session.ActiveBuffs.Clear();
        session.ActiveOverTimeEffects.Clear();
        session.Invisibility = InvisibilityType.None;
        session.TransformId = 0;

        if (data == null || data.Length < 2)
            return;

        var count = BitConverter.ToInt16(data, 0);
        var offset = 2;

        for (var index = 0; index < count && offset + 16 < data.Length; index++)
        {
            var magicId = BitConverter.ToInt32(data, offset);
            var casterId = BitConverter.ToInt32(data, offset + 4);
            var buffType = (BuffType)data[offset + 8];
            var specialAmount = BitConverter.ToInt32(data, offset + 9);
            var remainingMs = BitConverter.ToInt32(data, offset + 13);
            offset += 17;

            if (remainingMs <= 0)
                continue;

            session.ActiveBuffs[magicId] =
                CreateSavedBuff(gameData, magicId, casterId, buffType, specialAmount, remainingMs);
        }

        RebuildSpecialStates(session, gameData);
    }

    private static short ApplyBuffResourceBonus(short currentValue, int flatBonus, byte percentBonus)
    {
        var updatedValue = flatBonus == 0 && percentBonus > 0
            ? currentValue + currentValue * (percentBonus - 100) / 100
            : currentValue + flatBonus;

        return (short)Math.Clamp(updatedValue, 1, short.MaxValue);
    }

    private static void ResetBuffFlags(UserSession session)
    {
        session.IsBlinded = false;
        session.BlockCurses = false;
        session.ReflectCurses = false;
        session.InstantCast = false;
        session.CanUseSkills = true;
        session.CanUsePotions = true;
        session.CanTeleport = true;
        session.StealthProhibited = false;
        session.WeaponsDisabled = false;
        session.IsUndead = false;
        session.IsKaul = false;
        session.BlockPhysical = false;
        session.BlockMagic = false;
        session.MirrorDamage = false;
        session.MirrorDamageAmount = 0;
        session.SpeedAmount = 100;
        session.ManaAbsorbPct = 0;
        session.MagicDamageReduction = 100;
        session.ExpGainAmount = 100;
        session.LoyaltyGainAmount = 100;
        session.NoahGainAmount = 100;
        session.PlayerAttackAmount = 100;
        session.AttackAmount = 100;
        session.AttackSpeedAmount = 100;
        session.MagicAttackAmount = 0;
        session.ReflectArmorType = 0;
    }

    private static void ApplyBuffTypeFlags(UserSession session, ActiveBuff buff)
    {
        switch (buff.BuffType)
        {
            case BuffType.Speed:
            case BuffType.Freeze:
                session.SpeedAmount = (byte)buff.BonusSpeed;
                break;
            case BuffType.Speed2:
                session.SpeedAmount = (byte)(session.SpeedAmount * 65 / 100);
                if (session.SpeedAmount == 0)
                    session.SpeedAmount = 1;
                break;
            case BuffType.TripleAcHalfSpeed:
                session.SpeedAmount = (byte)Math.Max(1, session.SpeedAmount / 2);
                break;
            case BuffType.Damage:
                if (buff.BonusAttack > 0)
                    session.AttackAmount = (byte)buff.BonusAttack;
                break;
            case BuffType.AttackSpeedArmor:
                if (buff.BonusAttack > 0)
                    session.AttackAmount = (byte)(session.AttackAmount + buff.BonusAttack - 100);
                break;
            case BuffType.AttackSpeed:
                if (buff.BonusAttackSpeed > 0)
                    session.AttackSpeedAmount += (short)(buff.BonusAttackSpeed - 100);
                break;
            case BuffType.DamageDouble:
                if (buff.BonusAttack > 0)
                    session.PlayerAttackAmount = (byte)buff.BonusAttack;
                break;
            case BuffType.DisableTargeting:
            case BuffType.Blind:
            case BuffType.Unsight:
                session.IsBlinded = true;
                break;
            case BuffType.InstantMagic:
                session.InstantCast = true;
                break;
            case BuffType.BlockCurse:
                session.BlockCurses = true;
                break;
            case BuffType.BlockCurseReflect:
                session.ReflectCurses = true;
                break;
            case BuffType.SilenceTarget:
                session.CanUseSkills = false;
                break;
            case BuffType.NoPotions:
                session.CanUsePotions = false;
                break;
            case BuffType.NoRecall:
                session.CanTeleport = false;
                break;
            case BuffType.ProhibitInvis:
                session.StealthProhibited = true;
                break;
            case BuffType.IgnoreWeapon:
                session.WeaponsDisabled = true;
                break;
            case BuffType.Undead:
                session.IsUndead = true;
                break;
            case BuffType.KaulTransformation:
                session.IsKaul = true;
                break;
            case BuffType.BlockPhysicalDamage:
                session.BlockPhysical = true;
                break;
            case BuffType.BlockMagicalDamage:
                session.BlockMagic = true;
                break;
            case BuffType.MirrorDamageParty:
                session.MirrorDamage = true;
                session.MirrorDamageAmount = (byte)buff.SpecialAmount;
                break;
            case BuffType.ManaAbsorb:
                session.ManaAbsorbPct = (byte)buff.SpecialAmount;
                break;
            case BuffType.ResisAndMagicDmg:
                session.MagicDamageReduction = (byte)buff.SpecialAmount;
                break;
            case BuffType.MageArmor:
                session.ReflectArmorType = (byte)buff.SpecialAmount;
                break;
            case BuffType.Experience:
                session.ExpGainAmount = (byte)buff.SpecialAmount;
                break;
            case BuffType.Loyalty:
                session.LoyaltyGainAmount = (byte)buff.SpecialAmount;
                break;
            case BuffType.NoahBonus:
                session.NoahGainAmount = (byte)buff.SpecialAmount;
                break;
        }
    }

    private static ActiveBuff CreateSavedBuff(
        IGameDataService gameData,
        int magicId,
        int casterId,
        BuffType savedBuffType,
        int savedSpecialAmount,
        int remainingMs)
    {
        var buff = new ActiveBuff
        {
            MagicId = magicId,
            CasterId = casterId,
            BuffType = savedBuffType,
            SpecialAmount = savedSpecialAmount,
            Duration = (short)Math.Clamp(remainingMs / 1000, 0, short.MaxValue),
            ExpireTicks = DateTime.UtcNow.Ticks + (long)remainingMs * TimeSpan.TicksPerMillisecond
        };

        var magic = gameData.GetMagic(magicId);
        if (magic == null)
            return buff;

        switch (magic.PrimaryType)
        {
            case MagicSkillType.Buff
                when MagicTypeLookup.TryResolve(gameData.MagicType4Table, magic, magicId, out var type4Data):
                buff.Duration = type4Data.Duration;
                buff.BuffType = type4Data.BuffType != 0 ? (BuffType)type4Data.BuffType : savedBuffType;
                buff.SpecialAmount = savedSpecialAmount != 0
                    ? savedSpecialAmount
                    : (type4Data.SpecialAmount > 0 ? type4Data.SpecialAmount : type4Data.ExpPct);
                buff.BonusAc = type4Data.Ac;
                buff.BonusAcPct = type4Data.AcPct;
                buff.BonusAttack = type4Data.Attack;
                buff.BonusMagicAttack = type4Data.MagicAttack;
                buff.BonusMaxHp = type4Data.MaxHP;
                buff.BonusMaxHpPct = type4Data.MaxHPPct;
                buff.BonusMaxMp = type4Data.MaxMP;
                buff.BonusMaxMpPct = type4Data.MaxMPPct;
                buff.BonusHitRate = type4Data.HitRate;
                buff.BonusAvoidRate = type4Data.AvoidRate;
                buff.BonusStr = type4Data.Str;
                buff.BonusSta = type4Data.Sta;
                buff.BonusDex = type4Data.Dex;
                buff.BonusIntel = type4Data.Intel;
                buff.BonusCha = type4Data.Cha;
                buff.BonusFireR = type4Data.FireR;
                buff.BonusColdR = type4Data.ColdR;
                buff.BonusLightningR = type4Data.LightningR;
                buff.BonusMagicR = type4Data.MagicR;
                buff.BonusPoisonR = type4Data.PoisonR;
                buff.BonusDiseaseR = type4Data.DiseaseR;
                buff.BonusSpeed = type4Data.Speed;
                buff.BonusAttackSpeed = type4Data.AttackSpeed;
                break;
            case MagicSkillType.Transform
                when MagicTypeLookup.TryResolve(gameData.MagicType6Table, magic, magicId, out var type6Data):
                buff.Duration = type6Data.Duration;
                break;
            case MagicSkillType.Area
                when MagicTypeLookup.TryResolve(gameData.MagicType7Table, magic, magicId, out var type7Data):
                buff.Duration = type7Data.Duration;
                break;
            case MagicSkillType.Stealth
                when MagicTypeLookup.TryResolve(gameData.MagicType9Table, magic, magicId, out var type9Data):
                buff.Duration = type9Data.Duration;
                break;
        }

        return buff;
    }
}
