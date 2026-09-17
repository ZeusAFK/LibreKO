using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public class BattleZoneManager
{
    // Battle status
    public const byte NO_BATTLE = 0;
    public const byte NATION_BATTLE = 1;
    public const byte SNOW_BATTLE = 2;

    // Battle zone IDs
    public const byte ZONE_KARUS = (byte)ZoneId.KarusCamp1;
    public const byte ZONE_ELMORAD = (byte)ZoneId.ElMoradCamp1;
    public const byte ZONE_KARUS_ESLANT = (byte)ZoneId.KarusEslant1;
    public const byte ZONE_ELMORAD_ESLANT = (byte)ZoneId.ElMoradEslant1;
    public const byte ZONE_MORADON = (byte)ZoneId.Moradon;
    public const byte ZONE_ARENA = (byte)ZoneId.Arena;
    public const byte ZONE_BATTLE_BASE = (byte)ZoneId.BattleBase;
    public const byte ZONE_BATTLE1 = (byte)ZoneId.NapiesGorge;
    public const byte ZONE_BATTLE2 = (byte)ZoneId.AlseidsPrairie;
    public const byte ZONE_BATTLE3 = (byte)ZoneId.NiedsTriangle;
    public const byte ZONE_BATTLE4 = (byte)ZoneId.NereidsIsland;
    public const byte ZONE_BATTLE5 = (byte)ZoneId.Zipang;
    public const byte ZONE_BATTLE6 = (byte)ZoneId.Oreads;
    public const byte ZONE_SNOW_BATTLE = (byte)ZoneId.SnowBattle;
    public const byte ZONE_RONARK_LAND = (byte)ZoneId.RonarkLand;
    public const byte ZONE_ARDREAM = (byte)ZoneId.Ardream;
    public const byte ZONE_RONARK_LAND_BASE = (byte)ZoneId.RonarkLandBase;
    public const byte ZONE_KROWAZ_DOMINION = (byte)ZoneId.KrowazDominion;
    public const byte ZONE_JURAID_MOUNTAIN = (byte)ZoneId.JuradMountain;
    public const byte ZONE_DELOS = (byte)ZoneId.Delos;
    public const byte ZONE_BIFROST = (byte)ZoneId.Bifrost;
    public const byte ZONE_CAITHAROS_ARENA = (byte)ZoneId.CaitharosArena;
    public const byte ZONE_BORDER_DEFENSE_WAR = (byte)ZoneId.BorderDefenseWar;
    public const byte ZONE_CHAOS_DUNGEON = (byte)ZoneId.ChaosDungeon;
    public const byte ZONE_CHRONO_LANDS = (byte)ZoneId.ChronoLands;
    public const byte ZONE_NATION_WAR = (byte)ZoneId.NationWar;
    public const byte ZONE_DRAKI_TOWER = (byte)ZoneId.DrakiTower;
    public const byte ZONE_DESPERATION_ABYSS = (byte)ZoneId.DesperationAbyss;
    public const byte ZONE_HELL_ABYSS = (byte)ZoneId.HellAbyss;
    public const byte ZONE_DRAGON_CAVE = (byte)ZoneId.DragonCave;

    // Announcement types
    public const byte BATTLEZONE_OPEN = 0x00;
    public const byte BATTLEZONE_CLOSE = 0x01;
    public const byte DECLARE_WINNER = 0x02;
    public const byte DECLARE_LOSER = 0x03;
    public const byte DECLARE_BAN = 0x04;
    public const byte KARUS_CAPTAIN_NOTIFY = 0x05;
    public const byte ELMORAD_CAPTAIN_NOTIFY = 0x06;
    public const byte SNOW_BATTLEZONE_OPEN = 0x09;
    public const byte DECLARE_BATTLE_ZONE_STATUS = 0x11;
    public const byte DECLARE_BATTLE_MONUMENT_STATUS = 0x12;
    public const byte DECLARE_NATION_MONUMENT_STATUS = 0x13;

    // Win condition types
    public const byte BATTLE_WINNER_NPC = 1;
    public const byte BATTLE_WINNER_MONUMENT = 2;
    public const byte BATTLE_WINNER_KILL = 3;

    // NPC kill threshold for victory (zones 1-3)
    public const int NPC_KILL_VICTORY_COUNT = 3;

    // Max users per nation in battle zone
    public const int MAX_BATTLE_ZONE_USERS = 150;

    // Current war state
    public byte BattleOpen { get; private set; } = NO_BATTLE;
    public byte BattleZone { get; private set; }
    public byte Victory { get; set; }
    public bool IsBattleActive => BattleOpen != NO_BATTLE;

    // Timestamps
    public DateTime? BattleOpenedTime { get; private set; }
    public TimeSpan BattleDuration { get; set; } = TimeSpan.FromHours(2);
    public TimeSpan BattleRemainingTime { get; private set; }
    public TimeSpan BanishDelay { get; set; } = TimeSpan.FromSeconds(60);

    // Kill/death counters
    public int KilledKarusNpc { get; set; }
    public int KilledElmoNpc { get; set; }
    public short KarusDead { get; set; }
    public short ElmoradDead { get; set; }

    // Monument state
    public ushort KarusMonumentPoint { get; set; }
    public ushort ElmoMonumentPoint { get; set; }
    public byte KarusMonuments { get; set; }
    public byte ElmoMonuments { get; set; }

    // Flags
    public bool KarusOpenFlag { get; set; }
    public bool ElmoradOpenFlag { get; set; }
    public bool BanishFlag { get; set; }
    public bool BattleSaved { get; set; }

    // Player counts in zone
    public short KarusCount { get; set; }
    public short ElmoradCount { get; set; }

    // PVP monument ownership per zone: zoneId -> nation (1=Karus, 2=Elmo)
    private readonly Dictionary<byte, byte> _pvpMonumentNation = [];

    public bool OpenBattleZone(byte type, byte zone)
    {
        if (BattleOpen != NO_BATTLE)
            return false;

        BattleOpen = type == SNOW_BATTLEZONE_OPEN ? SNOW_BATTLE : NATION_BATTLE;
        BattleZone = zone;
        BattleOpenedTime = DateTime.UtcNow;
        BattleRemainingTime = BattleDuration;
        Victory = 0;

        // Reset counters
        KilledKarusNpc = 0;
        KilledElmoNpc = 0;
        KarusDead = 0;
        ElmoradDead = 0;
        KarusMonumentPoint = 0;
        ElmoMonumentPoint = 0;
        KarusMonuments = 0;
        ElmoMonuments = 0;
        KarusOpenFlag = false;
        ElmoradOpenFlag = false;
        BanishFlag = false;
        BattleSaved = false;
        KarusCount = 0;
        ElmoradCount = 0;

        return true;
    }

    public void CloseBattleZone()
    {
        BanishFlag = true;
        Reset();
    }

    public void Reset()
    {
        BattleOpen = NO_BATTLE;
        BattleZone = 0;
        Victory = 0;
        BattleOpenedTime = null;

        KilledKarusNpc = 0;
        KilledElmoNpc = 0;
        KarusDead = 0;
        ElmoradDead = 0;
        KarusMonumentPoint = 0;
        ElmoMonumentPoint = 0;
        KarusMonuments = 0;
        ElmoMonuments = 0;
        KarusOpenFlag = false;
        ElmoradOpenFlag = false;
        BattleSaved = false;
        KarusCount = 0;
        ElmoradCount = 0;
    }

    public static bool IsBattleZone(byte zoneId)
        => zoneId is >= ZONE_BATTLE1 and <= ZONE_BATTLE6 or ZONE_SNOW_BATTLE;

    public static bool IsPvpZone(byte zoneId)
        => zoneId is ZONE_RONARK_LAND or ZONE_ARDREAM or ZONE_RONARK_LAND_BASE;

    public static bool IsPkZone(byte zoneId)
        => zoneId is ZONE_ARDREAM or ZONE_RONARK_LAND or ZONE_RONARK_LAND_BASE or ZONE_CHRONO_LANDS;

    public static bool IsFreeForAllZone(byte zoneId)
        => zoneId is ZONE_ARENA or ZONE_CHAOS_DUNGEON;

    public static bool AllowsNationCombat(byte zoneId)
        => IsPvpZone(zoneId)
        || IsBattleZone(zoneId)
        || zoneId is ZONE_CHRONO_LANDS or ZONE_NATION_WAR or ZONE_JURAID_MOUNTAIN
            or ZONE_BORDER_DEFENSE_WAR or ZONE_BIFROST or ZONE_DRAKI_TOWER or ZONE_KROWAZ_DOMINION
            or ZONE_DESPERATION_ABYSS or ZONE_HELL_ABYSS or ZONE_DRAGON_CAVE;

    public void RegisterDeath(byte victimNation)
    {
        if (victimNation == (byte)AccountNation.Karus)
            KarusDead++;
        else
            ElmoradDead++;
    }

    public byte RegisterNpcKill(byte killerNation)
    {
        if (killerNation == (byte)AccountNation.Karus)
            KilledElmoNpc++;
        else
            KilledKarusNpc++;

        // Zones 1-3 use NPC kill victory (3 kills)
        if (BattleZone is ZONE_BATTLE1 or ZONE_BATTLE2 or ZONE_BATTLE3)
        {
            if (KilledKarusNpc >= NPC_KILL_VICTORY_COUNT)
                return 2; // Elmorad wins (killed 3 Karus NPCs)
            if (KilledElmoNpc >= NPC_KILL_VICTORY_COUNT)
                return 1; // Karus wins (killed 3 Elmo NPCs)
        }

        return 0;
    }

    public void CaptureMonument(byte nation)
    {
        if (nation == 1) // Karus
        {
            KarusMonumentPoint += 2;
            KarusMonuments++;
            if (KarusMonuments >= 7)
                KarusMonumentPoint += 10;
            if (ElmoMonuments > 0)
                ElmoMonuments--;
        }
        else // Elmorad
        {
            ElmoMonumentPoint += 2;
            ElmoMonuments++;
            if (ElmoMonuments >= 7)
                ElmoMonumentPoint += 10;
            if (KarusMonuments > 0)
                KarusMonuments--;
        }
    }

    public byte DetermineWinner()
    {
        // Zones 4, 6 use monument points
        if (BattleZone is ZONE_BATTLE4 or ZONE_BATTLE6)
        {
            if (KarusMonumentPoint > ElmoMonumentPoint) return 1;
            if (ElmoMonumentPoint > KarusMonumentPoint) return 2;
            // Tie-break by fewer deaths
            if (KarusDead < ElmoradDead) return 1;
            if (ElmoradDead < KarusDead) return 2;
            return 0;
        }

        // Zones 1-3 use NPC kills (already handled in RegisterNpcKill)
        if (BattleZone is ZONE_BATTLE1 or ZONE_BATTLE2 or ZONE_BATTLE3)
        {
            if (KilledElmoNpc > KilledKarusNpc) return 1;
            if (KilledKarusNpc > KilledElmoNpc) return 2;
            return 0;
        }

        // Default: fewer deaths wins
        if (KarusDead < ElmoradDead) return 1;
        if (ElmoradDead < KarusDead) return 2;
        return 0;
    }

    public TimeSpan GetElapsedTime()
        => BattleOpenedTime.HasValue ? DateTime.UtcNow - BattleOpenedTime.Value : TimeSpan.Zero;

    public byte GetPvpMonumentNation(byte zoneId)
        => _pvpMonumentNation.TryGetValue(zoneId, out var nation) ? nation : (byte)0;

    public void SetPvpMonumentNation(byte zoneId, byte nation)
        => _pvpMonumentNation[zoneId] = nation;
}
