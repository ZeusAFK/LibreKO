using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public interface INpcSummonService
{
    Task<IReadOnlyList<NpcInstance>> SummonAsync(int npcId, byte zoneId, ushort room, int x, int z, int count, float fallbackY);
}

public sealed class NpcSummonService(
    SessionManager sessionManager,
    IGameDataService gameData,
    IMonsterAggressionPolicy aggression,
    INpcLifecycleService lifecycle) : INpcSummonService
{
    public const int SpreadRange = 3;

    public async Task<IReadOnlyList<NpcInstance>> SummonAsync(int npcId, byte zoneId, ushort room, int x, int z, int count, float fallbackY)
    {
        var npcData = gameData.GetNpc(npcId);
        if (npcData == null)
            return [];

        var pos = new NpcPosData
        {
            NpcId = npcId,
            ZoneId = zoneId,
            LeftX = x,
            TopZ = z,
            SpawnRange = SpreadRange,
            ActType = npcData.ActType,
            NumNPC = (byte)Math.Clamp(count, 1, byte.MaxValue),
        };
        var spawned = new List<NpcInstance>(pos.NumNPC);
        for (var i = 0; i < pos.NumNPC; i++)
        {
            var npc = NpcInstance.FromData(npcData, pos, 0);
            npc.Room = room;
            npc.RespawnType = NpcRespawnType.Never;
            aggression.Apply(npc);
            npc.Y = sessionManager.Maps?.GetHeight(zoneId, npc.X, npc.Z) ?? fallbackY;
            npc.SpawnY = npc.Y;
            await lifecycle.SpawnAsync(npc);
            spawned.Add(npc);
        }

        return spawned;
    }
}
