using LibreKO.Quests.Binding;

namespace LibreKO.Quests.Runtime;

public static class QuestProgramLinks
{
    public static QuestProgram? Resolve(
        QuestProgram source, BorrowedEvent borrow, IReadOnlyList<QuestProgram> programs, out string? problem)
    {
        var stem = Path.GetFileNameWithoutExtension(borrow.File);
        var target = programs.FirstOrDefault(p => string.Equals(
            Path.GetFileNameWithoutExtension(p.FileName), stem, StringComparison.OrdinalIgnoreCase));
        problem = target is null
            ? $"there is no quest file called \"{stem}\""
            : !target.EventNames.TryGetValue(borrow.Name, out var id) || !target.Events.ContainsKey(id)
                ? $"\"{stem}\" does not answer it"
                : target.NpcId != source.NpcId || target.ZoneId != 0 && target.ZoneId != source.ZoneId
                    ? $"\"{stem}\" is not one of NPC {source.NpcId}'s files in zone {source.ZoneId}"
                    : null;
        return problem is null ? target : null;
    }

    public static HashSet<(QuestProgram Program, int Event)> Reachable(IReadOnlyList<QuestProgram> programs)
    {
        var links = new Dictionary<(QuestProgram, int), (QuestProgram, int)>();
        var pending = new Stack<(QuestProgram Program, int Event)>();
        foreach (var program in programs)
        {
            foreach (var (name, id) in program.EventNames)
                if (program.Events.ContainsKey(id) && QuestProgram.IsEntryName(name))
                    pending.Push((program, id));
            foreach (var borrow in program.Borrowed)
                if (Resolve(program, borrow, programs, out _) is { } target
                    && program.EventNames.TryGetValue(borrow.Name, out var from))
                    links[(program, from)] = (target, target.EventNames[borrow.Name]);
        }
        var reached = new HashSet<(QuestProgram Program, int Event)>();
        while (pending.TryPop(out var next))
        {
            if (links.TryGetValue(next, out var linked))
                next = linked;
            if (!next.Program.TryGetEvent(next.Event, out var entry) || !reached.Add(next))
                continue;
            foreach (var target in Targets(entry.Body))
                if (target >= 0)
                    pending.Push((next.Program, target));
        }
        return reached;
    }

    private static IEnumerable<int> Targets(IReadOnlyList<BoundStatement> body)
    {
        foreach (var statement in body)
        {
            if (statement is BoundStatement.Goto jump)
                yield return jump.TargetEvent;
            if (statement is BoundStatement.Dialog dialog)
                foreach (var choice in dialog.Choices)
                    yield return choice.Button.TargetEvent;
            IEnumerable<IReadOnlyList<BoundStatement>> nested = statement switch
            {
                BoundStatement.Dialog d => [d.Fallback ?? []],
                BoundStatement.Reward r => [r.Body, r.ElseBody ?? []],
                BoundStatement.If branch => branch.Arms.Select(a => a.Body).Append(branch.ElseBody ?? []),
                BoundStatement.Switch dispatch => dispatch.Cases.Select(c => c.Body).Append(dispatch.DefaultBody ?? []),
                _ => []
            };
            foreach (var part in nested)
                foreach (var target in Targets(part))
                    yield return target;
        }
    }
}
