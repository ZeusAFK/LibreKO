using System;
using System.Collections.Generic;
using LibreKO.Common.Enums;
using LibreKO.Game.Scripting;

namespace LibreKO.Game.World;

public readonly record struct DrakiSpawnInfo(int MonsterId, float PosX, float PosZ, short Direction, bool IsMonster);

public sealed class DrakiStageInfo
{
    public int Id { get; set; }
    public byte Stage { get; set; }
    public byte SubStage { get; set; }
    public bool IsNpcBreak { get; set; }
    public List<DrakiSpawnInfo> Spawns { get; set; } = [];
}

public static class DrakiTowerRules
{
    public const byte ZoneIdValue = (byte)ZoneId.DrakiTower; // 95
    public const int MinimumLevel = 1;
    public const int MaxDailyEntrances = 3;
    public const int CertificateOfDrakiItem = 810377000;
    public const int DrakiSupplyBoxItem = 810596000;
    public const int SuperiorDrakiSupplyBoxItem = 810597000;
    public const int SuperiorBoxThresholdSeconds = 1200;
    public const int RoomDurationMinutes = 120;
    public static readonly TimeSpan RoomDuration = TimeSpan.FromMinutes(RoomDurationMinutes);
    public const int WaveSeconds = ScriptCharacterService.DrakiRiftSeconds;
    public const int BreakSeconds = 180;
    public const int DrakiRiftNpcId = 25267;
    public const int FinalExitNpcId = 25266;
    public static readonly (float X, float Z) FinalExitCoordinates = (77f, 214f);

    public static readonly HashSet<int> GateNpcIds =
    [
        25257, 25258, 25259, 25260, 25261, 25263, 25264, 25265
    ];

    public static readonly Dictionary<byte, (float X, float Z)> StageCoordinates = new()
    {
        [1] = (40f, 451f),
        [2] = (78f, 58f),
        [3] = (315f, 439f),
        [4] = (304f, 271f),
        [5] = (71f, 195f),
    };

    public static bool CanEnterFrom(byte zoneId) =>
        zoneId is (byte)ZoneId.KarusCamp1
            or (byte)ZoneId.ElMoradCamp1
            or (byte)ZoneId.KarusCamp2
            or (byte)ZoneId.ElMoradCamp2;

    public static bool IsGateNpc(int npcId) => GateNpcIds.Contains(npcId);

    public static bool IsDrakiNpc(int npcId) =>
        npcId == DrakiRiftNpcId || npcId == FinalExitNpcId || IsGateNpc(npcId);

    public static void EnsureDailyLimit(UserSession session)
    {
        var today = DateTime.UtcNow.Date;
        if (session.DrakiEntranceLimitResetDate.Date != today)
        {
            session.DrakiEntranceLimit = MaxDailyEntrances;
            session.DrakiEntranceLimitResetDate = today;
        }
    }
}
