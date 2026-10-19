using System.Text.Json;

namespace LibreKO.Quests.Catalog;

public sealed class JsonQuestCatalog : IQuestCatalog
{
    private readonly Dictionary<long, string> itemNames = [];
    private readonly HashSet<long> stacking = [];
    private readonly Dictionary<long, string> exchanges = [];
    private readonly Dictionary<long, string> quests = [];
    private readonly Dictionary<long, string> questTitles = [];
    private readonly Dictionary<long, HashSet<long>> questNpcs = [];
    private readonly Dictionary<(long Npc, long Event), SortedSet<(long Quest, long Status)>> triggers = [];
    private readonly Dictionary<long, string> npcNames = [];
    private readonly Dictionary<long, string> zones = [];
    private readonly Dictionary<long, string> maps = [];
    private readonly Dictionary<long, string> talk = [];
    private readonly Dictionary<long, string> menu = [];

    public bool KnowsItems => itemNames.Count > 0;
    public bool KnowsExchanges => exchanges.Count > 0;
    public bool KnowsQuests => quests.Count > 0;
    public bool KnowsNpcs => npcNames.Count > 0;
    public bool KnowsZones => zones.Count > 0;
    public bool KnowsText => talk.Count > 0 || menu.Count > 0;

    public string? ItemName(long itemId) => Lookup(itemNames, itemId);
    public bool ItemStacks(long itemId) => stacking.Contains(itemId);
    public string? ExchangeSummary(long exchangeId) => Lookup(exchanges, exchangeId);
    public string? QuestSummary(long questId) => Lookup(quests, questId);
    public string? QuestTitle(long questId) => Lookup(questTitles, questId);

    public IReadOnlyCollection<long> NpcsForQuest(long questId) =>
        questNpcs.TryGetValue(questId, out var npcs) ? npcs : [];

    public string? EventTriggerSummary(long npcId, long eventId)
    {
        if (!triggers.TryGetValue((npcId, eventId), out var rows) || rows.Count == 0)
            return null;

        var parts = new List<string>();
        foreach (var quest in rows.Select(r => r.Quest).Distinct())
        {
            var states = rows
                .Where(r => r.Quest == quest)
                .Select(r => StatusName(r.Status))
                .Distinct()
                .ToArray();
            var title = quest == 0 ? "no quest" : QuestTitle(quest) ?? $"quest {quest}";
            parts.Add(states.Length > 0 ? $"{title} \u00b7 {string.Join("/", states)}" : title);
        }
        return string.Join("; ", parts);
    }

    private static string StatusName(long status) => status switch
    {
        0 => "not started",
        1 => "started",
        2 => "completed",
        255 => "on offer",
        _ => $"state {status}",
    };
    public string? NpcName(long npcId) => Lookup(npcNames, npcId);
    public string? ZoneName(long zoneId) => Lookup(zones, zoneId);
    public string? MapName(long mapId) => Lookup(maps, mapId);
    public string? TalkText(long textId) => Lookup(talk, textId);
    public string? MenuText(long textId) => Lookup(menu, textId);

    private static string? Lookup(Dictionary<long, string> map, long key) =>
        map.TryGetValue(key, out var value) ? value : null;

