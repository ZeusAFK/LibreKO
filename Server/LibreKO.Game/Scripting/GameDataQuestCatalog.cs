using LibreKO.Common.Domain.Services;
using LibreKO.Quests.Catalog;

namespace LibreKO.Game.Scripting;

public sealed class GameDataQuestCatalog(IGameDataService gameData) : IQuestCatalog
{
    public bool KnowsItems => gameData.ItemTable.Count > 0;
    public bool KnowsExchanges => gameData.ItemExchangeTable.Count > 0;
    public bool KnowsQuests => false;
    public bool KnowsNpcs => gameData.NpcTable.Count > 0;
    public bool KnowsZones => gameData.ZoneInfoTable.Count > 0;
    public bool KnowsText => false;

    public string? ItemName(long itemId) =>
        itemId is < int.MinValue or > int.MaxValue
            ? null
            : gameData.ItemTable.TryGetValue((int)itemId, out var item) ? item.Name : null;

    public bool ItemStacks(long itemId) =>
        itemId is >= int.MinValue and <= int.MaxValue
            && gameData.ItemTable.TryGetValue((int)itemId, out var item) && item.Countable != 0;

    public string? ExchangeSummary(long exchangeId) =>
        exchangeId is < int.MinValue or > int.MaxValue
            ? null
            : gameData.ItemExchangeTable.ContainsKey((int)exchangeId) ? $"exchange {exchangeId}" : null;

    public string? NpcName(long npcId)
    {
        if (npcId is < int.MinValue or > int.MaxValue)
            return null;
        if (gameData.NpcTable.TryGetValue((int)npcId, out var npc))
            return npc.Name;
        return gameData.MonsterTable.TryGetValue((int)npcId, out var monster) ? monster.Name : null;
    }

    public string? ZoneName(long zoneId) =>
        zoneId is < short.MinValue or > short.MaxValue
            ? null
            : gameData.ZoneInfoTable.TryGetValue((short)zoneId, out var zone) ? zone.MapName : null;

    public string? QuestSummary(long questId) => null;

    public string? MapName(long mapId) => null;

    public string? QuestTitle(long questId) => null;

    public IReadOnlyCollection<long> NpcsForQuest(long questId) => [];

    public string? EventTriggerSummary(long npcId, long eventId) => null;

    public string? TalkText(long textId) => null;

    public string? MenuText(long textId) => null;
}
