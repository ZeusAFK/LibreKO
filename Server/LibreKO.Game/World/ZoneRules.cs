using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public readonly record struct ZoneRule(ZoneAbilityType Ability, ZoneFlags Flags);

public static class ZoneRules
{
    private const ZoneFlags Town =
        ZoneFlags.TradeOtherNation | ZoneFlags.TalkOtherNation | ZoneFlags.FriendlyNpcs;

    private const ZoneFlags NationBase = ZoneFlags.AttackOtherNation | ZoneFlags.ClanUpdate;

    private const ZoneFlags Frontier = ZoneFlags.AttackOtherNation;

    private const ZoneFlags War = ZoneFlags.AttackOtherNation | ZoneFlags.WarZone;

    private const ZoneFlags Instance =
        ZoneFlags.TalkOtherNation | ZoneFlags.AttackOtherNation | ZoneFlags.FriendlyNpcs;

    private const ZoneFlags Melee = ZoneFlags.AttackOtherNation | ZoneFlags.AttackSameNation;

    private static readonly ZoneRule Unlisted = new(ZoneAbilityType.Pvp, ZoneFlags.None);

    private static readonly Dictionary<byte, ZoneRule> Table = new()
    {
        [(byte)ZoneId.KarusCamp1] = new(ZoneAbilityType.Pvp, NationBase),
        [(byte)ZoneId.KarusCamp2] = new(ZoneAbilityType.Pvp, NationBase),
        [(byte)ZoneId.ElMoradCamp1] = new(ZoneAbilityType.Pvp, NationBase),
        [(byte)ZoneId.ElMoradCamp2] = new(ZoneAbilityType.Pvp, NationBase),

        [(byte)ZoneId.KarusEslant1] = new(ZoneAbilityType.Pvp, Frontier),
        [(byte)ZoneId.KarusEslant2] = new(ZoneAbilityType.Pvp, Frontier),
        [(byte)ZoneId.ElMoradEslant1] = new(ZoneAbilityType.Pvp, Frontier),
        [(byte)ZoneId.ElMoradEslant2] = new(ZoneAbilityType.Pvp, Frontier),

        [(byte)ZoneId.Moradon] = new(ZoneAbilityType.Neutral, Town | ZoneFlags.ClanUpdate),
        [(byte)ZoneId.Moradon2] = new(ZoneAbilityType.Neutral, Town | ZoneFlags.ClanUpdate),
        [(byte)ZoneId.Moradon3] = new(ZoneAbilityType.Neutral, Town | ZoneFlags.ClanUpdate),

        [(byte)ZoneId.Delos] = new(ZoneAbilityType.SiegeDisabled, Town),
        [(byte)ZoneId.BattleOfBarrack] = new(ZoneAbilityType.SiegeDisabled, Town),
        [(byte)ZoneId.UnderCastle] = new(ZoneAbilityType.Neutral, Town),
        [(byte)ZoneId.OrcArena] = new(ZoneAbilityType.Neutral, Town),
        [(byte)ZoneId.BloodDonArena] = new(ZoneAbilityType.Neutral, Town),
        [(byte)ZoneId.GoblinArena] = new(ZoneAbilityType.Neutral, Town),
        [(byte)ZoneId.ForgottenTemple] = new(ZoneAbilityType.Neutral, Town),

        [(byte)ZoneId.Bifrost] = new(ZoneAbilityType.PvpNeutralNpcs,
            ZoneFlags.AttackOtherNation | ZoneFlags.FriendlyNpcs),
        [(byte)ZoneId.DesperationAbyss] = new(ZoneAbilityType.PvpNeutralNpcs, Instance),
        [(byte)ZoneId.HellAbyss] = new(ZoneAbilityType.PvpNeutralNpcs, Instance),
        [(byte)ZoneId.DragonCave] = new(ZoneAbilityType.PvpNeutralNpcs, Instance),
        [(byte)ZoneId.Prison] = new(ZoneAbilityType.PvpNeutralNpcs,
            ZoneFlags.AttackOtherNation | ZoneFlags.FriendlyNpcs),
        [(byte)ZoneId.IsiloonArena] = new(ZoneAbilityType.PvpNeutralNpcs, Instance),
        [(byte)ZoneId.FelankorArena] = new(ZoneAbilityType.PvpNeutralNpcs, Instance),

        [(byte)ZoneId.Arena] = new(ZoneAbilityType.Neutral, Instance | ZoneFlags.AttackSameNation),
        [(byte)ZoneId.CaitharosArena] = new(ZoneAbilityType.CaitharosArena, Instance),
        [(byte)ZoneId.DrakiTower] = new(ZoneAbilityType.SiegeType2,
            Instance | ZoneFlags.AttackSameNation),

        [(byte)ZoneId.NapiesGorge] = new(ZoneAbilityType.Pvp, War),
        [(byte)ZoneId.AlseidsPrairie] = new(ZoneAbilityType.Pvp, War),
        [(byte)ZoneId.NiedsTriangle] = new(ZoneAbilityType.Pvp, War),
        [(byte)ZoneId.NereidsIsland] = new(ZoneAbilityType.Pvp, War),
        [(byte)ZoneId.Zipang] = new(ZoneAbilityType.Pvp, War),
        [(byte)ZoneId.Oreads] = new(ZoneAbilityType.Pvp, War),
        [(byte)ZoneId.SnowBattle] = new(ZoneAbilityType.Pvp, War),

        [(byte)ZoneId.RonarkLand] = new(ZoneAbilityType.Pvp, Frontier),
        [(byte)ZoneId.Ardream] = new(ZoneAbilityType.Pvp, Frontier),
        [(byte)ZoneId.RonarkLandBase] = new(ZoneAbilityType.Pvp, Frontier),
        [(byte)ZoneId.ChronoLands] = new(ZoneAbilityType.Pvp, Frontier),
        [(byte)ZoneId.NationWar] = new(ZoneAbilityType.Pvp, Frontier),
        [(byte)ZoneId.KrowazDominion] = new(ZoneAbilityType.Pvp,
            ZoneFlags.AttackOtherNation | ZoneFlags.FriendlyNpcs),
        [(byte)ZoneId.BorderDefenseWar] = new(ZoneAbilityType.Pvp,
            ZoneFlags.AttackOtherNation | ZoneFlags.FriendlyNpcs),
        [(byte)ZoneId.JuradMountain] = new(ZoneAbilityType.Pvp,
            ZoneFlags.AttackOtherNation | ZoneFlags.FriendlyNpcs),
        [(byte)ZoneId.ChaosDungeon] = new(ZoneAbilityType.Pvp, Melee),

        [(byte)ZoneId.ClanWar] = new(ZoneAbilityType.SiegeDisabled, Melee),
        [(byte)ZoneId.PartyClan1] = new(ZoneAbilityType.SiegeDisabled, Melee),
        [(byte)ZoneId.PartyClan2] = new(ZoneAbilityType.SiegeDisabled, Melee),
        [(byte)ZoneId.PartyClan3] = new(ZoneAbilityType.SiegeDisabled, Melee),
        [(byte)ZoneId.PartyClan4] = new(ZoneAbilityType.SiegeDisabled, Melee),
    };

    public static ZoneRule For(byte zoneId)
        => Table.TryGetValue(zoneId, out var rule) ? rule : Unlisted;

    public static bool Allows(byte zoneId, ZoneFlags flag) => (For(zoneId).Flags & flag) == flag;
}