    public static JsonQuestCatalog Load(string seedDirectory, string? questTextPath = null, string? retailQuestDataDirectory = null)
    {
        var catalog = new JsonQuestCatalog();
        var items = Path.Combine(seedDirectory, "Items.json");
        var itemFiles = File.Exists(items) ? [items]
            : Directory.Exists(seedDirectory)
                ? Directory.GetFiles(seedDirectory, "Items.slot*.json").Order(StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>();
        foreach (var file in itemFiles)
            catalog.LoadItems(file);
        catalog.LoadExchanges(Path.Combine(seedDirectory, "ItemExchanges.json"));
        catalog.LoadNpcs(Path.Combine(seedDirectory, "Npcs.json"));
        if (retailQuestDataDirectory is { Length: > 0 })
            catalog.LoadQuestHelpers(Path.Combine(retailQuestDataDirectory, "QuestHelpers.json"));
        catalog.LoadZones(Path.Combine(seedDirectory, "ZoneInfos.json"));
        catalog.LoadQuestTitles(Path.Combine(seedDirectory, "QuestTitles.json"));
        catalog.LoadQuestLocations(Path.Combine(seedDirectory, "QuestLocations.json"));
        if (questTextPath is { Length: > 0 })
            catalog.LoadQuestText(questTextPath);
        return catalog;
    }

    private void LoadItems(string path)
    {
        foreach (var row in ReadArray(path))
        {
            if (!TryGetLong(row, "Num", out var id))
                continue;
            itemNames[id] = row.TryGetProperty("Name", out var name) ? name.GetString() ?? "?" : "?";
            if (row.TryGetProperty("Countable", out var countable) && countable.TryGetInt32(out var stacks)
                && stacks != 0)
                stacking.Add(id);
        }
    }

    private void LoadExchanges(string path)
    {
        foreach (var row in ReadArray(path))
        {
            if (!TryGetLong(row, "Index", out var id))
                continue;
            var parts = new List<string>();
            for (var i = 1; i <= 10; i++)
            {
                if (TryGetLong(row, $"OriginItem{i}", out var origin) && origin > 0)
                {
                    TryGetLong(row, $"OriginCount{i}", out var count);
                    parts.Add($"-{Describe(origin)} x{Math.Max(1, count)}");
                }
            }
            for (var i = 1; i <= 10; i++)
            {
                if (TryGetLong(row, $"ExchangeItem{i}", out var reward) && reward > 0)
                {
                    TryGetLong(row, $"ExchangeCount{i}", out var count);
                    parts.Add($"+{Describe(reward)} x{Math.Max(1, count)}");
                }
            }
            exchanges[id] = parts.Count == 0 ? "empty exchange row" : string.Join(", ", parts);
        }
    }

    private void LoadQuestHelpers(string path)
    {
        var npcsByQuest = new Dictionary<long, SortedSet<long>>();
        var statesByQuest = new Dictionary<long, SortedSet<long>>();

        foreach (var row in ReadArray(path))
        {
            if (!TryGetLong(row, "Index", out var id))
                continue;
            TryGetLong(row, "EventDataIndex", out var quest);
            TryGetLong(row, "EventStatus", out var status);
            TryGetLong(row, "NpcId", out var npc);
            var statusName = status switch
            {
                0 => "NotStarted",
                1 => "InProgress",
                2 => "Completed",
                _ => status.ToString(),
            };
            TryGetLong(row, "EventTriggerIndex", out var trigger);
            if (trigger > 0 && npc != 0)
            {
                var key = (npc, trigger);
                if (!triggers.TryGetValue(key, out var seen))
                    triggers[key] = seen = [];
                seen.Add((quest, status));
            }

            if (quest == 0)
                continue;
            if (!npcsByQuest.TryGetValue(quest, out var npcs))
                npcsByQuest[quest] = npcs = [];
            if (npc != 0)
                npcs.Add(npc);
            if (!statesByQuest.TryGetValue(quest, out var states))
                statesByQuest[quest] = states = [];
            states.Add(status);
        }

        foreach (var (quest, npcs) in npcsByQuest)
        {
            questNpcs[quest] = [.. npcs];
            var states = statesByQuest.TryGetValue(quest, out var found) ? found : [];
            var names = npcs
                .Select(id => npcNames.TryGetValue(id, out var name) ? name : id.ToString())
                .Take(3)
                .ToArray();
            var who = names.Length == 0
                ? "no NPC"
                : string.Join(", ", names) + (npcs.Count > names.Length ? ", …" : string.Empty);
            quests[quest] = $"{who} · states {string.Join("/", states)}";
        }
    }

    private void LoadQuestTitles(string path)
    {
        if (!File.Exists(path))
            return;
        foreach (var row in ReadArray(path))
        {
            if (!TryGetLong(row, "QuestNum", out var id))
                continue;
            if (!row.TryGetProperty("Title", out var title))
                continue;
            var text = title.GetString();
            if (text is { Length: > 0 })
                questTitles[id] = text;
        }
    }

    private void LoadQuestLocations(string path)
    {
        if (!File.Exists(path))
            return;
        foreach (var row in ReadArray(path))
        {
            if (!TryGetLong(row, "Num", out var id))
                continue;
            var name = row.TryGetProperty("Name", out var found) ? found.GetString() : null;
            if (name is null or { Length: 0 })
                continue;
            var where = row.TryGetProperty("Where", out var place) ? place.GetString() : null;
            maps[id] = where is { Length: > 0 } ? $"{name} \u2014 {where}" : name;
        }
    }

    private void LoadNpcs(string path)
    {
        foreach (var row in ReadArray(path))
        {
            if (!TryGetLong(row, "Id", out var id))
                continue;
            npcNames[id] = row.TryGetProperty("Name", out var name) ? name.GetString() ?? "?" : "?";
        }
    }

    private void LoadZones(string path)
    {
        foreach (var row in ReadArray(path))
        {
            if (!TryGetLong(row, "ZoneNo", out var id))
                continue;
            zones[id] = row.TryGetProperty("MapName", out var name) ? name.GetString() ?? "?" : "?";
        }
    }

    private void LoadQuestText(string path)
    {
        if (!File.Exists(path))
            return;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            return;
        Fill(document.RootElement, "menu", menu);
        Fill(document.RootElement, "talk", talk);
    }

    private static void Fill(JsonElement root, string property, Dictionary<long, string> target)
    {
        if (!root.TryGetProperty(property, out var section) || section.ValueKind != JsonValueKind.Object)
            return;
        foreach (var entry in section.EnumerateObject())
        {
            if (!long.TryParse(entry.Name, out var id))
                continue;
            target[id] = entry.Value.GetString() ?? string.Empty;
        }
    }

    private string Describe(long itemId) =>
        itemNames.TryGetValue(itemId, out var name) ? name : itemId.ToString();

    private static IEnumerable<JsonElement> ReadArray(string path)
    {
        if (!File.Exists(path))
            yield break;
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var row in document.RootElement.EnumerateArray())
            yield return row.Clone();
    }

    private static bool TryGetLong(JsonElement row, string property, out long value)
    {
        value = 0;
        if (!row.TryGetProperty(property, out var element))
            return false;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value))
            return true;
        if (element.ValueKind == JsonValueKind.String && long.TryParse(element.GetString(), out value))
            return true;
        return false;
    }
}
