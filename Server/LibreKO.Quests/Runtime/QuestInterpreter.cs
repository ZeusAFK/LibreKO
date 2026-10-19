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

    private readonly QuestProgram program;
    private readonly IQuestHost host;
    private readonly int chosenReward;
    private readonly List<int> chain = [];
    private int statements;
    private int? pendingJump;
    private string? failure;
    private bool buildingGreeting;
    private int viewQuest = -1;
    private bool viewShown;

    public QuestInterpreter(QuestProgram program, IQuestHost host, int chosenReward = -1)
    {
        this.program = program;
        this.host = host;
        this.chosenReward = chosenReward;
    }

    public QuestExecutionResult Run(int eventId)
    {
        buildingGreeting = program.TryGetGreeting(out var greeting) && greeting == eventId;
        var current = eventId;
        var visited = new HashSet<int>();

        while (true)
        {
            if (!program.TryGetEvent(current, out var entry))
            {
                var handled = chain.Count > 0;
                if (!handled)
                    failure ??= $"event {current} is not handled by {program.FileName}";
                return new QuestExecutionResult(handled, statements, chain, failure);
            }

            if (!visited.Add(current))
            {
                failure = $"event {current} was entered twice in one interaction";
                return new QuestExecutionResult(true, statements, chain, failure);
            }

            chain.Add(current);
            if (chain.Count > MaxEventHops)
            {
                failure = $"more than {MaxEventHops} event hops in one interaction";
                return new QuestExecutionResult(true, statements, chain, failure);
            }

            if (program.TryGetGreeting(out var greetingId) && current == greetingId)
                buildingGreeting = true;
            pendingJump = null;
            Execute(entry.Body);

            if (failure is not null)
                return new QuestExecutionResult(true, statements, chain, failure);

            if (pendingJump is not { } next)
                return new QuestExecutionResult(true, statements, chain, null);

            current = next;
        }
    }

    private void Execute(IReadOnlyList<BoundStatement> body)
    {
        foreach (var statement in body)
        {
            if (pendingJump is not null || failure is not null)
                return;

            if (++statements > MaxStatements)
            {
                failure = $"more than {MaxStatements} statements in one interaction";
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
                    host.ApplyReward(selected ? transfers : []);
                    if (selected && !host.ActionFailed)
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
                    host.Say(say.Lines);
                    break;

                case BoundStatement.Goto jump:
                    pendingJump = jump.TargetEvent;
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
        if (host.QuestStatus(choice.QuestId) is not (1 or 3))
            return choice;
        var goals = program.Objectives.FirstOrDefault(o => o.QuestId == choice.QuestId);
        var rewards = program.RewardsFor(choice.QuestId, host.PlayerClassGroup, host.PlayerNation, host.PlayerRebirthLevel);
        var measurable = goals is { Groups.Count: > 0 } || rewards is not null;
        var ready = measurable && ObjectivesComplete(choice.QuestId)
                    && (rewards is null || CostsAvailable(rewards)) && !WaitsOnFulfilment(choice.QuestId);
        var label = DialogLine.FromText((ready ? ReadyTag : InProgressTag) + choice.Button.Label.Text);
        return choice with { Button = choice.Button with { Label = label } };
    }

    private bool ShowDialog(BoundStatement.Dialog dialog)
    {
        var greeting = buildingGreeting;
        if (buildingGreeting)
        {
            buildingGreeting = false;
            var menu = dialog.Choices.Count == 0 ? program.GreetingTopics : program.AutomaticTopics;
            dialog = dialog with
            {
                Choices = [.. menu.Select(TagWithProgress), .. dialog.Choices],
                Fallback = dialog.Fallback ?? program.GreetingFallback
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
            return failure is null;
        }
        if (greeting && !dialog.Header.HasText && offered.Count == 0)
            return true;

        if (offered.Count > Binding.QuestVocabulary.MaxDialogButtons)
        {
            failure = $"The dialog offers more than {Binding.QuestVocabulary.MaxDialogButtons} buttons.";
            return false;
        }

        var questId = viewQuest > 0 ? viewQuest : dialog.QuestId;
        if (program.Flows.Any(f => f.QuestId == questId))
        {
            viewShown = true;
            host.ShowQuestView(BuildView(questId, dialog.Header, offered, dialog.Page));
        }
        else
            host.ShowDialog(dialog.Style, dialog.QuestId, dialog.Header, offered);
        return true;
    }

    private bool WaitsOnFulfilment(int questId) =>
        host.QuestStatus(questId) == 1
        && (program.TextFor(questId, host.PlayerNation, host.PlayerClassGroup)?.FulfilElsewhere ?? false);

    private bool ObjectivesComplete(int questId)
    {
        var goals = program.Objectives.FirstOrDefault(o => o.QuestId == questId);
        if (goals is not { Groups.Count: > 0 })
            return true;
        var done = goals.Groups.Select((g, index) => host.KillCount(questId, index + 1) >= g.Count);
        return goals.Rule == ObjectiveRule.Any ? done.Any(v => v) : done.All(v => v);
    }

    private bool CostsAvailable(QuestRewards rewards) => rewards.Transfers
        .Where(a => a.Kind is QuestActionKind.TakeItem or QuestActionKind.TakeGold or QuestActionKind.TakeNationalPoints)
        .GroupBy(a => (a.Kind, Item: a.Arguments.GetInt("item")))
        .All(group => (long)(group.Key.Kind switch
        {
            QuestActionKind.TakeGold => host.PlayerCoins,
            QuestActionKind.TakeNationalPoints => host.PlayerLoyalty,
            _ => host.ItemCount(group.Key.Item)
        }) >= group.Sum(a => a.Arguments.Get("amount", a.Arguments.Get("count", 1))));

    private IReadOnlyList<BoundStatement.Action> ResolvePremium(IReadOnlyList<BoundStatement.Action> transfers)
    {
        if (!transfers.Any(a => a.Arguments.Has("premium")))
            return transfers;
        return [.. transfers.Select(a => !a.Arguments.Has("premium") ? a : a with
        {
            Arguments = new ArgumentSet(new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["amount"] = host.PremiumType > 0 ? a.Arguments.Get("premium") : a.Arguments.Get("amount")
            })
        })];
    }

    public QuestView BuildView(int questId, DialogLine? dialogue = null, IReadOnlyList<DialogButton>? topics = null,
        QuestPageKind page = QuestPageKind.Quest)
    {
        var flow = program.Flows.Single(f => f.QuestId == questId);
        var classGroup = host.PlayerClassGroup;
        var rewards = program.RewardsFor(questId, classGroup, host.PlayerNation, host.PlayerRebirthLevel)
            ?? throw new InvalidOperationException($"Quest {questId} has no rewards.");
        rewards = rewards with { Transfers = ResolvePremium(rewards.Transfers) };
        var text = program.TextFor(questId, host.PlayerNation, classGroup)
            ?? throw new InvalidOperationException($"Quest {questId} has no text.");
        var state = host.QuestStatus(questId) switch
        {
            1 when text.FulfilElsewhere => QuestViewState.InProgress,
            1 or 3 => ObjectivesComplete(questId) && CostsAvailable(rewards)
                ? QuestViewState.Claimable : QuestViewState.InProgress,
            2 => QuestViewState.Completed,
            0 or 4 when flow.Requires is null || Evaluate(flow.Requires) => QuestViewState.Available,
            _ => QuestViewState.Locked
        };
        var goals = program.Objectives.FirstOrDefault(o => o.QuestId == questId);
        return new QuestView(text, flow.BindingFor(host.PlayerNation, host.PlayerZone, classGroup)?.ZoneId ?? flow.ZoneId, state, goals,
            goals?.Groups.Select((_, index) => host.KillCount(questId, index + 1)).ToArray() ?? [],
            rewards, dialogue ?? DialogLine.FromText(text.Journal ?? string.Empty), topics ?? [], page, flow.AutoAccept);
    }

    private void ShowView(int questId, bool silent = false)
    {
        var view = BuildView(questId);
        if (silent)
        {
            host.UpdateQuestView(view);
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
        viewQuest = questId;
        viewShown = false;
        if (program.TryGetEntry(role, questId, out var id) && program.TryGetEvent(id, out var entry))
            Execute(entry.Body);
        if (!viewShown && pendingJump is null)
            host.ShowQuestView(view);
        viewQuest = -1;
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
        SwitchSelectorKind.PlayerClass => host.PlayerClassGroup,
        SwitchSelectorKind.PlayerNation => host.PlayerNation,
        SwitchSelectorKind.Event => chain.Count > 0 ? chain[^1] : 0,
        SwitchSelectorKind.Roll => host.RollDice(node.Max - 1),
        SwitchSelectorKind.Reward => chosenReward,
        _ => 0,
    };

    private void ExecuteSwitch(BoundStatement.Switch node)
    {
        if (SelectCase(node) is { } chosen)
            Execute(chosen);
        else if (node.Selector == SwitchSelectorKind.Reward)
            host.ApplyReward([]);
    }

    private bool MatchesClass(long label, int group) =>
        label switch
        {
            QuestVocabulary.ClassGroupWarrior or QuestVocabulary.ClassGroupRogue
                or QuestVocabulary.ClassGroupMage or QuestVocabulary.ClassGroupPriest
                or QuestVocabulary.ClassGroupKurian => label == group,
            _ => label == host.PlayerClassSubtype,
        };


    private BoundStatement.Action? Draw(BoundStatement.Action action)
    {
        if (!program.TryGetReward(action.Arguments.GetInt("reward"), out var reward))
            return null;

        var row = reward.Rows[reward.Rows.Count == 1 ? 0 : host.RollDice(reward.Rows.Count - 1)];
        var total = row.Sum(choice => choice.Weight);
        var roll = total <= 1 ? 0 : host.RollDice((int)total - 1);
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
        if (host.ActionFailed || host.QuestStatus(questId) is not (1 or 3) || WaitsOnFulfilment(questId))
            return;
        var rewards = program.RewardsFor(questId, host.PlayerClassGroup, host.PlayerNation, host.PlayerRebirthLevel);
        if (rewards is null)
        {
            failure = $"Quest {questId} has no declared Rewards.";
            return;
        }
        if (!ObjectivesComplete(questId))
            return;
        var transfers = ResolvePremium(rewards.Transfers);
        if (rewards.Options.Count > 0)
        {
            if (chosenReward < 0 || chosenReward >= rewards.Options.Count)
            {
                failure = $"Quest {questId} offers a choice of reward and none was chosen.";
                return;
            }
            transfers = [.. transfers, rewards.Options[chosenReward]];
        }
        host.ApplyReward(transfers);
        if (host.ActionFailed)
            return;
        host.SetQuestState(questId, 2);
        if (transfers.Count > 0) host.ShowReceipt(questId);
    }

    private void ExecuteAction(BoundStatement.Action action)
    {
        var args = action.Arguments;
        switch (action.Kind)
        {
            case QuestActionKind.RefuseReward:
                host.ApplyReward([]);
                break;

            case QuestActionKind.GiveRandomReward:
                if (Draw(action) is { } prize)
                    host.GiveItem(prize.Arguments.GetInt("item"), prize.Arguments.GetInt("count", 1), 0);
                break;

            case QuestActionKind.Todo:
            case QuestActionKind.DoNothing:
                break;

            case QuestActionKind.SetQuestState:
                host.SetQuestState(args.GetInt("quest"), args.GetInt("status"));
                break;

            case QuestActionKind.ClaimQuest:
                ClaimQuest(args.GetInt("quest"));
                break;

            case QuestActionKind.GiveItem:
                host.GiveItem(args.GetInt("item"), args.GetInt("count", 1), RentalHours(args));
                break;

            case QuestActionKind.GivePremium:
                host.GivePremium(args.GetInt("type"), args.GetInt("days"));
                break;

            case QuestActionKind.GiveClanPremium:
                host.GiveClanPremium(args.GetInt("days"));
                break;

            case QuestActionKind.GiveAchievement:
                host.GiveAchievement(args.GetInt("achievement"));
                break;

            case QuestActionKind.JoinTempleEvent:
                host.JoinTempleEvent();
                break;

            case QuestActionKind.JoinBlueSoccerTeam:
                host.JoinSoccerTeam(blue: true);
                break;

            case QuestActionKind.JoinRedSoccerTeam:
                host.JoinSoccerTeam(blue: false);
                break;

            case QuestActionKind.StartSoccerGame:
                host.StartSoccerGame();
                break;

            case QuestActionKind.ShowRankBoard:
                host.ShowRankBoard();
                break;

            case QuestActionKind.SetLevel:
                host.SetPlayerLevel(args.GetInt("level"));
                break;

            case QuestActionKind.SetDrakiRift:
                host.SetDrakiRift(args.GetInt("stage"), args.GetInt("substage"));
                break;

            case QuestActionKind.TakeItem:
                host.TakeItem(args.GetInt("item"), args.GetInt("count", 1));
                break;

            case QuestActionKind.GiveGold:
                host.GiveGold(args.GetInt("amount"));
                break;

            case QuestActionKind.TakeGold:
                host.TakeGold(args.GetInt("amount"));
                break;

            case QuestActionKind.GiveExperience:
                host.GiveExp(args.GetInt("amount"));
                break;

            case QuestActionKind.GiveNationalPoints:
                host.GiveLoyalty(args.GetInt("amount"));
                break;

            case QuestActionKind.TakeNationalPoints:
                host.TakeLoyalty(args.GetInt("amount"));
                break;

            case QuestActionKind.Exchange:
                host.RunExchange(args.GetInt("exchange"));
                break;

            case QuestActionKind.QuestExchange:
                host.RunQuestExchange(args.GetInt("exchange"), args.GetInt("award", chosenReward));
                break;

            case QuestActionKind.GiveCash:
                host.GiveCash(args.GetInt("amount"));
                break;

            case QuestActionKind.ChangeJob:
                host.ChangeJob(args.GetInt("class"), args.GetInt("mastered", 1) != 0);
                break;

            case QuestActionKind.ExchangeTimes:
                host.RunCountExchange(args.GetInt("exchange"), args.GetInt("count", 1));
                break;

            case QuestActionKind.MiningExchange:
                host.RunMiningExchange(args.GetInt("ore"));
                break;

            case QuestActionKind.GenieExchange:
                host.RunGenieExchange(args.GetInt("item"), args.GetInt("hours"));
                break;

            case QuestActionKind.ExchangeRandom:
            {
                var first = args.GetInt("first");
                var last = args.GetInt("last");
                var span = Math.Max(0, last - first);
                host.RunExchange(first + host.RollDice(span));
                break;
            }

            case QuestActionKind.ShowMap:
                if (program.TryGetLocation(args.GetInt("map"), out var location))
                    host.ShowLocation(location, args.GetInt("quest", viewQuest));
                break;

            case QuestActionKind.Warp:
                host.TeleportToZone(args.GetInt("zone"), args.GetInt("x"), args.GetInt("z"));
                break;

            case QuestActionKind.WarpClan:
                host.TeleportClanToZone(args.GetInt("zone"), args.GetInt("x"), args.GetInt("z"));
                break;

            case QuestActionKind.WarpParty:
                host.TeleportPartyToZone(args.GetInt("zone"), args.GetInt("x"), args.GetInt("z"));
                break;

            case QuestActionKind.EnterInstance:
                host.EnterInstance(args.GetInt("zone"), args.GetInt("set"), args.GetInt("x"), args.GetInt("z"));
                break;

            case QuestActionKind.EnterClanInstance:
                host.EnterClanInstance(args.GetInt("zone"), args.GetInt("set"), args.GetInt("x"), args.GetInt("z"));
                break;

            case QuestActionKind.Cast:
                host.CastSkill(args.GetInt("skill"));
                break;

            case QuestActionKind.Effect:
                host.PlayEffect(args.GetInt("effect"));
                break;

            case QuestActionKind.DespawnNpc:
                host.DespawnNpc();
                break;

            case QuestActionKind.Summon:
                host.SummonNpc(args.GetInt("npc"), args.GetInt("count", 1), args.GetInt("x", 0), args.GetInt("z", 0));
                break;

            case QuestActionKind.NpcEffect:
                host.PlayNpcEffect(args.GetInt("effect"));
                break;

            case QuestActionKind.PromoteClan:
                host.PromoteClan(action.Arguments.GetInt("rank"));
                break;

            case QuestActionKind.TakeClanPoints:
                host.TakeClanPoints(action.Arguments.GetInt("amount"));
                break;

            case QuestActionKind.Promote:
                host.PromotePlayer();
                break;

            case QuestActionKind.PromoteNovice:
                host.PromoteToNovice();
                break;

            case QuestActionKind.ResetStats:
                host.ResetStatPoints();
                break;

            case QuestActionKind.ResetSkills:
                host.ResetSkillPoints();
                break;

            case QuestActionKind.OpenRenamePanel:
                host.OpenRenamePanel();
                break;

            case QuestActionKind.OpenJobChangePanel:
                host.OpenJobChangePanel();
                break;

            case QuestActionKind.OpenClanRenamePanel:
                host.OpenClanRenamePanel();
                break;

            case QuestActionKind.OpenRebirthPanel:
                host.OpenRebirthPanel();
                break;

            case QuestActionKind.OpenRebirthResetPanel:
                host.OpenRebirthResetPanel();
                break;

            case QuestActionKind.OpenRepurchaseWindow:
                host.OpenRepurchaseWindow();
                break;

            case QuestActionKind.OpenFamiliarPanel:
                host.OpenFamiliarPanel();
                break;

            case QuestActionKind.OpenFamiliarShop:
                host.OpenFamiliarShop();
                break;

            case QuestActionKind.OpenGenderChangePanel:
                host.OpenGenderChangePanel();
                break;

            case QuestActionKind.OpenFortunePanel:
                host.OpenFortunePanel();
                break;
            case QuestActionKind.OpenSpecialAuctionPanel:
                host.OpenSpecialAuctionPanel();
                break;
            case QuestActionKind.OpenYutGame:
                host.OpenYutGame();
                break;
            case QuestActionKind.OpenDrakiRankBoard:
                host.OpenDrakiRankBoard();
                break;
            case QuestActionKind.OpenItemCombinePanel:
                host.OpenItemCombinePanel();
                break;
            case QuestActionKind.OpenCombineRecipeBook:
                host.OpenCombineRecipeBook();
                break;
            case QuestActionKind.OpenDisassembleWindow:
                host.OpenDisassembleWindow();
                break;

            case QuestActionKind.OpenNationTransferPanel:
                host.OpenNationTransferPanel();
                break;

            case QuestActionKind.OpenStatSkillPanel:
                host.OpenStatSkillPanel();
                break;

            default:
                host.Unsupported(action.Kind.ToString());
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
                Compare(host.ItemCount(args.GetInt("item")), node.Operator, args.GetInt("count")),
            QuestConditionKind.HasItem => host.ItemCount(args.GetInt("item")) > 0,
            QuestConditionKind.RoomFor =>
                Compare(host.PlayerFreeSlots, node.Operator, args.GetInt("count", 1)),
            QuestConditionKind.CanReceiveItem =>
                host.HasRoomForItem(args.GetInt("item"), args.GetInt("count")),
            QuestConditionKind.CanReceiveStacks => host.CanReceiveStacks(args.GetInt("count")),
            QuestConditionKind.PlayerWeight => Compare(host.PlayerWeight, node.Operator, args.GetInt("value")),
            QuestConditionKind.PlayerExperience => Compare(host.PlayerExperience, node.Operator, args.GetInt("value")),
            QuestConditionKind.QuestStatus => host.QuestStatus(args.GetInt("quest")) == args.GetInt("status"),
            QuestConditionKind.QuestStatusIn =>
                host.QuestStatus(args.GetInt("quest")) is >= 0 and < 32 and var status
                && (args.GetInt("states") & (1 << status)) != 0,
            QuestConditionKind.PlayerClass => MatchesClass(args.GetInt("class"), host.PlayerClassGroup),
            QuestConditionKind.PlayerClassSubtype => Compare(host.PlayerClassSubtype, node.Operator, args.GetInt("value")),
            QuestConditionKind.PlayerNation => host.PlayerNation == args.GetInt("nation"),
            QuestConditionKind.PlayerLevel => Compare(host.PlayerLevel, node.Operator, args.GetInt("value")),
            QuestConditionKind.RebirthLevel => Compare(host.PlayerRebirthLevel, node.Operator, args.GetInt("value")),
            QuestConditionKind.PlayerGold => Compare(host.PlayerCoins, node.Operator, args.GetInt("amount")),
            QuestConditionKind.PlayerNationalPoints => Compare(host.PlayerLoyalty, node.Operator, args.GetInt("amount")),
            QuestConditionKind.KillCount =>
                Compare(host.KillCount(args.GetInt("quest"), args.GetInt("group")), node.Operator, args.GetInt("count")),
            QuestConditionKind.HasKillQuest => host.HasActiveKillQuest(),
            QuestConditionKind.LeadsClan => host.IsClanLeader,
            QuestConditionKind.InClan => host.IsInClan,
            QuestConditionKind.ClanRank =>
                Compare(host.ClanRank, node.Operator, args.GetInt("rank")),
            QuestConditionKind.ClanGrade =>
                Compare(host.ClanGrade, node.Operator, args.GetInt("value")),
            QuestConditionKind.ClanPoints =>
                Compare(host.ClanPoints, node.Operator, args.GetInt("amount")),
            QuestConditionKind.SkillPoints =>
                Compare(host.SkillPoints(args.GetInt("tree")), node.Operator, args.GetInt("amount")),
            QuestConditionKind.InParty => host.IsInParty,
            QuestConditionKind.LeadsParty => host.IsPartyLeader,
            QuestConditionKind.IsKing => host.IsKing,
            QuestConditionKind.KrowazOpen => host.KrowazOpen,
            QuestConditionKind.DailyAvailable => host.DailyRewardAvailable(args.GetInt("slot")),
            QuestConditionKind.Weekday => host.Weekday == args.GetInt("day"),
            QuestConditionKind.HasEffect => host.HasEffect(args.GetInt("skill")),
            QuestConditionKind.HasPremium => Compare(host.PremiumType, node.Operator, args.GetInt("value")),
            QuestConditionKind.LastStepFailed => host.ActionFailed,
            QuestConditionKind.PlayerZone => host.PlayerZone == args.GetInt("zone"),
            QuestConditionKind.MonumentNation => host.MonumentNation == args.GetInt("nation"),
            QuestConditionKind.Chance => host.RollChance(args.GetInt("percent")),
            QuestConditionKind.RollUnder => Compare(
                host.RollDice(args.GetInt("max") - 1), node.Operator, args.GetInt("value")),
            QuestConditionKind.ReachedLevel =>
                host.ReachedLevel(args.GetInt("level"), args.GetInt("percent")),
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
