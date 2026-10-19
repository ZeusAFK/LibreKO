using Godot;

namespace LibreKO.Features.Windows;

internal static class QuestInfo
{
    internal static string StatusText(QuestBook book, int questId, int state) =>
        book.Views.TryGetValue(questId, out var view) ? view.StateLabel : StateName(state);

    internal static (int Current, int Target) Progress(QuestBook book, int questId)
    {
        if (book.Objectives.TryGetValue(questId, out var declared))
            return declared.Progress(book.Kills.GetValueOrDefault(questId) ?? []);
        var groups = Groups(book, questId);
        if (groups.Length == 0) return (0, 0);
        book.Kills.TryGetValue(questId, out var counts);
        int current = 0, target = 0;
        for (int g = 0; g < groups.Length; g++)
        {
            target += groups[g].Count;
            current += Mathf.Min(counts != null && g < counts.Length ? counts[g] : 0, groups[g].Count);
        }
        return (current, target);
    }

    internal static QuestData.KillGroup[] Groups(QuestBook book, int questId) =>
        book.Objectives.TryGetValue(questId, out var declared)
        ? [.. declared.Groups.Select(group => new QuestData.KillGroup(group.Monsters, group.Count))]
        : QuestData.Groups(questId);

    internal static QuestData.Facts Facts(QuestBook book, int questId)
    {
        if (book.Views.TryGetValue(questId, out var view))
            return new QuestData.Facts(0, 0, 0, view.ZoneId, 0, 0,
                view.Objectives.Groups.Length > 0 ? QuestData.Kind.Hunt
                : view.Transfers.Any(t => t.Take) ? QuestData.Kind.Delivery : QuestData.Kind.Story);
        var facts = QuestData.Get(questId);
        return book.Objectives.ContainsKey(questId) ? facts with { Kind = QuestData.Kind.Hunt } : facts;
    }

    internal static string ZoneName(int zone)
    {
        if (zone == 0) return "Anywhere";
        foreach (var z in ZoneCatalog.All) if (z.Id == zone) return z.Name;
        return $"Zone {zone}";
    }

    internal static string NpcDisplayName(int npcId) =>
        NpcNames.TalkName(npcId);

    internal static List<(string Mob, int Current, int Target)> KillProgress(QuestBook book, int questId)
    {
        var groups = Groups(book, questId);
        var lines = new List<(string, int, int)>(groups.Length);
        book.Kills.TryGetValue(questId, out var counts);
        for (int g = 0; g < groups.Length; g++)
        {
            int cur = counts != null && g < counts.Length ? counts[g] : 0;
            int target = groups[g].Count;
            int firstNpc = groups[g].Npcs[0];
            string mob = book.Objectives.TryGetValue(questId, out var goals) && goals.Groups[g].Name is { } named
                ? named
                : firstNpc is Nations.Karus or Nations.ElMorad ? $"{Nations.Name(firstNpc)} players"
                : NpcNames.Name(firstNpc, true);
            lines.Add((mob, Mathf.Min(cur, target), target));
        }
        return lines;
    }

    internal static IEnumerable<(string Label, int Done, int Needed)> ObjectiveSteps(QuestBook book, Inventory inventory, int questId)
    {
        var groups = Groups(book, questId);
        book.Kills.TryGetValue(questId, out var counts);
        for (int g = 0; g < groups.Length; g++)
        {
            int firstNpc = groups[g].Npcs[0];
            string mob = book.Objectives.TryGetValue(questId, out var goals) && goals.Groups[g].Name is { } named
                ? named
                : firstNpc is Nations.Karus or Nations.ElMorad ? $"{Nations.Name(firstNpc)} players"
                : NpcNames.Name(firstNpc, true);
            int cur = counts != null && g < counts.Length ? counts[g] : 0;
            yield return (mob, Mathf.Min(cur, groups[g].Count), groups[g].Count);
        }
        foreach (var want in book.HandIns(questId))
            yield return (ItemData.DisplayName(want.ItemId),
                Mathf.Min(inventory.CountOf(want.ItemId), want.Count), want.Count);
    }

    internal static string Name(QuestBook book, int questId, string selfName) =>
        QuestMarkup.Plain(NameSource(book, questId, selfName), selfName);

    internal static string NameSource(QuestBook book, int questId, string selfName) =>
        book.Views.TryGetValue(questId, out var view)
        ? string.IsNullOrEmpty(view.Title) ? $"Quest {questId}" : view.Title
    : book.Strings.TryGetValue(questId, out var text) && text.Title.Length > 0
        ? text.Title
        : QuestData.Name(questId, selfName);

    internal static string Journal(QuestBook book, int questId, string selfName) =>
        QuestMarkup.Plain(JournalSource(book, questId, selfName), selfName).Trim();

    internal static string JournalSource(QuestBook book, int questId, string selfName) =>
        (book.Views.TryGetValue(questId, out var view) ? view.Journal
            : book.Strings.TryGetValue(questId, out var text) && text.Journal.Length > 0
                ? text.Journal : QuestData.Objective(questId, selfName))
    .Trim();

    internal static string StateName(int state) => state switch
    {
        1 => "In progress", 2 => "Completed", 3 => "Ready to turn in", 4 => "Abandoned", _ => "Available",
    };

    internal static Color StateColor(int state) => state switch
    {
        2 => UiTheme.Good, 3 => UiTheme.GoldBright, 4 => UiTheme.TextDim, _ => UiTheme.TextHi,
    };
}
