using LibreKO.Game.Configuration;
using Microsoft.Extensions.Options;

namespace LibreKO.Game.World;

public interface IMonsterAggressionPolicy
{
    void Apply(NpcInstance npc);
}

public class MonsterAggressionPolicy(IOptions<GameServerSettings> settings) : IMonsterAggressionPolicy
{
    private readonly bool _bosses = settings.Value.Monsters.AggressiveBosses;
    private readonly HashSet<int> _zones = [.. settings.Value.Monsters.AggressiveZones];
    private readonly HashSet<int> _npcIds = [.. settings.Value.Monsters.AggressiveNpcIds];

    public void Apply(NpcInstance npc)
    {
        if (npc.IsAggressive || !npc.IsMonster || npc.IsScarecrow)
            return;

        if (_npcIds.Contains(npc.NpcId) || (_bosses && npc.IsBoss) || _zones.Contains(npc.ZoneId))
            npc.IsAggressive = true;
    }
}
