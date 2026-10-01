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
            if (buff.IsExpired || !GrantsStats(buff.BuffType))
                continue;
            str += buff.BonusStr;
            sta += buff.BonusSta;
            dex += buff.BonusDex;
            intel += buff.BonusIntel;
            cha += buff.BonusCha;
        }
        return new StatBonus(str, sta, dex, intel, cha);
    }

    public static short WeaponDamageBonus(UserSession session)
    {
        short bonus = 0;
        foreach (var buff in session.ActiveBuffs.Values)
            if (!buff.IsExpired && buff.BuffType == BuffType.WeaponDamage)
                bonus += buff.BonusAttack;
        return bonus;
    }

    private static bool GrantsStats(BuffType buffType) =>
        buffType is BuffType.Stats or BuffType.BattleCry or BuffType.GmBuff;

    public static void ApplyBuffBonuses(UserSession session)
    {
        ResetBuffFlags(session);

        int acPct = NeutralPercent;
        short flatAcBonus = 0;
        short magicAttackBonus = 0;
        int resistPct = NeutralPercent;

        foreach (var buff in session.ActiveBuffs.Values)
        {
            if (buff.IsExpired)
                continue;

            switch (buff.BuffType)
            {
                case BuffType.Ac:
                case BuffType.WeaponAc:
                    if (buff.BonusAc == 0 && buff.BonusAcPct > 0)
                        acPct += buff.BonusAcPct - NeutralPercent;
                    else
                        flatAcBonus += buff.BonusAc;
                    break;
                case BuffType.AttackSpeedArmor:
                case BuffType.Armored:
                    flatAcBonus += buff.BonusAc;
                    break;
                case BuffType.TripleAcHalfSpeed:
                    acPct += TripleAcPercent;
                    break;
                case BuffType.KaulTransformation:
                    flatAcBonus += KaulAcBonus;
                    break;
                case BuffType.AttackRangeArmor:
                    flatAcBonus += AttackRangeArmorAcBonus;
                    break;
                case BuffType.ReduceTarget:
                case BuffType.Undead:
                case BuffType.DivideArmor:
                    if (buff.BonusAcPct > 0)
                        acPct += buff.BonusAcPct - NeutralPercent;
                    break;
                case BuffType.VariousEffects:
                    if (buff.BonusAc == 0 && buff.BonusAcPct > NeutralPercent)
                        acPct += buff.BonusAcPct - NeutralPercent;
                    else if (buff.BonusAc > 0 && buff.BonusAcPct == NeutralPercent)
                        flatAcBonus += buff.BonusAc;
                    session.Stats.MaxHp = ApplyBuffResourceBonus(session.Stats.MaxHp, buff.BonusMaxHp, buff.BonusMaxHpPct);
                    break;
                case BuffType.HpMp:
                    session.Stats.MaxHp = ApplyBuffResourceBonus(session.Stats.MaxHp, buff.BonusMaxHp, buff.BonusMaxHpPct);
                    session.Stats.MaxMp = ApplyBuffResourceBonus(session.Stats.MaxMp, buff.BonusMaxMp, buff.BonusMaxMpPct);
                    break;
                case BuffType.Resistances:
                    session.Stats.FireR += buff.BonusFireR;
                    session.Stats.ColdR += buff.BonusColdR;
                    session.Stats.LightningR += buff.BonusLightningR;
                    session.Stats.MagicR += buff.BonusMagicR;
                    session.Stats.PoisonR += buff.BonusPoisonR;
                    session.Stats.DiseaseR += buff.BonusDiseaseR;
                    break;
                case BuffType.DecreaseResist:
                    resistPct = NeutralPercent - buff.BonusFireR;
                    break;
                case BuffType.MagicPower:
                case BuffType.MagicSpell:
                    magicAttackBonus += (short)(buff.BonusMagicAttack - NeutralPercent);
                    break;
                case BuffType.AntiDagger:
                    session.Stats.DaggerR += WeaponDefenceScrollBonus;
                    break;
                case BuffType.AntiJamadar:
                    session.Stats.JamadarR += WeaponDefenceScrollBonus;
                    break;
                case BuffType.AntiSword:
                    session.Stats.SwordR += WeaponDefenceScrollBonus;
                    break;
                case BuffType.AntiMace:
                    session.Stats.MaceR += WeaponDefenceScrollBonus;
                    break;
                case BuffType.AntiAxe:
                    session.Stats.AxeR += WeaponDefenceScrollBonus;
                    break;
                case BuffType.AntiSpear:
                    session.Stats.SpearR += WeaponDefenceScrollBonus;
                    break;
                case BuffType.AntiBow:
                    session.Stats.BowR += WeaponDefenceScrollBonus;
                    break;
            }

            ApplyBuffTypeFlags(session, buff);
        }

        if (acPct <= 0)
            session.Stats.TotalAc = 0;
        else if (acPct != NeutralPercent)
            session.Stats.TotalAc = (short)(session.Stats.TotalAc * acPct / NeutralPercent);

        session.Stats.TotalAc += flatAcBonus;

        if (resistPct != NeutralPercent)
            ScaleResistances(session.Stats, Math.Max(0, resistPct));

        session.MagicAttackAmount = magicAttackBonus;
    }

    private const int NeutralPercent = 100;
    private const int TripleAcPercent = 300;
    private const short KaulAcBonus = 500;
    private const short AttackRangeArmorAcBonus = 100;
    private const short WeaponDefenceScrollBonus = 5;

    private static void ScaleResistances(DerivedStats stats, int percent)
    {
        stats.FireR = (short)(stats.FireR * percent / NeutralPercent);
        stats.ColdR = (short)(stats.ColdR * percent / NeutralPercent);
        stats.LightningR = (short)(stats.LightningR * percent / NeutralPercent);
        stats.MagicR = (short)(stats.MagicR * percent / NeutralPercent);
        stats.PoisonR = (short)(stats.PoisonR * percent / NeutralPercent);
        stats.DiseaseR = (short)(stats.DiseaseR * percent / NeutralPercent);
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
                if (!session.ActiveBuffs.Values.Any(b => b.BuffType == BuffType.FragmentOfManes && !b.IsExpired))
                    session.SpeedAmount = (byte)buff.BonusSpeed;
                break;
            case BuffType.Freeze:
                session.SpeedAmount = (byte)buff.BonusSpeed;
                session.CanUseSkills = false;
                session.BlockMagic = true;
                session.BlockPhysical = true;
                break;
            case BuffType.FragmentOfManes:
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
                    session.AttackAmount = (byte)Math.Max(0, session.AttackAmount + buff.BonusAttack - NeutralPercent);
                break;
            case BuffType.VariousEffects:
                if (buff.BonusAttack > NeutralPercent)
                    session.AttackAmount = (byte)(session.AttackAmount + buff.BonusAttack - NeutralPercent);
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
