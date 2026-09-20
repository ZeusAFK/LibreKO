using LibreKO.Quests.Binding;

namespace LibreKO.Quests.Runtime;

public sealed record QuestExecutionResult(
    bool Handled,
    int StatementsExecuted,
    IReadOnlyList<int> EventChain,
    string? Failure);

public sealed class QuestInterpreter
{
    public const int MaxEventHops = 8;
    public const int MaxStatements = 4096;

    private readonly QuestProgram _program;
    private readonly IQuestHost _host;
    private readonly int _chosenReward;
    private readonly List<int> _chain = [];
    private int _statements;
    private int? _pendingJump;
    private string? _failure;
    private bool _buildingGreeting;
    private int _viewQuest = -1;
    private bool _viewShown;

    public QuestInterpreter(QuestProgram program, IQuestHost host, int chosenReward = -1)
    {
        _program = program;
        _host = host;
        _chosenReward = chosenReward;
    }

    public QuestExecutionResult Run(int eventId)
    {
        _buildingGreeting = _program.TryGetGreeting(out var greeting) && greeting == eventId;
        var current = eventId;
        var visited = new HashSet<int>();

        while (true)
        {
            if (!_program.TryGetEvent(current, out var entry))
            {
                var handled = _chain.Count > 0;
                if (!handled)
                    _failure ??= $"event {current} is not handled by {_program.FileName}";
                return new QuestExecutionResult(handled, _statements, _chain, _failure);
            }

            if (!visited.Add(current))
            {
                _failure = $"event {current} was entered twice in one interaction";
                return new QuestExecutionResult(true, _statements, _chain, _failure);
            }

            _chain.Add(current);
            if (_chain.Count > MaxEventHops)
            {
                _failure = $"more than {MaxEventHops} event hops in one interaction";
                return new QuestExecutionResult(true, _statements, _chain, _failure);
            }

            if (_program.TryGetGreeting(out var greetingId) && current == greetingId)
                _buildingGreeting = true;
            _pendingJump = null;
            Execute(entry.Body);

            if (_failure is not null)
                return new QuestExecutionResult(true, _statements, _chain, _failure);

            if (_pendingJump is not { } next)
                return new QuestExecutionResult(true, _statements, _chain, null);

            current = next;
        }
    }

    private void Execute(IReadOnlyList<BoundStatement> body)
    {
        foreach (var statement in body)
        {
            if (_pendingJump is not null || _failure is not null)
                return;

            if (++_statements > MaxStatements)
            {
                _failure = $"more than {MaxStatements} statements in one interaction";
                return;
            }

            switch (statement)
            {
                case BoundStatement.View view:
                    ShowView(view.QuestId, view.Silent);
                    break;

                case BoundStatement.Reward reward:
                {
                    var transfers = new List<BoundStatement.Action>();
                    var onSuccess = new List<BoundStatement>();
                    var selected = Collect(reward.Body, transfers, onSuccess);
                    _host.ApplyReward(selected ? transfers : []);
                    if (selected && !_host.ActionFailed)
                        Execute(onSuccess);
                    else if (reward.ElseBody is not null)
                        Execute(reward.ElseBody);
                    break;
                }

                case BoundStatement.Dialog dialog:
                    if (!ShowDialog(dialog))
                        return;
                    break;

                case BoundStatement.Say say:
                    _host.Say(say.Lines);
                    break;

                case BoundStatement.Goto jump:
                    _pendingJump = jump.TargetEvent;
                    return;

                case BoundStatement.If node:
                    ExecuteIf(node);
                    break;

                case BoundStatement.Switch node:
                    ExecuteSwitch(node);
                    break;

                case BoundStatement.Action action:
                    ExecuteAction(action);
                    break;
            }
        }
    }

    private const string InProgressTag = "[In progress] ";
    private const string ReadyTag = "[Ready] ";

    private DialogChoice TagWithProgress(DialogChoice choice)
    {
        if (choice.QuestId <= 0 || !choice.Button.Label.HasText)
            return choice;
        if (_host.QuestStatus(choice.QuestId) is not (1 or 3))
            return choice;
        var goals = _program.Objectives.FirstOrDefault(o => o.QuestId == choice.QuestId);
        var rewards = _program.RewardsFor(choice.QuestId, _host.PlayerClassGroup);
        var measurable = goals is { Groups.Count: > 0 } || rewards is not null;
        var ready = measurable && ObjectivesComplete(choice.QuestId)
                    && (rewards is null || CostsAvailable(rewards));
        var label = DialogLine.FromText((ready ? ReadyTag : InProgressTag) + choice.Button.Label.Text);
        return choice with { Button = choice.Button with { Label = label } };
    }

