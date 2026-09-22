using LibreKO.Quests.Binding;
using LibreKO.Quests.Text;

namespace LibreKO.Quests.Runtime;

public static class QuestProgramComposer
{
    public static QuestProgram Compose(string name, int npc, int zone, IReadOnlyList<QuestProgram> programs)
    {
        var events = new Dictionary<int, QuestEventEntry>();
        var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var locations = new List<QuestLocation>();
        var rewards = new List<RewardDefinition>();
        var topics = new List<DialogChoice>();
        var automaticTopics = new List<DialogChoice>();
        IReadOnlyList<BoundStatement>? fallback = null;
        var next = QuestProgram.LocalEventBase;
        var maps = new Dictionary<QuestProgram, Dictionary<int, int>>();
        foreach (var program in programs)
        {
            var assigned = program.Events.Keys.ToDictionary(id => id, _ => next++);
            maps[program] = assigned;
        }
        foreach (var program in programs)
        {
            foreach (var borrow in program.Borrowed)
                if (QuestProgramLinks.Resolve(program, borrow, programs, out _) is { } target
                    && program.EventNames.TryGetValue(borrow.Name, out var id))
                    maps[program][id] = maps[target][target.EventNames[borrow.Name]];
        }
        foreach (var program in programs)
        {
            var ids = maps[program];
            var offset = locations.Count;
            var rewardOffset = rewards.Count;
            locations.AddRange(program.Locations);
            rewards.AddRange(program.Rewards);
            int Target(int id) => id < 0 ? id : ids.GetValueOrDefault(id, -1);
            DialogChoice Choice(DialogChoice choice) => choice with
            {
                Button = choice.Button with { TargetEvent = Target(choice.Button.TargetEvent) }
            };
            IReadOnlyList<BoundStatement> Body(IReadOnlyList<BoundStatement> body) => body.Select(Statement).ToArray();
            BoundStatement Statement(BoundStatement statement) => statement switch
            {
                BoundStatement.Goto jump => jump with { TargetEvent = Target(jump.TargetEvent) },
                BoundStatement.Dialog dialog => dialog with
                {
                    Choices = dialog.Choices.Select(Choice).ToArray(),
                    Fallback = dialog.Fallback is null ? null : Body(dialog.Fallback)
                },
                BoundStatement.Reward reward => reward with
                {
                    Body = Body(reward.Body),
                    ElseBody = reward.ElseBody is null ? null : Body(reward.ElseBody)
                },
                BoundStatement.If branch => branch with
                {
                    Arms = branch.Arms.Select(a => (a.Condition, Body(a.Body))).ToArray(),
                    ElseBody = branch.ElseBody is null ? null : Body(branch.ElseBody)
                },
                BoundStatement.Switch dispatch => dispatch with
                {
                    Cases = dispatch.Cases.Select(c => (
                        dispatch.Selector == SwitchSelectorKind.Event
                            ? (IReadOnlyList<long>)c.Labels.Select(id => (long)Target((int)id)).ToArray() : c.Labels,
                        Body(c.Body))).ToArray(),
                    DefaultBody = dispatch.DefaultBody is null ? null : Body(dispatch.DefaultBody)
                },
                BoundStatement.Action { Kind: QuestActionKind.ShowMap } action => action with
                {
                    Arguments = new ArgumentSet(new Dictionary<string, long>(action.Arguments.Values)
                    { ["map"] = action.Arguments.Get("map") + offset })
                },
                BoundStatement.Action { Kind: QuestActionKind.GiveRandomReward } action => action with
                {
                    Arguments = new ArgumentSet(new Dictionary<string, long>(action.Arguments.Values)
                    { ["reward"] = action.Arguments.Get("reward") + rewardOffset })
                },
                _ => statement
            };
            var localBindings = program.Bindings.Where(b => b.NpcId == npc && (b.ZoneId == 0 || b.ZoneId == zone)).ToArray();
            BoundCondition? nationGuard = null;
            if (localBindings.Length > 0 && localBindings.All(b => b.Nation > 0))
                foreach (var nation in localBindings.Select(b => b.Nation).Distinct())
                {
                    var condition = new BoundCondition.Predicate(QuestConditionKind.PlayerNation, CompareOperator.Equal,
                        new ArgumentSet(new Dictionary<string, long> { ["nation"] = nation }), default);
                    nationGuard = nationGuard is null ? condition : new BoundCondition.Or(nationGuard, condition);
                }
            foreach (var (id, entry) in program.Events)
            {
                var body = Body(entry.Body);
                if (nationGuard is not null)
                {
                    if (program.TryGetEntry(QuestProgram.TopicsEvent, 0, out var topicEntry) && id == topicEntry
                        && body is [BoundStatement.Dialog menu])
                        body = [menu with { Choices = menu.Choices.Select(c => c with
                        {
                            When = c.When is null ? nationGuard : new BoundCondition.And(nationGuard, c.When)
                        }).ToArray() }];
                    else
                        body = [new BoundStatement.If(entry.Span, [(nationGuard, body)], null)];
                }
                events[ids[id]] = entry with { Id = ids[id], Body = body };
            }
            foreach (var (eventName, id) in program.EventNames)
            {
                if (program.TryGetEvent(id, out _))
                    names.TryAdd(eventName, ids[id]);
            }
            if (program.TryGetEntry(QuestProgram.TopicsEvent, 0, out var topicId)
                && events[ids[topicId]].Body is [BoundStatement.Dialog dialog])
            {
                var owned = program.DefaultQuestId > 0
                    ? dialog.Choices.Select(c => c with { QuestId = program.DefaultQuestId }).ToArray()
                    : dialog.Choices;
                topics.AddRange(owned);
                if (program.Flows.Count > 0)
                    automaticTopics.AddRange(owned);
                fallback ??= dialog.Fallback;
            }
        }
        if (!names.ContainsKey(QuestProgram.GreetingEvent) && topics.Count > 0)
        {
            var id = next++;
            names[QuestProgram.GreetingEvent] = id;
            events[id] = new QuestEventEntry(id, QuestProgram.GreetingEvent, default,
                [new BoundStatement.Dialog(default, DialogStyle.Talk, -1, DialogLine.None, [])]);
        }
        if (names.TryGetValue(QuestProgram.GreetingEvent, out var greeting))
        {
            topics = topics.Select(c => c.Button.TargetEvent == -2
                ? c with { Button = c.Button with { TargetEvent = greeting } } : c).ToList();
            fallback = fallback is null ? null : ResolveGreeting(fallback, greeting);
            foreach (var id in events.Keys.ToArray())
                events[id] = events[id] with { Body = ResolveGreeting(events[id].Body, greeting) };
        }
        return new QuestProgram(name, npc, null, new SourceText("", name), events, names, locations, zone,
            programs.SelectMany(p => p.Objectives).DistinctBy(q => q.QuestId).ToArray(),
            texts: programs.SelectMany(p => p.Texts).DistinctBy(q => (q.QuestId, q.Nation, q.ClassGroup)).ToArray(), hasBinding: true,
            rewards: rewards)
        {
            Bindings = programs.SelectMany(p => p.Bindings).Distinct().ToArray(),
            GreetingTopics = topics,
            AutomaticTopics = automaticTopics,
            QuestRewards = programs.SelectMany(p => p.QuestRewards).DistinctBy(r => (r.QuestId, r.ClassGroup, r.Nation)).ToArray(),
            Flows = programs.SelectMany(p => p.Flows).DistinctBy(r => r.QuestId).ToArray(),
            GreetingFallback = fallback
        };
    }
    private static IReadOnlyList<BoundStatement> ResolveGreeting(IReadOnlyList<BoundStatement> body, int greeting) =>
        body.Select<BoundStatement, BoundStatement>(statement => statement switch
        {
            BoundStatement.Goto { TargetEvent: -2 } jump => jump with { TargetEvent = greeting },
            BoundStatement.Dialog dialog => dialog with
            {
                Choices = dialog.Choices.Select(c => c.Button.TargetEvent == -2
                    ? c with { Button = c.Button with { TargetEvent = greeting } } : c).ToArray(),
                Fallback = dialog.Fallback is null ? null : ResolveGreeting(dialog.Fallback, greeting)
            },
            BoundStatement.Reward reward => reward with
            {
                Body = ResolveGreeting(reward.Body, greeting),
                ElseBody = reward.ElseBody is null ? null : ResolveGreeting(reward.ElseBody, greeting)
            },
            BoundStatement.If branch => branch with
            {
                Arms = branch.Arms.Select(a => (a.Condition, ResolveGreeting(a.Body, greeting))).ToArray(),
                ElseBody = branch.ElseBody is null ? null : ResolveGreeting(branch.ElseBody, greeting)
            },
            BoundStatement.Switch dispatch => dispatch with
            {
                Cases = dispatch.Cases.Select(c => (c.Labels, ResolveGreeting(c.Body, greeting))).ToArray(),
                DefaultBody = dispatch.DefaultBody is null ? null : ResolveGreeting(dispatch.DefaultBody, greeting)
            },
            _ => statement
        }).ToArray();

}
