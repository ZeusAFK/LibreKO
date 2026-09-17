namespace LibreKO.Quests.Catalog;

public interface IQuestCatalog
{
    bool KnowsItems { get; }
    bool KnowsExchanges { get; }
    bool KnowsQuests { get; }
    bool KnowsNpcs { get; }
    bool KnowsZones { get; }
    bool KnowsText { get; }

    string? ItemName(long itemId);
    bool ItemStacks(long itemId);
    string? ExchangeSummary(long exchangeId);
    string? QuestSummary(long questId);
    string? QuestTitle(long questId);
    IReadOnlyCollection<long> NpcsForQuest(long questId);
    string? EventTriggerSummary(long npcId, long eventId);
    string? NpcName(long npcId);
    string? ZoneName(long zoneId);
    string? MapName(long mapId);
    string? TalkText(long textId);
    string? MenuText(long textId);
}

public sealed class NullQuestCatalog : IQuestCatalog
{
    public static readonly NullQuestCatalog Instance = new();

    public bool KnowsItems => false;
    public bool KnowsExchanges => false;
    public bool KnowsQuests => false;
    public bool KnowsNpcs => false;
    public bool KnowsZones => false;
    public bool KnowsText => false;

    public string? ItemName(long itemId) => null;
    public bool ItemStacks(long itemId) => false;
    public string? ExchangeSummary(long exchangeId) => null;
    public string? QuestSummary(long questId) => null;
    public string? QuestTitle(long questId) => null;
    public IReadOnlyCollection<long> NpcsForQuest(long questId) => [];
    public string? EventTriggerSummary(long npcId, long eventId) => null;
    public string? NpcName(long npcId) => null;
    public string? ZoneName(long zoneId) => null;
    public string? MapName(long mapId) => null;
    public string? TalkText(long textId) => null;
    public string? MenuText(long textId) => null;
}