    private bool ShowDialog(BoundStatement.Dialog dialog)
    {
        var greeting = _buildingGreeting;
        if (_buildingGreeting)
        {
            _buildingGreeting = false;
            var menu = dialog.Choices.Count == 0 ? _program.GreetingTopics : _program.AutomaticTopics;
            dialog = dialog with
            {
                Choices = [.. menu.Select(TagWithProgress), .. dialog.Choices],
                Fallback = dialog.Fallback ?? _program.GreetingFallback
            };
        }
        var offered = new List<DialogButton>();
        var gated = 0;
        var qualified = 0;
        foreach (var choice in dialog.Choices)
        {
            if (choice.When is null)
            {
                offered.Add(choice.Button);
                continue;
            }
            gated++;
            if (!Evaluate(choice.When))
                continue;
            qualified++;
            offered.Add(choice.Button);
        }

        if (gated > 0 && qualified == 0 && dialog.Fallback is { } instead)
        {
            Execute(instead);
            return _failure is null;
        }
        if (greeting && !dialog.Header.HasText && offered.Count == 0)
            return true;

        if (offered.Count > Binding.QuestVocabulary.MaxDialogButtons)
        {
            _failure = $"The dialog offers more than {Binding.QuestVocabulary.MaxDialogButtons} buttons.";
            return false;
        }

        var questId = _viewQuest > 0 ? _viewQuest : dialog.QuestId;
        if (_program.Flows.Any(f => f.QuestId == questId))
        {
            _viewShown = true;
            _host.ShowQuestView(BuildView(questId, dialog.Header, offered, dialog.Page));
        }
        else
            _host.ShowDialog(dialog.Style, dialog.QuestId, dialog.Header, offered);
        return true;
    }

    private bool ObjectivesComplete(int questId)
    {
        var goals = _program.Objectives.FirstOrDefault(o => o.QuestId == questId);
        if (goals is not { Groups.Count: > 0 })
            return true;
        var done = goals.Groups.Select((g, index) => _host.KillCount(questId, index + 1) >= g.Count);
        return goals.Rule == ObjectiveRule.Any ? done.Any(v => v) : done.All(v => v);
    }

    private bool CostsAvailable(QuestRewards rewards) => rewards.Transfers
        .Where(a => a.Kind is QuestActionKind.TakeItem or QuestActionKind.TakeGold or QuestActionKind.TakeNationalPoints)
        .GroupBy(a => (a.Kind, Item: a.Arguments.GetInt("item")))
        .All(group => (long)(group.Key.Kind switch
        {
            QuestActionKind.TakeGold => _host.PlayerCoins,
            QuestActionKind.TakeNationalPoints => _host.PlayerLoyalty,
            _ => _host.ItemCount(group.Key.Item)
        }) >= group.Sum(a => a.Arguments.Get("amount", a.Arguments.Get("count", 1))));

