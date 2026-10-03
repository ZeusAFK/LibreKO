using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public readonly record struct MonsterStoneDungeon(byte ZoneId, short Family)
{
    public short Set => (short)(MonsterStoneRules.FamilySetBase + Family);
}

public static class MonsterStoneRules
{
    public const int UniversalStone = 900144023;
    public const int FirstNestStone = 300144036;
    public const int SecondNestStone = 300145037;
    public const int ThirdNestStone = 300146038;

    public const short FamilySetBase = 100;
    public const int MinimumLevel = 20;
    public const int FinishGraceSeconds = 20;

    private static readonly Dictionary<byte, short[]> FamiliesByZone = new()
    {
        [(byte)ZoneId.MonsterStone1] = [1, 2, 3, 4],
        [(byte)ZoneId.MonsterStone2] = [5, 6, 7, 8, 9],
        [(byte)ZoneId.MonsterStone3] = [10, 11, 12, 13],
    };

    private static readonly Dictionary<int, byte> ZoneByStone = new()
    {
        [FirstNestStone] = (byte)ZoneId.MonsterStone1,
        [SecondNestStone] = (byte)ZoneId.MonsterStone2,
        [ThirdNestStone] = (byte)ZoneId.MonsterStone3,
    };

    private static readonly (int MaxLevel, short[] Families)[] UniversalBands =
    [
        (29, [1]),
        (35, [2]),
        (40, [3]),
        (46, [4]),
        (55, [4, 5]),
        (60, [6, 7, 8]),
        (66, [8, 9]),
        (70, [9, 10]),
        (74, [10, 11, 12]),
        (int.MaxValue, [13]),
    ];

    public static bool IsStone(int itemId) => itemId == UniversalStone || ZoneByStone.ContainsKey(itemId);

    public static bool IsNestZone(byte zoneId) => FamiliesByZone.ContainsKey(zoneId);

    public static bool CanEnterFrom(byte zoneId) =>
        zoneId != (byte)ZoneId.Prison
        && zoneId != (byte)ZoneId.JuradMountain
        && !IsNestZone(zoneId)
        && !ZoneRules.IsTempleEvent(zoneId);

    public static MonsterStoneDungeon? Pick(int itemId, int level, Random random)
    {
        var families = itemId == UniversalStone
            ? UniversalFamilies(level)
            : ZoneByStone.TryGetValue(itemId, out var zone) ? FamiliesByZone[zone] : null;
        if (families is not { Length: > 0 })
            return null;

        var family = families[random.Next(families.Length)];
        return new MonsterStoneDungeon(ZoneOf(family), family);
    }

    private static short[]? UniversalFamilies(int level) =>
        level < MinimumLevel ? null : UniversalBands.First(band => level <= band.MaxLevel).Families;

    private static byte ZoneOf(short family) => FamiliesByZone.First(entry => entry.Value.Contains(family)).Key;

    public static int FamilyLevel(byte zoneId, short set)
    {
        var family = (short)(set - FamilySetBase);
        if (!FamiliesByZone.TryGetValue(zoneId, out var families) || !families.Contains(family))
            return 0;

        int low = MinimumLevel, first = 0, last = 0;
        foreach (var (maxLevel, bandFamilies) in UniversalBands)
        {
            var high = Math.Min(maxLevel, ProgressionTable.MaxLevel);
            if (bandFamilies.Contains(family))
            {
                if (first == 0)
                    first = low;
                last = high;
            }
            low = high + 1;
        }

        return first == 0 ? 0 : (first + last) / 2;
    }
}

public interface IMonsterStoneService
{
    Task UseAsync(UserSession session, int itemId);
    Task OnNpcKilledAsync(NpcInstance npc);
}

public sealed class MonsterStoneService(
    SessionManager sessionManager,
    IGameDataService gameData,
    IMagicItemUsageService itemUsage,
    IInstanceEntryService instanceEntry,
    InstanceRoomRegistry rooms,
    ILogger<MonsterStoneService> logger) : IMonsterStoneService
{
    public static readonly string LevelTooLowNotice =
        $"Only characters of level {MonsterStoneRules.MinimumLevel} and above can enter a monster nest.";

    private static readonly ushort RoomSeconds = (ushort)TimeSpan.FromMinutes(GameConstants.InstanceRoomMinutes).TotalSeconds;

    public async Task UseAsync(UserSession session, int itemId)
    {
        if (!MonsterStoneRules.IsStone(itemId))
        {
            logger.LogInformation("{Name} asked for a nest with item {Item}, which opens none", session.Name, itemId);
            await ReplyAsync(session, MonsterStoneResult.Failed);
            return;
        }

        if (session.Hp < session.MaxHp / 2)
        {
            await ReplyAsync(session, MonsterStoneResult.NotEnoughHealth);
            return;
        }

        if (!MonsterStoneRules.CanEnterFrom(session.ZoneId))
        {
            await ReplyAsync(session, MonsterStoneResult.CannotEnterHere);
            return;
        }

        if (session.Room != 0 || session.IsWarping || !itemUsage.CanUseItem(session, itemId))
        {
            await ReplyAsync(session, MonsterStoneResult.Failed);
            return;
        }

        if (MonsterStoneRules.Pick(itemId, session.Level, Random.Shared) is not { } nest)
        {
            await session.Client.SendPacket(ChatPacketWriter.SystemNotice((byte)session.Nation, LevelTooLowNotice));
            await ReplyAsync(session, MonsterStoneResult.Failed);
            return;
        }

        if (!gameData.NpcPositions.Any(pos => pos.ZoneId == nest.ZoneId && pos.Room == nest.Set))
        {
            logger.LogWarning("Monster nest family {Family} of zone {Zone} has no spawn rows", nest.Family, nest.ZoneId);
            await ReplyAsync(session, MonsterStoneResult.Failed);
            return;
        }

        if (!await itemUsage.TryConsumeItemAsync(session, itemId))
        {
            await ReplyAsync(session, MonsterStoneResult.Failed);
            return;
        }

        await session.Client.SendPacket(EventPacketWriter.MonsterStoneEntered(itemId));
        await instanceEntry.EnterAloneAsync(session, nest.ZoneId, nest.Set, 0f, 0f, endsOnBossKill: true);
        await session.Client.SendPacket(BifrostPacketWriter.NestTimer(RoomSeconds));
        logger.LogInformation("{Name} used monster stone {Item}: zone {Zone} family {Family}",
            session.Name, itemId, nest.ZoneId, nest.Family);
    }

    public async Task OnNpcKilledAsync(NpcInstance npc)
    {
        if (npc.Room == 0 || !MonsterStoneRules.IsNestZone(npc.ZoneId))
            return;

        if (rooms.Get(npc.Room) is not { EndsOnBossKill: true } room
            || gameData.GetNpc(npc.NpcId, isMonster: true) is not { IsBoss: true })
            return;

        if (!room.Finish(DateTime.UtcNow.AddSeconds(MonsterStoneRules.FinishGraceSeconds)))
            return;

        foreach (var characterId in room.Members.Keys)
        {
            if (sessionManager.GetByCharacterId(characterId) is { } member)
                await member.Client.SendPacket(EventPacketWriter.NestCompleted(MonsterStoneRules.FinishGraceSeconds));
        }
    }

    private static Task ReplyAsync(UserSession session, MonsterStoneResult result) =>
        session.Client.SendPacket(EventPacketWriter.MonsterStone(result));
}
