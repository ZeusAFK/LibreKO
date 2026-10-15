using System;
using System.Collections.Generic;
using System.Linq;
using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public static class FullMoonRules
{
    public const byte NormalBoard = 0;
    public const byte FullMoonBoard = 2;
    public const int VoucherItem = 914057000;
    public const byte EntrySub = 58;
    public const byte TowerSub = 59;
    public const byte StageSub = 60;

    public const byte Ready = 0;
    public const byte InstanceFailed = 1;
    public const byte NotAllInMoradon = 2;
    public const byte MemberDead = 4;
    public const byte NoParty = 5;
    public const byte NotFullMoonParty = 6;
    public const byte MemberWithoutVoucher = 7;

    public const int MonsterCoinItem = 914058000;
    public const int RiftJewelItem = 914069000;
    public const int LunarOrderToken = 810977000;

    public const int PriestOfFullMoonNpcId = 31737;
    public const int KillingBladeItem = 700037000;
    public const int DrainRestoreItem = 700040000;
    public const int LightPitItem = 700041000;

    public const int StarterKillingBladeCount = 10;
    public const int StarterDrainRestoreCount = 5;
    public const int StarterLightPitCount = 5;

    public const byte DifficultyEasy = 1;
    public const byte DifficultyNormal = 2;
    public const byte DifficultyHard = 3;

    public const int EasyMinStage = 1;
    public const int EasyMaxStage = 5;
    public const int NormalMinStage = 6;
    public const int NormalMaxStage = 17;
    public const int HardMinStage = 18;
    public const int HardMaxStage = 35;

    public const int InitialSpawnDelaySeconds = 60;
    public const int FinishGraceSeconds = 30;
    public const int RoomDurationMinutes = 120;
    public const float SpawnX = 215f;
    public const float SpawnZ = 207f;
    public const float MonsterSpawnX = 60f;
    public const float MonsterSpawnZ = 40f;

    public const int NexusTowerNpcId = 31740;

    public static readonly (int NpcId, float X, float Z)[] InitialTowers =
    [
        (31737, 163f, 212f),
        (31737, 199f, 200f),
        (31737, 52f, 124f),
        (31737, 211f, 38f),
        (31740, 167f, 212f),
        (31739, 65f, 134f),
        (31738, 196f, 54f),
    ];

    public static readonly (short X, short Z)[] NorthLaneWaypoints =
    [
        (60, 40),
        (32, 68),
        (32, 76),
        (16, 156),
        (16, 168),
        (12, 188),
        (24, 192),
        (124, 192),
        (140, 180),
        (155, 195),
        (165, 211)
    ];

    public static readonly (short X, short Z)[] EastLaneWaypoints =
    [
        (60, 40),
        (84, 28),
        (92, 28),
        (176, 20),
        (200, 76),
        (200, 104),
        (192, 120),
        (140, 180),
        (155, 195),
        (165, 211)
    ];

    public static readonly HashSet<int> BossMonsterIds = [9931, 9951, 9936, 9959, 9942, 9971];

    public const byte AnyState = 0;
    public const byte SoloState = 2;
    public const byte PartyState = 3;
    public const byte AnyZone = 0;
    public const byte AnyLevel = 0;
    public const int LevelBand = 10;
    public const int LastBand = 9;

    public static bool InMoradon(byte zone) => zone is >= (byte)ZoneId.Moradon and <= (byte)ZoneId.Moradon4;

    public static bool HasVoucher(UserSession session) =>
        session.Inventory.Any(slot => slot.ItemId == VoucherItem && slot.Count > 0);

    public static byte DifficultyForPartySize(int memberCount) => memberCount switch
    {
        >= 8 => DifficultyHard,
        >= 4 => DifficultyNormal,
        _ => DifficultyEasy
    };

    public static int StartingStageForDifficulty(byte difficulty) => difficulty switch
    {
        DifficultyHard => HardMinStage,
        DifficultyNormal => NormalMinStage,
        _ => EasyMinStage
    };

    public static int MaxStageForDifficulty(byte difficulty) => difficulty switch
    {
        DifficultyHard => HardMaxStage,
        DifficultyNormal => NormalMaxStage,
        _ => EasyMaxStage
    };

    public static (byte MaxDisplayStage, byte CurrentDisplayStage) DisplayStage(byte difficulty, int stageId) => difficulty switch
    {
        DifficultyHard => (18, (byte)Math.Clamp(stageId - 17, 1, 18)),
        DifficultyNormal => (12, (byte)Math.Clamp(stageId - 5, 1, 12)),
        _ => (5, (byte)Math.Clamp(stageId, 1, 5))
    };

    public static byte EntryRefusal(UserSession? leader, IReadOnlyList<UserSession> members, byte boardType = FullMoonBoard, bool requireFullMoonBoard = false)
    {
        if (leader == null || members.Count == 0)
            return NoParty;
        if (requireFullMoonBoard && boardType != FullMoonBoard)
            return NotFullMoonParty;
        if (members.Any(m => !InMoradon(m.ZoneId)))
            return NotAllInMoradon;
        if (members.Any(m => m.Hp <= 0))
            return MemberDead;
        if (members.Any(m => !HasVoucher(m)))
            return MemberWithoutVoucher;
        return Ready;
    }

    public static bool PassesFilter(byte state, short zone, byte level, bool isParty, short candidateZone, byte candidateLevel)
    {
        if (state == SoloState && isParty) return false;
        if (state == PartyState && !isParty) return false;
        if (zone != AnyZone && zone != candidateZone) return false;
        if (level == AnyLevel) return true;
        int band = Math.Min((candidateLevel - 1) / LevelBand + 1, LastBand);
        return band == level;
    }
}