    private IReadOnlyList<BoundStatement.Action> ResolvePremium(IReadOnlyList<BoundStatement.Action> transfers)
    {
        if (!transfers.Any(a => a.Arguments.Has("premium")))
            return transfers;
        return [.. transfers.Select(a => !a.Arguments.Has("premium") ? a : a with
        {
            Arguments = new ArgumentSet(new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["amount"] = _host.PremiumType > 0 ? a.Arguments.Get("premium") : a.Arguments.Get("amount")
            })
        })];
    }

    public QuestView BuildView(int questId, DialogLine? dialogue = null, IReadOnlyList<DialogButton>? topics = null,
        QuestPageKind page = QuestPageKind.Quest)
    {
        var flow = _program.Flows.Single(f => f.QuestId == questId);
        var classGroup = _host.PlayerClassGroup;
        var rewards = _program.RewardsFor(questId, classGroup)
            ?? throw new InvalidOperationException($"Quest {questId} has no rewards.");
        rewards = rewards with { Transfers = ResolvePremium(rewards.Transfers) };
        var text = _program.TextFor(questId, _host.PlayerNation, classGroup)
            ?? throw new InvalidOperationException($"Quest {questId} has no text.");
        var state = _host.QuestStatus(questId) switch
        {
            1 or 3 => ObjectivesComplete(questId) && CostsAvailable(rewards)
                ? QuestViewState.Claimable : QuestViewState.InProgress,
            2 => QuestViewState.Completed,
            0 or 4 when flow.Requires is null || Evaluate(flow.Requires) => QuestViewState.Available,
            _ => QuestViewState.Locked
        };
        var goals = _program.Objectives.FirstOrDefault(o => o.QuestId == questId);
        return new QuestView(text, flow.BindingFor(_host.PlayerNation, _host.PlayerZone, classGroup)?.ZoneId ?? flow.ZoneId, state, goals,
            goals?.Groups.Select((_, index) => _host.KillCount(questId, index + 1)).ToArray() ?? [],
            rewards, dialogue ?? DialogLine.FromText(text.Journal ?? string.Empty), topics ?? [], page, flow.AutoAccept);
    }

    private void ShowView(int questId, bool silent = false)
    {
        var view = BuildView(questId);
        if (silent)
        {
            _host.UpdateQuestView(view);
            return;
        }
        var role = view.State switch
        {
            QuestViewState.Available => "offer",
            QuestViewState.InProgress => "in_progress",
            QuestViewState.Claimable => "claimable",
            QuestViewState.Completed => "completed",
            _ => string.Empty
        };
        _viewQuest = questId;
        _viewShown = false;
        if (_program.TryGetEntry(role, questId, out var id) && _program.TryGetEvent(id, out var entry))
            Execute(entry.Body);
        if (!_viewShown && _pendingJump is null)
            _host.ShowQuestView(view);
        _viewQuest = -1;
    }

    private void ExecuteIf(BoundStatement.If node)
    {
        foreach (var (condition, body) in node.Arms)
        {
            if (!Evaluate(condition))
                continue;
            Execute(body);
            return;
        }
        if (node.ElseBody is not null)
            Execute(node.ElseBody);
    }

    private bool Collect(
        IReadOnlyList<BoundStatement> body,
        List<BoundStatement.Action> transfers,
        List<BoundStatement> onSuccess)
    {
        foreach (var statement in body)
        {
            switch (statement)
            {
                case BoundStatement.Action action when IsTransfer(action):
                    transfers.Add(action);
                    break;

                case BoundStatement.Action { Kind: QuestActionKind.GiveRandomReward } action:
                    if (Draw(action) is { } drawn)
                        transfers.Add(drawn);
                    break;

                case BoundStatement.Switch node:
                {
                    var chosen = SelectCase(node);
                    if (chosen is null)
                    {
                        if (node.Selector == SwitchSelectorKind.Reward)
                            return false;
                        break;
                    }
                    if (!Collect(chosen, transfers, onSuccess))
                        return false;
                    break;
                }

                case BoundStatement.If node:
                {
                    var arm = node.Arms.FirstOrDefault(a => Evaluate(a.Condition)).Body ?? node.ElseBody;
                    if (arm is not null && !Collect(arm, transfers, onSuccess))
                        return false;
                    break;
                }

                default:
                    onSuccess.Add(statement);
                    break;
            }
        }
        return true;
    }

    public static int RentalHours(ArgumentSet arguments) =>
        arguments.GetInt("hours") + arguments.GetInt("days") * QuestVocabulary.HoursPerDay;

    private static bool IsTransfer(BoundStatement.Action action) =>
        action.Kind is QuestActionKind.GiveItem or QuestActionKind.TakeItem
            or QuestActionKind.GiveGold or QuestActionKind.TakeGold
            or QuestActionKind.GiveExperience or QuestActionKind.GiveNationalPoints
            or QuestActionKind.TakeNationalPoints;

    private IReadOnlyList<BoundStatement>? SelectCase(BoundStatement.Switch node)
    {
        var value = SelectorValue(node);
        foreach (var (labels, body) in node.Cases)
        {
            var matched = node.Selector == SwitchSelectorKind.PlayerClass
                ? labels.Any(label => MatchesClass(label, value))
                : labels.Contains(value);
            if (matched)
                return body;
        }
        return node.DefaultBody;
    }

    private int SelectorValue(BoundStatement.Switch node) => node.Selector switch
    {
        SwitchSelectorKind.PlayerClass => _host.PlayerClassGroup,
        SwitchSelectorKind.PlayerNation => _host.PlayerNation,
        SwitchSelectorKind.Event => _chain.Count > 0 ? _chain[^1] : 0,
        SwitchSelectorKind.Roll => _host.RollDice(node.Max - 1),
        SwitchSelectorKind.Reward => _chosenReward,
        _ => 0,
    };

    private void ExecuteSwitch(BoundStatement.Switch node)
    {
        if (SelectCase(node) is { } chosen)
            Execute(chosen);
        else if (node.Selector == SwitchSelectorKind.Reward)
            _host.ApplyReward([]);
    }

    private bool MatchesClass(long label, int group) =>
        label switch
        {
            QuestVocabulary.ClassGroupWarrior or QuestVocabulary.ClassGroupRogue
                or QuestVocabulary.ClassGroupMage or QuestVocabulary.ClassGroupPriest
                or QuestVocabulary.ClassGroupKurian => label == group,
            _ => label == _host.PlayerClassSubtype,
        };


    private BoundStatement.Action? Draw(BoundStatement.Action action)
    {
        if (!_program.TryGetReward(action.Arguments.GetInt("reward"), out var reward))
            return null;

        var row = reward.Rows[reward.Rows.Count == 1 ? 0 : _host.RollDice(reward.Rows.Count - 1)];
        var total = row.Sum(choice => choice.Weight);
        var roll = total <= 1 ? 0 : _host.RollDice((int)total - 1);
        var chosen = row[^1];
        foreach (var choice in row)
        {
            if (roll < choice.Weight)
            {
                chosen = choice;
                break;
            }
            roll -= (int)choice.Weight;
        }

        return new BoundStatement.Action(action.Span, QuestActionKind.GiveItem,
            new ArgumentSet(new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["item"] = chosen.Item,
                ["count"] = chosen.Count,
            }));
    }

    private void ClaimQuest(int questId)
    {
        if (_host.ActionFailed || _host.QuestStatus(questId) is not (1 or 3))
            return;
        var rewards = _program.QuestRewards.FirstOrDefault(r => r.QuestId == questId);
        if (rewards is null)
        {
            _failure = $"Quest {questId} has no declared Rewards.";
            return;
        }
        if (!ObjectivesComplete(questId))
            return;
        var transfers = ResolvePremium(rewards.Transfers);
        if (rewards.Options.Count > 0)
        {
            if (_chosenReward < 0 || _chosenReward >= rewards.Options.Count)
            {
                _failure = $"Quest {questId} offers a choice of reward and none was chosen.";
                return;
            }
            transfers = [.. transfers, rewards.Options[_chosenReward]];
        }
        _host.ApplyReward(transfers);
        if (_host.ActionFailed)
            return;
        _host.SetQuestState(questId, 2);
        if (transfers.Count > 0) _host.ShowReceipt(questId);
    }

    private void ExecuteAction(BoundStatement.Action action)
    {
        var args = action.Arguments;
        switch (action.Kind)
        {
            case QuestActionKind.RefuseReward:
                _host.ApplyReward([]);
                break;

            case QuestActionKind.GiveRandomReward:
                if (Draw(action) is { } prize)
                    _host.GiveItem(prize.Arguments.GetInt("item"), prize.Arguments.GetInt("count", 1), 0);
                break;

            case QuestActionKind.Todo:
            case QuestActionKind.DoNothing:
                break;

            case QuestActionKind.SetQuestState:
                _host.SetQuestState(args.GetInt("quest"), args.GetInt("status"));
                break;

            case QuestActionKind.ClaimQuest:
                ClaimQuest(args.GetInt("quest"));
                break;

            case QuestActionKind.GiveItem:
                _host.GiveItem(args.GetInt("item"), args.GetInt("count", 1), RentalHours(args));
                break;

            case QuestActionKind.GivePremium:
                _host.GivePremium(args.GetInt("type"), args.GetInt("days"));
                break;

            case QuestActionKind.GiveClanPremium:
                _host.GiveClanPremium(args.GetInt("days"));
                break;

            case QuestActionKind.GiveAchievement:
                _host.GiveAchievement(args.GetInt("achievement"));
                break;

            case QuestActionKind.JoinTempleEvent:
                _host.JoinTempleEvent();
                break;

            case QuestActionKind.SetLevel:
                _host.SetPlayerLevel(args.GetInt("level"));
                break;

            case QuestActionKind.SetDrakiRift:
                _host.SetDrakiRift(args.GetInt("stage"), args.GetInt("substage"));
                break;

            case QuestActionKind.TakeItem:
                _host.TakeItem(args.GetInt("item"), args.GetInt("count", 1));
                break;

            case QuestActionKind.GiveGold:
                _host.GiveGold(args.GetInt("amount"));
                break;

            case QuestActionKind.TakeGold:
                _host.TakeGold(args.GetInt("amount"));
                break;

            case QuestActionKind.GiveExperience:
                _host.GiveExp(args.GetInt("amount"));
                break;

            case QuestActionKind.GiveNationalPoints:
                _host.GiveLoyalty(args.GetInt("amount"));
                break;

            case QuestActionKind.TakeNationalPoints:
                _host.TakeLoyalty(args.GetInt("amount"));
                break;

            case QuestActionKind.Exchange:
                _host.RunExchange(args.GetInt("exchange"));
                break;

            case QuestActionKind.QuestExchange:
                _host.RunQuestExchange(args.GetInt("exchange"), args.GetInt("award", _chosenReward));
                break;

            case QuestActionKind.GiveCash:
                _host.GiveCash(args.GetInt("amount"));
                break;

            case QuestActionKind.ChangeJob:
                _host.ChangeJob(args.GetInt("class"), args.GetInt("mastered", 1) != 0);
                break;

            case QuestActionKind.ExchangeTimes:
                _host.RunCountExchange(args.GetInt("exchange"), args.GetInt("count", 1));
                break;

            case QuestActionKind.MiningExchange:
                _host.RunMiningExchange(args.GetInt("ore"));
                break;

            case QuestActionKind.GenieExchange:
                _host.RunGenieExchange(args.GetInt("item"), args.GetInt("hours"));
                break;

            case QuestActionKind.ExchangeRandom:
            {
                var first = args.GetInt("first");
                var last = args.GetInt("last");
                var span = Math.Max(0, last - first);
                _host.RunExchange(first + _host.RollDice(span));
                break;
            }

            case QuestActionKind.ShowMap:
                if (_program.TryGetLocation(args.GetInt("map"), out var location))
                    _host.ShowLocation(location, args.GetInt("quest", _viewQuest));
                break;

            case QuestActionKind.Warp:
                _host.TeleportToZone(args.GetInt("zone"), args.GetInt("x"), args.GetInt("z"));
                break;

            case QuestActionKind.Cast:
                _host.CastSkill(args.GetInt("skill"));
                break;

            case QuestActionKind.Effect:
                _host.PlayEffect(args.GetInt("effect"));
                break;

            case QuestActionKind.DespawnNpc:
                _host.DespawnNpc();
                break;

            case QuestActionKind.Summon:
                _host.SummonNpc(args.GetInt("npc"), args.GetInt("count", 1), args.GetInt("x", 0), args.GetInt("z", 0));
                break;

            case QuestActionKind.NpcEffect:
                _host.PlayNpcEffect(args.GetInt("effect"));
                break;

            case QuestActionKind.PromoteClan:
                _host.PromoteClan(action.Arguments.GetInt("rank"));
                break;

            case QuestActionKind.TakeClanPoints:
                _host.TakeClanPoints(action.Arguments.GetInt("amount"));
                break;

            case QuestActionKind.Promote:
                _host.PromotePlayer();
                break;

            case QuestActionKind.PromoteNovice:
                _host.PromoteToNovice();
                break;

            case QuestActionKind.ResetStats:
                _host.ResetStatPoints();
                break;

            case QuestActionKind.ResetSkills:
                _host.ResetSkillPoints();
                break;

            case QuestActionKind.OpenRenamePanel:
                _host.OpenRenamePanel();
                break;

            case QuestActionKind.OpenJobChangePanel:
                _host.OpenJobChangePanel();
                break;

            case QuestActionKind.OpenClanRenamePanel:
                _host.OpenClanRenamePanel();
                break;

            case QuestActionKind.OpenStatSkillPanel:
                _host.OpenStatSkillPanel();
                break;

            default:
                _host.Unsupported(action.Kind.ToString());
                break;
        }
    }

    private bool Evaluate(BoundCondition condition) => condition switch
    {
        BoundCondition.Or node => Evaluate(node.Left) || Evaluate(node.Right),
        BoundCondition.And node => Evaluate(node.Left) && Evaluate(node.Right),
        BoundCondition.Not node => !Evaluate(node.Operand),
        BoundCondition.AlwaysFalse => false,
        BoundCondition.ViewState v => BuildView(v.QuestId).State == v.State,
        BoundCondition.Predicate node => EvaluatePredicate(node),
        _ => false,
    };

    private bool EvaluatePredicate(BoundCondition.Predicate node)
    {
        var args = node.Arguments;
        return node.Kind switch
        {
            QuestConditionKind.ItemCount =>
                Compare(_host.ItemCount(args.GetInt("item")), node.Operator, args.GetInt("count")),
            QuestConditionKind.HasItem => _host.ItemCount(args.GetInt("item")) > 0,
            QuestConditionKind.RoomFor =>
                Compare(_host.PlayerFreeSlots, node.Operator, args.GetInt("count", 1)),
            QuestConditionKind.CanReceiveItem =>
                _host.HasRoomForItem(args.GetInt("item"), args.GetInt("count")),
            QuestConditionKind.CanReceiveStacks => _host.CanReceiveStacks(args.GetInt("count")),
            QuestConditionKind.PlayerWeight => Compare(_host.PlayerWeight, node.Operator, args.GetInt("value")),
            QuestConditionKind.PlayerExperience => Compare(_host.PlayerExperience, node.Operator, args.GetInt("value")),
            QuestConditionKind.QuestStatus => _host.QuestStatus(args.GetInt("quest")) == args.GetInt("status"),
            QuestConditionKind.QuestStatusIn =>
                _host.QuestStatus(args.GetInt("quest")) is >= 0 and < 32 and var status
                && (args.GetInt("states") & (1 << status)) != 0,
            QuestConditionKind.PlayerClass => MatchesClass(args.GetInt("class"), _host.PlayerClassGroup),
            QuestConditionKind.PlayerClassSubtype => Compare(_host.PlayerClassSubtype, node.Operator, args.GetInt("value")),
            QuestConditionKind.PlayerNation => _host.PlayerNation == args.GetInt("nation"),
            QuestConditionKind.PlayerLevel => Compare(_host.PlayerLevel, node.Operator, args.GetInt("value")),
            QuestConditionKind.PlayerGold => Compare(_host.PlayerCoins, node.Operator, args.GetInt("amount")),
            QuestConditionKind.PlayerNationalPoints => Compare(_host.PlayerLoyalty, node.Operator, args.GetInt("amount")),
            QuestConditionKind.KillCount =>
                Compare(_host.KillCount(args.GetInt("quest"), args.GetInt("group")), node.Operator, args.GetInt("count")),
            QuestConditionKind.HasKillQuest => _host.HasActiveKillQuest(),
            QuestConditionKind.LeadsClan => _host.IsClanLeader,
            QuestConditionKind.InClan => _host.IsInClan,
            QuestConditionKind.ClanRank =>
                Compare(_host.ClanRank, node.Operator, args.GetInt("rank")),
            QuestConditionKind.ClanGrade =>
                Compare(_host.ClanGrade, node.Operator, args.GetInt("value")),
            QuestConditionKind.ClanPoints =>
                Compare(_host.ClanPoints, node.Operator, args.GetInt("amount")),
            QuestConditionKind.SkillPoints =>
                Compare(_host.SkillPoints(args.GetInt("tree")), node.Operator, args.GetInt("amount")),
            QuestConditionKind.InParty => _host.IsInParty,
            QuestConditionKind.LeadsParty => _host.IsPartyLeader,
            QuestConditionKind.IsKing => _host.IsKing,
            QuestConditionKind.DailyAvailable => _host.DailyRewardAvailable(args.GetInt("slot")),
            QuestConditionKind.HasPremium => Compare(_host.PremiumType, node.Operator, args.GetInt("value")),
            QuestConditionKind.LastStepFailed => _host.ActionFailed,
            QuestConditionKind.PlayerZone => _host.PlayerZone == args.GetInt("zone"),
            QuestConditionKind.MonumentNation => _host.MonumentNation == args.GetInt("nation"),
            QuestConditionKind.Chance => _host.RollChance(args.GetInt("percent")),
            QuestConditionKind.RollUnder => Compare(
                _host.RollDice(args.GetInt("max") - 1), node.Operator, args.GetInt("value")),
            QuestConditionKind.ReachedLevel =>
                _host.ReachedLevel(args.GetInt("level"), args.GetInt("percent")),
            _ => false,
        };
    }

    private static bool Compare(long left, CompareOperator op, long right) => op switch
    {
        CompareOperator.Equal => left == right,
        CompareOperator.NotEqual => left != right,
        CompareOperator.Less => left < right,
        CompareOperator.LessOrEqual => left <= right,
        CompareOperator.Greater => left > right,
        CompareOperator.GreaterOrEqual => left >= right,
        _ => false,
    };
}
