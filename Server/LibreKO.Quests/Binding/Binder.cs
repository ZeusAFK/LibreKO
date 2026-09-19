using LibreKO.Quests.Catalog;
using LibreKO.Quests.Runtime;
using LibreKO.Quests.Syntax;
using LibreKO.Quests.Text;

namespace LibreKO.Quests.Binding;

public sealed class Binder
{
    private readonly QuestFileSyntax _file;
    private readonly IQuestCatalog _catalog;
    private readonly DiagnosticBag _diagnostics;
    private readonly Dictionary<string, Symbol> _events = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _eventIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Named> _named = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<QuestLocation> _locations = [];
    private readonly List<RewardDefinition> _rewards = [];
    private readonly List<NamedReference> _namedReferences = [];
    private readonly Dictionary<string, string> _texts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _usedNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _handledEvents = [];
    private readonly HashSet<int> _referencedEvents = [];
    private readonly HashSet<int> _sharedEvents = [];
    private readonly List<BorrowedEvent> _borrowed = [];
    private readonly List<ResolvedId> _resolvedIds = [];
    private readonly List<TranslatableText> _translatable = [];
    private int _nextLocalEventId = QuestProgram.LocalEventBase;
    private readonly HashSet<int> _entryEvents = [];
    private readonly List<QuestEventEntry> _lifted = [];
    private readonly List<QuestObjectives> _objectives = [];
    private readonly List<QuestText> _questTexts = [];
    private int _npcId;
    private int _zoneId;
    private readonly List<int> _npcIds = [];
    private int _defaultQuest = -1;
    private int _scopeQuest = -1;
    private int _scopeQuestAt = -1;
    private BoundCondition? _replyGuard;
    private readonly List<(long Quest, TextSpan Span)> _questScopes = [];
    private readonly HashSet<int> _consumedScopes = [];

    public IReadOnlyList<ResolvedId> ResolvedIds => _resolvedIds;

    public IReadOnlyList<TranslatableText> Translatable => _translatable;

    public IReadOnlyDictionary<int, string> QuestAliases =>
        _named.Values
            .Where(n => n.Kind == SlotKind.QuestId)
            .GroupBy(n => (int)n.Value)
            .ToDictionary(g => g.Key, g => g.First().Name);

    public IReadOnlyList<BorrowedEvent> Borrowed => _borrowed;

    public IReadOnlyList<QuestLocation> Locations => _locations;
    public IReadOnlyList<RewardDefinition> Rewards => _rewards;

    public IReadOnlyList<NamedReference> NamedReferences => _namedReferences;

    private sealed record Symbol(SlotKind Kind, string Name, long Value, TextSpan Span);

    private sealed record Named(SlotKind Kind, string Name, long Value, TextSpan Span, string? File);

    public Binder(QuestFileSyntax file, IQuestCatalog catalog, DiagnosticBag diagnostics)
    {
        _file = file;
        _catalog = catalog;
        _diagnostics = diagnostics;
    }

    public QuestProgram Bind()
    {
        ReadDirectives();
        if (_file.HasBinding && _file.Objectives.Count == 1)
            _defaultQuest = (int)_file.Objectives[0].QuestId;
        if (_file.HasBinding && _file.Objectives.Count > 1)
            _diagnostics.Error(DiagnosticId.DuplicateObjectives, _file.Objectives[1].Span,
                "A bound quest file defines one Quest.");
        DeclareRewards(_file.Rewards, null);
        DeclareNames();
        DeclareLocalEvents();
        DeclareHandlers();

        _scopeQuest = _defaultQuest;
        IReadOnlyList<QuestRewards> questRewards = [];
        var collectedByClass = BindCollectedByClass();
        var collected = collectedByClass.Where(c => c.ClassGroup == 0).Select(c => c.Take).ToList();

        IReadOnlyList<BoundStatement.Action> CollectedFor(int group) =>
            [.. collectedByClass.Where(c => c.ClassGroup == 0 || c.ClassGroup == group).Select(c => c.Take)];
        if (_file.QuestRewards is { Count: > 0 } rewardBlocks)
        {
            var built = new List<QuestRewards>();
            foreach (var block in rewardBlocks)
            {
                var choices = block.Body.OfType<StatementSyntax.Choice>().ToList();
                var transfers = BindBlock([.. block.Body.Where(s => s is not StatementSyntax.Choice)]);
                if (!_file.HasBinding || _defaultQuest <= 0
                    || transfers.Any(s => s is not BoundStatement.Action a || !IsTransfer(a)
                        || a.Kind == QuestActionKind.GiveRandomReward))
                {
                    _diagnostics.Error(DiagnosticId.UnknownDirective, _file.Span,
                        "Rewards requires a bound quest and explicit Take/Give lines, or Rewards none.");
                    continue;
                }
                var options = BindRewardChoice(choices);
                var group = ResolveClassGroup(block.ClassGroup);
                built.Add(new QuestRewards(_defaultQuest,
                    [.. CollectedFor(group), .. transfers.Cast<BoundStatement.Action>()], group)
                    { Options = options });
            }
            if (built.Count > 0)
                questRewards = built;
        }
        else if (collected.Count > 0)
        {
            questRewards = [new QuestRewards(_defaultQuest, collected)];
        }
        if (_file.AutoAccept)
        {
            if (!_file.HasBinding || _defaultQuest <= 0 || _zoneId <= 0 || _npcId > 0)
                _diagnostics.Error(DiagnosticId.UnknownDirective, _file.Span, "Auto accept requires one quest bound only to a zone.");
            if (questRewards.Count == 0) questRewards = [new QuestRewards(_defaultQuest, [])];
        }
        if (_file.AutoComplete)
        {
            if (!_file.AutoAccept)
                _diagnostics.Error(DiagnosticId.UnknownDirective, _file.Span, "Auto complete requires Auto accept.");
            if (questRewards.Any(r => r.Options.Count > 0))
                _diagnostics.Error(DiagnosticId.UnknownDirective, _file.Span, "Auto complete cannot pay a 'Choose one' reward; the player picks at an NPC.");
        }
        var automatic = questRewards.Count > 0 && !_file.Handlers.SelectMany(h => h.Events).Any(e =>
            new[] { "topics", "greeting", "accept", "fulfil" }.Contains(e.Name, StringComparer.OrdinalIgnoreCase));
        if (_file.Handlers.SelectMany(h => h.Events).Any(e => QuestProgram.StateEvents.Contains(e.Name)) && !automatic)
            _diagnostics.Error(DiagnosticId.UnknownDirective, _file.Span,
                "State dialogue handlers require Rewards and the automatic lifecycle; omit topics, greeting, accept and fulfil handlers.");
        if (_file.AutoAccept && !automatic)
            _diagnostics.Error(DiagnosticId.UnknownDirective, _file.Span, "Auto accept cannot use manual lifecycle handlers.");
        var eligibility = _file.Requires is null ? null : BindCondition(_file.Requires);
        var requirement = eligibility;
        if (requirement is not null)
        {
            if (HasTransientPredicate(requirement))
                _diagnostics.Error(DiagnosticId.UnknownDirective, _file.Requires!.Span,
                    "Requires cannot depend on a random roll or the last action's result.");
            if (!_file.HasBinding || _defaultQuest <= 0)
                _diagnostics.Error(DiagnosticId.UnknownDirective, _file.Requires!.Span,
                    "Requires belongs in a bound file with one Quest.");
            requirement = new BoundCondition.Or(StatusIn(1, 2, 3), requirement);
            requirement = Both(requirement, BindingRequirement());
        }

        var events = new Dictionary<int, QuestEventEntry>();
        foreach (var handler in _file.Handlers)
        {
            _scopeQuest = _defaultQuest;
            _scopeQuestAt = -1;
            if (handler.QuestId is { } quest)
            {
                _scopeQuest = (int)quest;
                _scopeQuestAt = handler.QuestSpan.Start;
                if (!handler.Events.Any(e => QuestProgram.QuestEntries.Contains(e.Name)))
                    _questScopes.Add((quest, handler.QuestSpan));
                CheckCatalog(SlotKind.QuestId, quest, handler.QuestSpan);
            }

            var syntaxBody = handler.Body;
            if (handler.Events.Any(e => e.Name.Equals(QuestProgram.TopicsEvent, StringComparison.OrdinalIgnoreCase)))
            {
                var signature = QuestVocabulary.Actions.First(a => a.Text == "Say {text:TalkTextId}");
                var match = new PhraseMatch(signature.Pattern,
                    new Dictionary<string, Token> { ["text"] = new(TokenKind.String, "", handler.HeaderSpan) }, 2, 1);
                syntaxBody = [new StatementSyntax.Action(handler.HeaderSpan, signature, match), .. handler.Body];
            }
            var displayState = handler.Events.Select(e => e.Name.ToLowerInvariant()).FirstOrDefault(QuestProgram.StateEvents.Contains);
            _replyGuard = automatic && displayState is not null ? new BoundCondition.ViewState(_defaultQuest,
                displayState switch { "available" or "offer" => QuestViewState.Available, "in_progress" or "started" => QuestViewState.InProgress,
                    "claimable" or QuestProgram.ReadyEvent => QuestViewState.Claimable,
                    _ => QuestViewState.Completed }) : null;
            var body = BindBlock(syntaxBody);
            _replyGuard = null;
            if (automatic && displayState is not null && !PresentationOnly(body))
                _diagnostics.Error(DiagnosticId.UnknownAction, handler.HeaderSpan,
                    "State handlers display dialogue. Put optional actions inside Topic ... do and the payout in Rewards.");
            if (automatic && displayState is "offer" or "claimable" && !ReachesQuestPage(body))
                _diagnostics.Error(DiagnosticId.UnknownAction, handler.HeaderSpan,
                    $"'On {displayState}' is where the player accepts or claims, so it needs the quest page.",
                    "Write 'Show quest \"...\"', or offer a Topic whose page shows it.");
            if (handler.Events.Any(e => e.Name.Equals(QuestProgram.TopicsEvent, StringComparison.OrdinalIgnoreCase))
                && body is not [BoundStatement.Dialog])
                _diagnostics.Error(DiagnosticId.UnknownAction, handler.HeaderSpan,
                    "On topics contains choices and their conditions; put actions inside 'Topic ... do'.");
            foreach (var reference in handler.Events)
            {
                var name = EntryName(handler, reference);
                if (!_eventIds.TryGetValue(name, out var id))
                    continue;
                if (reference.Name.Equals(QuestProgram.GreetingEvent, StringComparison.OrdinalIgnoreCase) && events.ContainsKey(id))
                    continue;
                var guarded = body;
                if (requirement is not null && reference.Name != QuestProgram.AbandonEvent)
                    guarded = reference.Name == QuestProgram.TopicsEvent && body is [BoundStatement.Dialog dialog]
                        ? [dialog with
                        {
                            Choices = [.. dialog.Choices.Select(c => c with { When = Both(requirement, c.When) })],
                            Fallback = dialog.Fallback is null ? null : Guard(dialog.Fallback, requirement, handler.HeaderSpan)
                        }]
                        : Guard(body, requirement, handler.HeaderSpan);
                events[id] = new QuestEventEntry(id, name, handler.HeaderSpan, guarded);
            }
        }

        foreach (var entry in _lifted)
            events[entry.Id] = entry with { Body = Guard(entry.Body, requirement, entry.Span) };

        if (questRewards.Count > 0)
        {
            var entryName = QuestProgram.EntryName(QuestProgram.FulfilEvent, _defaultQuest);
            if (!_eventIds.ContainsKey(entryName))
            {
                var id = _nextLocalEventId++;
                _eventIds[entryName] = id;
                _entryEvents.Add(id);
                var claim = new BoundStatement.Action(_file.Span, QuestActionKind.ClaimQuest,
                    new ArgumentSet(new Dictionary<string, long> { ["quest"] = _defaultQuest }));
                events[id] = new QuestEventEntry(id, entryName, _file.Span, Guard([claim], requirement, _file.Span));
            }
        }

        BindObjectives();
        if (automatic)
            BindAutomaticFlow(events, eligibility);

        foreach (var entry in events.Values)
        {
            CheckSpaceMatchesGifts(entry);
            LintBody(entry.Body);
        }

        ReportUnreachableLocalEvents(events);
        ReportUnusedNames();
        ReportUnusedQuestScopes();

        var carriesQuestData = _questTexts.Count > 0 || _objectives.Count > 0;
        if (_npcId != 0 && !(carriesQuestData && events.Count == 0) && !_eventIds.Keys.Any(name =>
                QuestProgram.ScriptEntries.Contains(name)
                || QuestProgram.QuestEntries.Contains(name.Split('_')[0])))
            _diagnostics.Warning(DiagnosticId.NoGreeting, new TextSpan(0, 0),
                "Nothing reaches this file: it has no greeting, topics or quest-panel entry.",
                "Add 'On greeting' with the menu of topics this NPC offers, or an 'On accept'.");

        return new QuestProgram(
            _file.Source.FileName,
            _npcId,
            null,
            _file.Source,
            events,
            _eventIds,
            _locations,
            _zoneId,
            _objectives,
            _npcIds,
            _questTexts,
            _defaultQuest,
            _file.HasBinding,
            _rewards) { Borrowed = _borrowed, QuestRewards = questRewards, Bindings = BindBindings(),
                Flows = automatic ? [new QuestFlow(_defaultQuest, eligibility, _zoneId, _file.AutoAccept, _file.AutoComplete) { Bindings = BindBindings() }] : [] };
    }

    private IReadOnlyList<BoundStatement.Action> BindRewardChoice(IReadOnlyList<StatementSyntax.Choice> choices)
    {
        if (choices.Count == 0)
            return [];
        if (choices.Count > 1)
            _diagnostics.Error(DiagnosticId.DuplicateDirective, choices[1].Span,
                "Rewards holds one 'Choose one'; list every option under it.");
        var options = BindBlock(choices[0].Body);
        if (options.Count < 2 || options.Any(s => s is not BoundStatement.Action a
                || a.Kind is not (QuestActionKind.GiveItem or QuestActionKind.GiveGold
                    or QuestActionKind.GiveExperience or QuestActionKind.GiveNationalPoints)))
        {
            _diagnostics.Error(DiagnosticId.UnknownAction, choices[0].Span,
                "'Choose one' lists two or more Give lines, one per option the player may pick.",
                "Costs, promotions and random draws belong outside the choice.");
            return [];
        }
        return [.. options.Cast<BoundStatement.Action>()];
    }

    private int ResolveClassGroup(Token? token)
    {
        if (token is not { } named)
            return 0;
        if (QuestVocabulary.ClassGroups.TryGetValue(named.Text, out var group))
            return group;
        _diagnostics.Error(DiagnosticId.UnknownDirective, named.Span, $"\"{named.Text}\" is not a class.");
        return 0;
    }

    private IReadOnlyList<QuestBinding> BindBindings()
    {
        var bound = new List<QuestBinding>();
        foreach (var binding in _file.Bindings ?? [])
        {
            var nation = 0;
            if (binding.Nation is { } named && !QuestVocabulary.Nations.TryGetValue(named.Text, out nation))
                _diagnostics.Error(DiagnosticId.UnknownDirective, named.Span,
                    $"\"{named.Text}\" is not a nation.");
            var group = ResolveClassGroup(binding.ClassGroup);
            var zone = binding.Zone is { } z ? (int)z.Value : 0;
            foreach (var npc in binding.Npcs)
                bound.Add(new QuestBinding((int)npc.Value, zone, nation, group));
            if (binding.Npcs.Count == 0 && zone > 0)
                bound.Add(new QuestBinding(0, zone, nation, group));
        }
        return bound;
    }

    private BoundCondition? BindingRequirement()
    {
        BoundCondition? result = null;
        foreach (var binding in BindBindings())
        {
            BoundCondition? current = null;
            if (binding.ZoneId > 0)
                current = new BoundCondition.Predicate(QuestConditionKind.PlayerZone, CompareOperator.Equal,
                    new ArgumentSet(new Dictionary<string, long> { ["zone"] = binding.ZoneId }), _file.Span);
            if (binding.Nation > 0)
                current = Both(current, new BoundCondition.Predicate(QuestConditionKind.PlayerNation, CompareOperator.Equal,
                    new ArgumentSet(new Dictionary<string, long> { ["nation"] = binding.Nation }), _file.Span));
            if (binding.ClassGroup > 0)
                current = Both(current, new BoundCondition.Predicate(QuestConditionKind.PlayerClass, CompareOperator.Equal,
                    new ArgumentSet(new Dictionary<string, long> { ["class"] = binding.ClassGroup }), _file.Span));
            if (current is null) return null;
            result = result is null ? current : new BoundCondition.Or(result, current);
        }
        return result;
    }

    private static bool ReachesQuestPage(IReadOnlyList<BoundStatement> body) => body.Any(s => s switch
    {
        BoundStatement.Dialog d => d.Page == QuestPageKind.Quest || d.Choices.Count > 0
            || (d.Fallback is not null && ReachesQuestPage(d.Fallback)),
        BoundStatement.If branch => branch.Arms.Any(a => ReachesQuestPage(a.Body))
            || (branch.ElseBody is not null && ReachesQuestPage(branch.ElseBody)),
        BoundStatement.Switch branch => branch.Cases.Any(c => ReachesQuestPage(c.Body))
            || (branch.DefaultBody is not null && ReachesQuestPage(branch.DefaultBody)),
        _ => false
    });

    private static bool PresentationOnly(IReadOnlyList<BoundStatement> body) => body.All(s => s switch
    {
        BoundStatement.Dialog d => d.Fallback is null || PresentationOnly(d.Fallback),
        BoundStatement.If branch => branch.Arms.All(a => PresentationOnly(a.Body))
            && (branch.ElseBody is null || PresentationOnly(branch.ElseBody)),
        BoundStatement.Switch branch => branch.Cases.All(c => PresentationOnly(c.Body))
            && (branch.DefaultBody is null || PresentationOnly(branch.DefaultBody)),
        _ => false
    });

    private BoundCondition StatusIn(params int[] states) => new BoundCondition.Predicate(
        QuestConditionKind.QuestStatusIn, CompareOperator.Equal,
        new ArgumentSet(new Dictionary<string, long>
        { ["quest"] = _defaultQuest, ["states"] = states.Aggregate(0, (mask, state) => mask | (1 << state)) }), _file.Span);

    private void BindAutomaticFlow(Dictionary<int, QuestEventEntry> events, BoundCondition? eligibility)
    {
        var span = _file.Span;
        var view = new BoundStatement.View(span, _defaultQuest);
        int Add(string role, IReadOnlyList<BoundStatement> body)
        {
            var name = QuestProgram.EntryName(role, _defaultQuest);
            if (!_eventIds.TryGetValue(name, out var id))
                _eventIds[name] = id = _nextLocalEventId++;
            _entryEvents.Add(id);
            events[id] = new QuestEventEntry(id, name, span, body);
            return id;
        }
        var viewId = Add(QuestProgram.ViewEvent, [view]);
        var sync = new BoundStatement.View(span, _defaultQuest, Silent: true);
        var start = new BoundStatement.Action(span, QuestActionKind.SetQuestState,
            new ArgumentSet(new Dictionary<string, long> { ["quest"] = _defaultQuest, ["status"] = 1 }));
        var zone = BindingRequirement();
        Add(QuestProgram.AcceptEvent,
            [.. Guard([start, .. BindGrantedItems()], Both(Both(StatusIn(0, 4), eligibility), zone), span), sync]);
        var claim = new BoundStatement.Action(span, QuestActionKind.ClaimQuest,
            new ArgumentSet(new Dictionary<string, long> { ["quest"] = _defaultQuest }));
        Add(QuestProgram.FulfilEvent, [.. Guard([claim], zone, span), sync]);
        if (!_questTexts.Any(t => t.QuestId == _defaultQuest))
            _questTexts.Add(new QuestText(_defaultQuest, $"Quest {_defaultQuest}", null));
        var title = _questTexts.First(t => t.QuestId == _defaultQuest).Title ?? $"Quest {_defaultQuest}";
        var visible = new BoundCondition.Or(StatusIn(1, 3), Both(StatusIn(0, 4), eligibility)!);
        Add(QuestProgram.TopicsEvent, [new BoundStatement.Dialog(span, DialogStyle.Talk, -1, DialogLine.None,
            [new DialogChoice(Both(visible, zone), new DialogButton(DialogLine.FromText(title), viewId))])]);
    }

    private void ReadDirectives()
    {
        DirectiveSyntax? npc = null;
        DirectiveSyntax? zone = null;
        foreach (var directive in _file.Directives)
        {
            var taken = directive.Kind == SlotKind.ZoneId ? zone : npc;
            if (taken is not null)
            {
                _diagnostics.Error(DiagnosticId.DuplicateDirective, directive.Span,
                    directive.Kind == SlotKind.ZoneId
                        ? "This file already names its zone."
                        : "This file already names its NPC.");
                continue;
            }

            if (directive.Kind == SlotKind.ZoneId)
            {
                zone = directive;
            }
            else
            {
                npc = directive;
                _npcIds.Add((int)directive.Value);
                foreach (var (value, span) in directive.Extra ?? [])
                {
                    _npcIds.Add((int)value);
                    CheckCatalog(SlotKind.NpcId, value, span);
                }
            }

            CheckCatalog(directive.Kind, directive.Value, directive.ValueSpan);
        }

        if (zone is not null)
        {
            _zoneId = (int)zone.Value;
            if (npc is null && !_file.HasBinding)
                _diagnostics.Warning(DiagnosticId.MissingNpc, zone.Span,
                    "A zone only narrows an NPC, and this file names no NPC.");
        }

        if (npc is not null)
            _npcId = (int)npc.Value;
        else if (_file.Handlers.Count > 0 && !_file.HasBinding)
            _diagnostics.Warning(DiagnosticId.MissingNpc, new TextSpan(0, 0),
                "This file does not say which NPC it belongs to.",
                "Add 'Npc 16079' at the top. Nothing is derived from the file name.");
    }


    private void DeclareRewards(IReadOnlyList<RewardDefinitionSyntax> definitions, string? file)
    {
        foreach (var definition in definitions)
        {
            var name = definition.Name.Text;
            if (_named.TryGetValue(name, out var existing) && existing.File is null && file is not null)
                continue;
            if (_named.ContainsKey(name) && file is null)
            {
                _diagnostics.Error(DiagnosticId.DuplicateConstant, definition.Name.Span,
                    $"\"{name}\" already names something in this file.");
                continue;
            }

            var rows = new List<IReadOnlyList<RewardChoice>>();
            var broken = false;
            foreach (var row in definition.Rows)
            {
                var weights = row.Weights ?? definition.Weights;
                if (weights is null)
                {
                    _diagnostics.Error(DiagnosticId.BadArgumentCount, row.Span,
                        $"\"{name}\" needs a 'Weights' line before its rows.");
                    broken = true;
                    continue;
                }
                if (weights.Count != row.Items.Count)
                {
                    _diagnostics.Error(DiagnosticId.BadArgumentCount, row.Span,
                        $"This row lists {row.Items.Count} item(s) but {weights.Count} weight(s).");
                    broken = true;
                    continue;
                }

                var choices = new List<RewardChoice>();
                var total = 0L;
                for (var index = 0; index < row.Items.Count; index++)
                {
                    var token = row.Items[index];
                    var item = token.Kind == TokenKind.Number
                        ? token.Value
                        : ResolveNamed(SlotKind.ItemId, token, 0);
                    var weight = weights[index].Value;
                    if (weight <= 0)
                    {
                        _diagnostics.Error(DiagnosticId.BadArgumentCount, weights[index].Span,
                            weight == 0
                                ? "A weight of zero could never be drawn; remove the item or give it a weight."
                                : "A weight is how often this item comes up, so it cannot be negative.");
                        broken = true;
                        continue;
                    }
                    total += weight;
                    if (total > int.MaxValue)
                    {
                        _diagnostics.Error(DiagnosticId.NumberOutOfRange, weights[index].Span,
                            $"The weights in this row add up past {int.MaxValue}.");
                        broken = true;
                        break;
                    }
                    if (file is null && token.Kind == TokenKind.Number)
                        CheckCatalog(SlotKind.ItemId, item, token.Span);
                    choices.Add(new RewardChoice((int)item,
                        (int)(row.Counts is null ? 1 : row.Counts[index]), weight));
                }

                if (choices.Count > 0)
                    rows.Add(choices);
            }

            if (rows.Count == 0)
            {
                if (!broken)
                    _diagnostics.Error(DiagnosticId.EmptyBlock, definition.Span,
                        $"\"{name}\" has nothing it can give.");
                continue;
            }

            _named[name] = new Named(SlotKind.RewardId, name, _rewards.Count, definition.Name.Span, file);
            _rewards.Add(new RewardDefinition(name, rows));
        }
    }

    private void DeclareNames()
    {
        foreach (var declaration in _file.Declarations)
        {
            if (declaration.Kind != SlotKind.EventRef)
            {
                DeclareNamed(declaration, null);
                continue;
            }

            if (_events.ContainsKey(declaration.Name))
            {
                _diagnostics.Error(DiagnosticId.DuplicateConstant, declaration.NameSpan,
                    $"\"{declaration.Name}\" is already an event in this file.");
                continue;
            }

            if (declaration.SharedFrom is not null)
            {
                DeclareSharedEvent(declaration);
                continue;
            }

            var id = (int)(declaration.Value ?? 0);
            var clash = _eventIds.FirstOrDefault(pair => pair.Value == id);
            if (clash.Key is not null)
            {
                _diagnostics.Error(DiagnosticId.DuplicateEvent, declaration.ValueSpan,
                    $"Event {id} is already named \"{clash.Key}\".");
                continue;
            }

            _eventIds[declaration.Name] = id;
            _events[declaration.Name] = new Symbol(SlotKind.EventRef, declaration.Name, id, declaration.NameSpan);
            _namedReferences.Add(new NamedReference(
                declaration.ValueSpan, SlotKind.EventRef, declaration.Name,
                _catalog.EventTriggerSummary(_npcId, id)));
        }
    }

    private void DeclareSharedEvent(DeclarationSyntax declaration)
    {
        if (!_file.HasBinding)
        {
            _diagnostics.Error(DiagnosticId.UnknownDirective, declaration.Span,
                "An event borrowed from another file is found through this file's NPC, "
                + "so this file has to say which NPC it binds to.");
            return;
        }

        var id = _nextLocalEventId++;
        _eventIds[declaration.Name] = id;
        _events[declaration.Name] = new Symbol(SlotKind.EventRef, declaration.Name, id, declaration.NameSpan);
        _sharedEvents.Add(id);
        _borrowed.Add(new BorrowedEvent(declaration.Name, declaration.SharedFrom!, declaration.ValueSpan));
    }

    private void DeclareLocalEvents()
    {
        foreach (var handler in _file.Handlers)
        {
            foreach (var reference in handler.Events)
            {
                var name = EntryName(handler, reference);
                if (QuestProgram.QuestEntries.Contains(reference.Name) && handler.QuestId is null && _defaultQuest < 0)
                    _diagnostics.Error(DiagnosticId.EntryWithoutQuest, reference.Span,
                        $"\"{reference.Name}\" answers the quest panel, so it needs to say which quest.",
                        $"Write 'On {reference.Name} for quest N'.");

                if (_eventIds.ContainsKey(name))
                    continue;
                var id = _nextLocalEventId++;
                _eventIds[name] = id;
                _events[name] = new Symbol(SlotKind.EventRef, name, id, reference.Span);
                _usedNames.Add(name);
            }
        }
    }

    private string EntryName(EventHandlerSyntax handler, EventReferenceSyntax reference) =>
        QuestProgram.QuestEntries.Contains(reference.Name) && (handler.QuestId ?? _defaultQuest) > 0
            ? QuestProgram.EntryName(reference.Name, (int)(handler.QuestId ?? _defaultQuest))
            : reference.Name;

    private void DeclareHandlers()
    {
        foreach (var handler in _file.Handlers)
        {
            foreach (var reference in handler.Events)
            {
                var name = EntryName(handler, reference);
                if (!_eventIds.TryGetValue(name, out var id))
                    continue;

                if (QuestProgram.IsEntry(reference.Name))
                    _entryEvents.Add(id);

                _usedNames.Add(name);
                if (!_handledEvents.Add(id) && !reference.Name.Equals(QuestProgram.GreetingEvent, StringComparison.OrdinalIgnoreCase))
                    _diagnostics.Error(DiagnosticId.DuplicateEvent, reference.Span,
                        $"\"{reference.Name}\" already has an 'On' block.",
                        "Each event is answered in exactly one place.");
            }
        }
    }

    private void CheckSpaceMatchesGifts(QuestEventEntry entry)
    {
        var required = 0;
        var gifts = 0;
        var span = entry.Span;

        void Walk(IReadOnlyList<BoundStatement> body)
        {
            foreach (var statement in body)
            {
                switch (statement)
                {
                    case BoundStatement.Action { Kind: QuestActionKind.GiveItem }:
                        gifts++;
                        break;

                    case BoundStatement.If node:
                        foreach (var (condition, armBody) in node.Arms)
                        {
                            required = Math.Max(required, SpaceRequired(condition));
                            Walk(armBody);
                        }
                        if (node.ElseBody is not null)
                            Walk(node.ElseBody);
                        break;

                    case BoundStatement.Switch node:
                        foreach (var clause in node.Cases)
                            Walk(clause.Body);
                        if (node.DefaultBody is not null)
                            Walk(node.DefaultBody);
                        break;
                }
            }
        }

        Walk(entry.Body);

        if (required <= 0 || gifts <= 0 || required <= gifts)
            return;

        _diagnostics.Warning(DiagnosticId.SpaceMismatch, span,
            $"This asks for {required} free slots but hands over {gifts} item(s).",
            "A player with fewer free slots is refused something that would have fitted; "
            + "stacking can lower the real need further.");
    }

    private static int SpaceRequired(BoundCondition condition) => condition switch
    {
        BoundCondition.And node => Math.Max(SpaceRequired(node.Left), SpaceRequired(node.Right)),
        BoundCondition.Or node => Math.Max(SpaceRequired(node.Left), SpaceRequired(node.Right)),
        BoundCondition.Not node => SpaceRequired(node.Operand),
        BoundCondition.Predicate { Kind: QuestConditionKind.RoomFor } node =>
            (int)node.Arguments.GetInt("count", 0),
        _ => 0,
    };

    private void BindObjectives()
    {
        const int maxGroups = 4;
        var seen = new Dictionary<long, TextSpan>();

        foreach (var block in _file.Objectives)
        {
            if (seen.TryGetValue(block.QuestId, out var first))
            {
                _diagnostics.Error(DiagnosticId.DuplicateObjectives, block.QuestSpan,
                    $"Quest {block.QuestId} already lists its objectives at line "
                    + $"{_file.Source.GetLinePosition(first.Start).Line + 1}.");
                continue;
            }
            seen[block.QuestId] = block.QuestSpan;

            if (block.QuestId is <= 0 or > short.MaxValue)
                _diagnostics.Error(DiagnosticId.BadArgumentCount, block.QuestSpan,
                    $"A quest id must be between 1 and {short.MaxValue}.");
            CheckCatalog(SlotKind.QuestId, block.QuestId, block.QuestSpan);

            if (block.Groups.Count > maxGroups)
                _diagnostics.Error(DiagnosticId.TooManyKillGroups, block.Span,
                    $"A quest tracks {maxGroups} objectives; this one lists {block.Groups.Count}.");

            var groups = new List<KillObjective>();
            foreach (var group in block.Groups.Take(maxGroups))
            {
                if (group.Count is <= 0 or > short.MaxValue)
                    _diagnostics.Error(DiagnosticId.BadArgumentCount, group.CountSpan,
                        $"An objective must ask for between 1 and {short.MaxValue} kills.");

                var monsters = new List<int>();
                foreach (var token in group.Monsters)
                {
                    var id = ResolveMonster(token);
                    if (id is <= 0 or > short.MaxValue)
                        _diagnostics.Error(DiagnosticId.BadArgumentCount, token.Span,
                            $"A monster id must be between 1 and {short.MaxValue}.");
                    if (id > 0 && !monsters.Contains(id))
                        monsters.Add(id);
                }
                if (monsters.Count > 4)
                    _diagnostics.Error(DiagnosticId.BadArgumentCount, group.Span,
                        "An objective can name at most four monsters.");
                var target = group.Target is { } named ? (int)ResolveNamed(SlotKind.MapId, named, -1) : -1;
                groups.Add(new KillObjective((int)group.Count, monsters, target));
            }

            if (block.AnyWillDo && groups.Count < 2)
                _diagnostics.Warning(DiagnosticId.UnusedQuestScope, block.QuestSpan,
                    "'needs any' says nothing when the quest has one objective.");

            var title = BindQuestText(block.Title);
            var journal = BindQuestText(block.Journal);
            var perNation = block.Journals ?? [];
            var perNationTitles = block.Titles ?? [];
            if (perNation.Count > 0 || perNationTitles.Count > 0)
            {
                var scoped = new Dictionary<(int Nation, int ClassGroup), (string? Title, string? Journal)>();
                foreach (var entry in perNationTitles)
                    if (TextScope(entry.Nation) is { } scope)
                        scoped[scope] = (BindQuestText(entry.Text), null);
                foreach (var entry in perNation)
                    if (TextScope(entry.Nation) is { } scope)
                        scoped[scope] = (scoped.GetValueOrDefault(scope).Title, BindQuestText(entry.Text));
                foreach (var entry in perNation)
                    if (perNationTitles.Count > 0 && TextScope(entry.Nation) is { } scope && scoped[scope].Title is null)
                        _diagnostics.Error(DiagnosticId.UnknownDirective, entry.Nation.Span,
                            $"\"{entry.Nation.Text}\" has a Journal line but no Title line; give every nation a title, or write it on the Quest line.");
                foreach (var (scope, text) in scoped)
                    _questTexts.Add(new QuestText((int)block.QuestId, text.Title ?? title,
                        text.Journal ?? journal, block.Daily, scope.Nation, scope.ClassGroup, block.Repeat));
            }
            else if (title is not null || journal is not null || block.Daily || block.Repeat)
            {
                _questTexts.Add(new QuestText((int)block.QuestId, title, journal, block.Daily, 0, 0, block.Repeat));
            }

            if (groups.Count > 0)
                _objectives.Add(new QuestObjectives(
                    (int)block.QuestId, groups,
                    block.AnyWillDo ? ObjectiveRule.Any : ObjectiveRule.All));
        }
    }

    private (int Nation, int ClassGroup)? TextScope(Token scope)
    {
        if (QuestVocabulary.ClassGroups.TryGetValue(scope.Text, out var group))
            return (0, group);
        if (QuestVocabulary.Nations.TryGetValue(scope.Text, out var nation))
            return (nation, 0);
        _diagnostics.Error(DiagnosticId.UnknownDirective, scope.Span,
            $"\"{scope.Text}\" is not a nation or a class.");
        return null;
    }

    private IReadOnlyList<BoundStatement.Action> BindGrantedItems() =>
        BindQuestItems(block => block.Grants?.Select(g => (g.Span, g.Count, g.CountSpan, (Token?)g.Item)),
            QuestActionKind.GiveItem, "Give");

    private IReadOnlyList<BoundStatement.Action> BindCollectedItems() =>
        BindQuestItems(block => block.Collects?.Select(c => (c.Span, c.Count, c.CountSpan, c.Item)),
            QuestActionKind.TakeItem, "Collect");

    private IReadOnlyList<(int ClassGroup, BoundStatement.Action Take)> BindCollectedByClass()
    {
        var byClass = new List<(int, BoundStatement.Action)>();
        foreach (var block in _file.Objectives)
        {
            foreach (var entry in block.Collects ?? [])
            {
                var one = BindQuestItems(b => ReferenceEquals(b, block)
                        ? [(entry.Span, entry.Count, entry.CountSpan, entry.Item)] : null,
                    QuestActionKind.TakeItem, "Collect");
                foreach (var take in one)
                    byClass.Add((ResolveClassGroup(entry.ClassGroup), take));
            }
        }
        return byClass;
    }

    private IReadOnlyList<BoundStatement.Action> BindQuestItems(
        Func<QuestObjectivesSyntax, IEnumerable<(TextSpan Span, long Count, TextSpan CountSpan, Token? Item)>?> select,
        QuestActionKind kind,
        string keyword)
    {
        var takes = new List<BoundStatement.Action>();
        foreach (var block in _file.Objectives)
        {
            foreach (var entry in select(block) ?? [])
            {
                if (block.QuestId != _defaultQuest)
                {
                    _diagnostics.Error(DiagnosticId.UnknownDirective, entry.Span,
                        $"A '{keyword}' line belongs to the quest this file binds.");
                    continue;
                }
                if (entry.Count is <= 0 or > int.MaxValue)
                {
                    _diagnostics.Error(DiagnosticId.BadArgumentCount, entry.CountSpan,
                        $"A '{keyword}' line must name between 1 and {int.MaxValue}.");
                    continue;
                }
                if (entry.Item is not { } named)
                {
                    takes.Add(new BoundStatement.Action(entry.Span, QuestActionKind.TakeGold,
                        new ArgumentSet(new Dictionary<string, long> { ["amount"] = entry.Count })));
                    continue;
                }
                var item = named.Kind == TokenKind.Number
                    ? named.Value
                    : ResolveNamed(SlotKind.ItemId, named, 0);
                if (named.Kind == TokenKind.Number)
                    CheckCatalog(SlotKind.ItemId, item, named.Span);
                takes.Add(new BoundStatement.Action(entry.Span, kind,
                    new ArgumentSet(new Dictionary<string, long>
                    { ["item"] = item, ["count"] = entry.Count })));
            }
        }
        return takes;
    }

    private string? BindQuestText(Token? token)
    {
        if (token is not { } text)
            return null;

        if (text.Text.Trim().Length == 0)
        {
            _diagnostics.Error(DiagnosticId.EmptyDialogText, text.Span,
                "The quest panel would show an empty line here.");
            return null;
        }

        _translatable.Add(new TranslatableText(text.Span, text.Text, SlotKind.TalkTextId));
        return text.Text;
    }

    private int ResolveMonster(Token token)
    {
        if (token.Kind == TokenKind.Number)
        {
            CheckCatalog(SlotKind.NpcId, token.Value, token.Span);
            return (int)token.Value;
        }

        return (int)ResolveNamed(SlotKind.NpcId, token, 0);
    }

    private void ReportUnreachableLocalEvents(IReadOnlyDictionary<int, QuestEventEntry> events)
    {
        foreach (var (id, entry) in events)
        {
            if (id < QuestProgram.LocalEventBase || _referencedEvents.Contains(id))
                continue;
            if (_entryEvents.Contains(id) || entry.Name is null)
                continue;
            _diagnostics.Warning(DiagnosticId.UnreachableEvent, entry.Span,
                $"Nothing leads to \"{entry.Name}\".",
                "A local event can only be reached from this file, so an unreferenced one is dead.");
        }
    }

    private void DeclareNamed(DeclarationSyntax declaration, string? file, TextSpan? blame = null)
    {
        var value = declaration.Value ?? 0;
        var span = blame ?? declaration.NameSpan;
        if (_named.TryGetValue(declaration.Name, out var existing) && existing.File is null)
        {
            var where = existing.File is { Length: > 0 }
                ? $" in {existing.File}"
                : " in this file";
            _diagnostics.Error(DiagnosticId.DuplicateConstant, span,
                $"\"{declaration.Name}\" already names {SlotKinds.Name(existing.Kind)} {existing.Value}{where}.");
            return;
        }

        if (_events.ContainsKey(declaration.Name))
        {
            _diagnostics.Error(DiagnosticId.DuplicateConstant, span,
                $"\"{declaration.Name}\" is already an event.");
            return;
        }

        _texts.Remove(declaration.Name);
        if (declaration.Kind == SlotKind.TalkTextId && declaration.Location is { } words)
        {
            _texts[declaration.Name] = words.Title;
            _named[declaration.Name] = new Named(
                declaration.Kind, declaration.Name, 0, span, file);
            if (file is null)
                _translatable.Add(
                    new TranslatableText(words.TitleSpan, words.Title, SlotKind.TalkTextId));
            return;
        }

        if (declaration.Location is not null)
            value = _locations.Count;

        _named[declaration.Name] = new Named(
            declaration.Kind, declaration.Name, value, span, file);

        if (file is null && declaration.Location is null)
            CheckCatalog(declaration.Kind, value, declaration.ValueSpan);

        if (declaration.Location is { } payload)
        {
            _locations.Add(new QuestLocation(
                declaration.Name, payload.Title, payload.Where, payload.X, payload.Y, payload.Zone,
                payload.About, payload.ElMoradX, payload.ElMoradY));
            if (file is null)
            {
                _translatable.Add(new TranslatableText(payload.TitleSpan, payload.Title, SlotKind.TalkTextId));
                if (payload.About is { Length: > 0 })
                    _translatable.Add(new TranslatableText(payload.AboutSpan, payload.About, SlotKind.TalkTextId));
            }
        }
    }

    public void ImportRewards(IReadOnlyList<RewardDefinitionSyntax> definitions, string file) =>
        DeclareRewards(definitions, file);

    public void Import(IReadOnlyList<DeclarationSyntax> declarations, string file, TextSpan blame)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in declarations)
        {
            if (!names.Add(declaration.Name))
            {
                _diagnostics.Error(DiagnosticId.DuplicateConstant, blame,
                    $"\"{declaration.Name}\" is declared more than once in {file}.");
                continue;
            }
            if (declaration.Kind == SlotKind.EventRef)
                continue;
            DeclareNamed(declaration, file, blame);
        }
    }

    private void ReportUnusedNames()
    {
        foreach (var symbol in _events.Values)
        {
            if (_usedNames.Contains(symbol.Name))
                continue;
            _diagnostics.Warning(DiagnosticId.UnusedDeclaration, symbol.Span,
                $"\"{symbol.Name}\" is named at the top but no 'On' block answers it.");
        }
    }

    private IReadOnlyList<BoundStatement> BindBlock(IReadOnlyList<StatementSyntax> statements)
    {
        var enclosing = _scopeQuest;
        var enclosingAt = _scopeQuestAt;
        var bound = new List<BoundStatement>();
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i] is StatementSyntax.Action
                { Signature.Kind: QuestActionKind.Say or QuestActionKind.ShowQuestPage } message)
            {
                bound.Add(BindDialog(message, statements, ref i));
                continue;
            }

            var statement = BindStatement(statements[i]);
            if (statement is not null)
                bound.Add(statement);
        }
        _scopeQuest = enclosing;
        _scopeQuestAt = enclosingAt;
        return bound;
    }

    private void ReportUnusedQuestScopes()
    {
        foreach (var (quest, span) in _questScopes)
        {
            if (_consumedScopes.Contains(span.Start))
                continue;
            _diagnostics.Warning(DiagnosticId.UnusedQuestScope, span,
                $"Nothing here shows a dialog, so 'for quest {quest}' has no effect.",
                "A quest on the header only titles a dialog; 'Complete' and 'If quest ...' name their own.");
        }
    }

    private BoundStatement BindDialog(
        StatementSyntax.Action message,
        IReadOnlyList<StatementSyntax> statements,
        ref int index)
    {
        var arguments = message.Match.Arguments;
        var header = ResolveLine(SlotKind.TalkTextId, arguments["text"], allowEmpty: true);
        var questId = _scopeQuest;
        if (_scopeQuestAt >= 0)
            _consumedScopes.Add(_scopeQuestAt);

        var style = DialogStyle.Talk;
        if (message.Signature.Defaults?.TryGetValue("style", out var styleValue) == true)
        {
            style = (DialogStyle)styleValue;
        }
        else if (arguments.TryGetValue("style", out var flagToken))
        {
            style = (DialogStyle)flagToken.Value;
            if (!Enum.IsDefined(style))
                _diagnostics.Warning(DiagnosticId.UnverifiedDialogFlag, flagToken.Span,
                    $"Dialog flag {flagToken.Value} is not one of the five the client is known to render.",
                    "A handful of retail scripts carry stray flag values that behave like a plain talk dialog.");
        }

        var buttons = new List<DialogChoice>();
        IReadOnlyList<BoundStatement>? fallback = null;
        var span = message.Span;

        while (index + 1 < statements.Count)
        {
            var following = statements[index + 1];

            if (following is StatementSyntax.If trailer && IsFallbackGate(trailer))
            {
                index++;
                span = span.Union(trailer.Span);
                fallback = BindFallback(trailer, buttons);
                break;
            }

            if (following is StatementSyntax.Action { Signature.Kind: QuestActionKind.Button } next)
            {
                index++;
                span = span.Union(next.Span);
                buttons.Add(new DialogChoice(null, BindButton(next)));
                continue;
            }

            if (following is StatementSyntax.ButtonBlock inline)
            {
                index++;
                span = span.Union(inline.Span);
                buttons.Add(new DialogChoice(null, BindButtonBlock(inline)));
                continue;
            }

            if (following is StatementSyntax.If gate && ButtonsOnly(gate))
            {
                index++;
                span = span.Union(gate.Span);
                CollectDialogButtons(gate, null, buttons);
                continue;
            }

            break;
        }

        if (buttons.Count == 0 && !_file.HasBinding)
            _diagnostics.Warning(DiagnosticId.MessageWithoutButtons, message.Span,
                "Nothing is offered after this, so the player has no way out of the dialog.",
                "Add a line like: Button close_it goto close");

        if (buttons.Count(choice => choice.When is null) > QuestVocabulary.MaxDialogButtons)
        {
            _diagnostics.Error(DiagnosticId.TooManyButtons, span,
                $"A dialog can show {QuestVocabulary.MaxDialogButtons} choices; "
                + $"this one always offers {buttons.Count(choice => choice.When is null)}.");
        }

        var page = message.Signature.Kind == QuestActionKind.ShowQuestPage
            ? QuestPageKind.Quest : QuestPageKind.Conversation;
        if (page == QuestPageKind.Quest && _defaultQuest <= 0)
            _diagnostics.Error(DiagnosticId.UnknownAction, message.Span,
                "'Show quest' belongs in a bound file with one Quest.");
        return new BoundStatement.Dialog(span, style, questId, header, buttons, fallback, page);
    }

    private static bool IsFallbackGate(StatementSyntax.If gate) =>
        gate.ElseBody is null
        && gate.Arms.Count == 1
        && gate.Arms[0].Condition is ConditionSyntax.Predicate
        { Signature.Kind: QuestConditionKind.NoTopicFits };

    private IReadOnlyList<BoundStatement>? BindFallback(
        StatementSyntax.If gate, List<DialogChoice> buttons)
    {
        var body = BindBlock(gate.Arms[0].Body);
        if (body.Count != 1 || body[0] is not (BoundStatement.Dialog or BoundStatement.Goto))
        {
            _diagnostics.Error(DiagnosticId.MisplacedFallback, gate.Span,
                "'If no topic fits' holds one message and its buttons, or one 'Goto'.",
                "Write a 'Say' line and its buttons, or send the player somewhere that has them.");
            return null;
        }

        if (!_file.HasBinding && !buttons.Any(choice => choice.When is not null))
            _diagnostics.Warning(DiagnosticId.MisplacedFallback, gate.Span,
                "No choice above this carries a condition, so this message always replaces the one above.",
                "A topic is a button under an 'If'; without one there is nothing for this to fall back from.");

        return body;
    }

    private static bool ButtonsOnly(StatementSyntax.If gate)
    {
        static bool AllOffers(IReadOnlyList<StatementSyntax> body) =>
            body.Count > 0 && body.All(s =>
                s is StatementSyntax.Action { Signature.Kind: QuestActionKind.Button }
                || s is StatementSyntax.ButtonBlock
                || (s is StatementSyntax.If nested && ButtonsOnly(nested)));

        return gate.Arms.All(arm => AllOffers(arm.Body))
            && (gate.ElseBody is null || AllOffers(gate.ElseBody));
    }

    private void CollectDialogButtons(
        StatementSyntax.If gate,
        BoundCondition? outer,
        List<DialogChoice> buttons)
    {
        BoundCondition? earlier = null;
        foreach (var (condition, body) in gate.Arms)
        {
            var mine = BindCondition(condition);
            var reached = earlier is null ? mine : Both(new BoundCondition.Not(earlier), mine);
            AddDialogButtons(body, Both(outer, reached), buttons);
            earlier = earlier is null ? mine : new BoundCondition.Or(earlier, mine);
        }

        if (gate.ElseBody is not null)
        {
            var reached = earlier is null ? null : new BoundCondition.Not(earlier);
            AddDialogButtons(gate.ElseBody, Both(outer, reached), buttons);
        }
    }

    private void AddDialogButtons(
        IReadOnlyList<StatementSyntax> body,
        BoundCondition? when,
        List<DialogChoice> buttons)
    {
        foreach (var statement in body)
        {
            if (statement is StatementSyntax.Action { Signature.Kind: QuestActionKind.Button } button)
                buttons.Add(new DialogChoice(when, BindButton(button)));
            else if (statement is StatementSyntax.ButtonBlock inline)
                buttons.Add(new DialogChoice(when, BindButtonBlock(inline, when)));
            else if (statement is StatementSyntax.If nested)
                CollectDialogButtons(nested, when, buttons);
        }
    }

    private static BoundCondition? Both(BoundCondition? left, BoundCondition? right) =>
        left is null ? right : right is null ? left : new BoundCondition.And(left, right);

    private static IReadOnlyList<BoundStatement> Guard(
        IReadOnlyList<BoundStatement> body, BoundCondition? condition, TextSpan span) =>
        condition is null ? body : [new BoundStatement.If(span, [(condition, body)], null)];

    private static bool HasTransientPredicate(BoundCondition condition) => condition switch
    {
        BoundCondition.Predicate p => p.Kind is QuestConditionKind.Chance or QuestConditionKind.RollUnder
            or QuestConditionKind.LastStepFailed,
        BoundCondition.And a => HasTransientPredicate(a.Left) || HasTransientPredicate(a.Right),
        BoundCondition.Or o => HasTransientPredicate(o.Left) || HasTransientPredicate(o.Right),
        BoundCondition.Not n => HasTransientPredicate(n.Operand),
        _ => false
    };

    private static BoundCondition? PersistentReplyGuard(BoundCondition? condition) => condition switch
    {
        null => null,
        BoundCondition.And a => Both(PersistentReplyGuard(a.Left), PersistentReplyGuard(a.Right)),
        _ => HasTransientPredicate(condition) ? null : condition
    };

    private DialogButton BindButtonBlock(StatementSyntax.ButtonBlock button, BoundCondition? when = null)
    {
        var label = ResolveLine(SlotKind.MenuTextId, button.Match.Arguments["label"]);
        var id = _nextLocalEventId++;
        var outer = _replyGuard;
        _replyGuard = null;
        var body = BindBlock(button.Body);
        _replyGuard = outer;
        _lifted.Add(new QuestEventEntry(id, null, button.HeaderSpan,
            Guard(body, PersistentReplyGuard(Both(outer, when)), button.HeaderSpan)));
        return new DialogButton(label, id, BindButtonReward(button.Match));
    }

    private DialogButton BindButton(StatementSyntax.Action button)
    {
        var label = ResolveLine(SlotKind.MenuTextId, button.Match.Arguments["label"]);
        return new DialogButton(label, ResolveEventTarget(button.Match.Arguments["target"]), BindButtonReward(button.Match));
    }

    private int BindButtonReward(PhraseMatch match)
    {
        if (!match.Arguments.TryGetValue("award", out var token))
            return -1;
        if (token.Kind != TokenKind.Number || token.Value is < 0 or > 4)
        {
            _diagnostics.Error(DiagnosticId.NumberOutOfRange, token.Span, "A reward choice must be between 0 and 4.");
            return -1;
        }
        return (int)token.Value;
    }

    private static bool IsTransfer(BoundStatement.Action action) =>
        action.Kind is QuestActionKind.GiveItem or QuestActionKind.TakeItem
            or QuestActionKind.GiveGold or QuestActionKind.TakeGold
            or QuestActionKind.GiveExperience or QuestActionKind.GiveNationalPoints
            or QuestActionKind.TakeNationalPoints or QuestActionKind.GiveRandomReward
            or QuestActionKind.PromoteNovice or QuestActionKind.Promote;

    private static IEnumerable<BoundStatement> Everything(IReadOnlyList<BoundStatement> body)
    {
        foreach (var statement in body)
        {
            yield return statement;
            var nested = statement switch
            {
                BoundStatement.Switch node =>
                    node.Cases.SelectMany(entry => entry.Body).Concat(node.DefaultBody ?? []),
                BoundStatement.If node =>
                    node.Arms.SelectMany(arm => arm.Body).Concat(node.ElseBody ?? []),
                _ => [],
            };
            foreach (var inner in Everything([.. nested]))
                yield return inner;
        }
    }

    private const int MaxTransferPaths = 64;

    private static IEnumerable<IReadOnlyList<BoundStatement.Action>> TransferPaths(
        IReadOnlyList<BoundStatement> body)
    {
        IEnumerable<IReadOnlyList<BoundStatement.Action>> paths = [Array.Empty<BoundStatement.Action>()];
        foreach (var statement in body)
        {
            IReadOnlyList<BoundStatement>[]? branches = statement switch
            {
                BoundStatement.Switch node =>
                [
                    .. node.Cases.Select(entry => entry.Body),
                    .. node.DefaultBody is null ? [] : new[] { node.DefaultBody },
                ],
                BoundStatement.If node =>
                [
                    .. node.Arms.Select(arm => arm.Body),
                    .. node.ElseBody is null ? [] : new[] { node.ElseBody },
                ],
                _ => null,
            };

            if (branches is not null)
            {
                paths = paths.SelectMany(prefix => branches.SelectMany(
                    branch => TransferPaths(branch).Select(
                        rest => (IReadOnlyList<BoundStatement.Action>)[.. prefix, .. rest])));
                continue;
            }

            if (statement is BoundStatement.Action action && IsTransfer(action))
                paths = paths.Select(
                    prefix => (IReadOnlyList<BoundStatement.Action>)[.. prefix, action]);
        }

        return paths;
    }


    private static IEnumerable<BoundCondition> Operands(BoundCondition condition)
    {
        yield return condition;
        var nested = condition switch
        {
            BoundCondition.And node => new[] { node.Left, node.Right },
            BoundCondition.Or node => [node.Left, node.Right],
            BoundCondition.Not node => [node.Operand],
            _ => Array.Empty<BoundCondition>(),
        };
        foreach (var operand in nested)
        {
            foreach (var inner in Operands(operand))
                yield return inner;
        }
    }

    private static string Shape(BoundCondition condition) => condition switch
    {
        BoundCondition.Predicate p =>
            $"{p.Kind}:{p.Operator}:{string.Join(',', p.Arguments.Values.OrderBy(v => v.Key).Select(v => $"{v.Key}={v.Value}"))}",
        BoundCondition.And node => $"and({Shape(node.Left)},{Shape(node.Right)})",
        BoundCondition.Or node => $"or({Shape(node.Left)},{Shape(node.Right)})",
        BoundCondition.Not node => $"not({Shape(node.Operand)})",
        _ => condition.GetType().Name,
    };

    private void LintCondition(BoundCondition condition, TextSpan span)
    {
        foreach (var operand in Operands(condition))
        {
            var (left, right, word) = operand switch
            {
                BoundCondition.And node => (node.Left, node.Right, "and"),
                BoundCondition.Or node => (node.Left, node.Right, "or"),
                _ => (null, null, null),
            };
            if (left is not null && Shape(left) == Shape(right!))
                _diagnostics.Warning(DiagnosticId.RepeatedCondition, span,
                    $"Both sides of this '{word}' ask the same thing.");
        }
    }

    private void LintBody(IReadOnlyList<BoundStatement> body)
    {
        for (var index = 0; index < body.Count; index++)
        {
            if (body[index] is BoundStatement.Reward reward)
            {
                var repeats = TransferPaths(reward.Body).Take(MaxTransferPaths).Any(path =>
                {
                    var shapes = path.Where(Stacks).Select(Shape).ToArray();
                    return shapes.Length != shapes.Distinct().Count();
                });
                if (repeats)
                    _diagnostics.Warning(DiagnosticId.RepeatedTransfer, reward.Span,
                        "This repeats a transfer; add the amounts together instead.");
                if (index + 1 < body.Count && body[index + 1] is BoundStatement.Reward next
                    && Moved(reward).Intersect(Moved(next)).Any())
                    _diagnostics.Warning(DiagnosticId.SplitTransaction, reward.Span,
                        "These two transactions move the same item and can half-apply.",
                        "Put the transfers in one block so they are applied together.");
                if (index > 0 && body[index - 1] is BoundStatement.Dialog or BoundStatement.Say)
                    _diagnostics.Warning(DiagnosticId.RewardBeforeItIsPaid, reward.Span,
                        "This dialogue runs before the transaction, so it shows even when it fails.",
                        "Move it inside the block, which only runs when the transfers were applied.");
            }

            foreach (var nested in body[index] switch
            {
                BoundStatement.If node => node.Arms.SelectMany(a => a.Body).Concat(node.ElseBody ?? []),
                BoundStatement.Switch node => node.Cases.SelectMany(c => c.Body).Concat(node.DefaultBody ?? []),
                BoundStatement.Reward node => node.Body.Concat(node.ElseBody ?? []),
                _ => Enumerable.Empty<BoundStatement>(),
            })
                LintBody([nested]);

            if (body[index] is BoundStatement.If branch)
            {
                foreach (var (condition, _) in branch.Arms)
                    LintCondition(condition, branch.Span);
            }
        }
    }

    private bool Stacks(BoundStatement.Action action)
    {
        var item = action.Arguments.Get("item");
        return item == 0 || !_catalog.KnowsItems || _catalog.ItemStacks(item);
    }

    private static IEnumerable<long> Moved(BoundStatement.Reward reward) =>
        Everything(reward.Body).OfType<BoundStatement.Action>()
            .Where(IsTransfer)
            .Select(action => action.Arguments.Get("item"))
            .Where(item => item != 0);

    private static string Shape(BoundStatement.Action action) =>
        $"{action.Kind}:{string.Join(',', action.Arguments.Values.OrderBy(v => v.Key).Select(v => $"{v.Key}={v.Value}"))}";

    private BoundStatement? BindStatement(StatementSyntax statement)
    {
        switch (statement)
        {
            case StatementSyntax.Reward reward:
            {
                var body = BindBlock(reward.Body);
                var actions = Everything(body).OfType<BoundStatement.Action>().ToArray();
                if (!actions.Any(IsTransfer))
                    _diagnostics.Error(DiagnosticId.UnknownAction, reward.Span,
                        "A Transaction needs at least one Take or Give line.",
                        "Without one it has nothing to apply, so it always fails.");
                return new BoundStatement.Reward(reward.Span, body,
                    reward.ElseBody is null ? null : BindBlock(reward.ElseBody));
            }
            case StatementSyntax.Action action:
                return BindAction(action);

            case StatementSyntax.Choice choice:
                _diagnostics.Error(DiagnosticId.UnknownAction, choice.Span,
                    "'Choose one' belongs inside Rewards, where the claim applies the player's pick.");
                return null;

            case StatementSyntax.If node:
            {
                var arms = new List<(BoundCondition, IReadOnlyList<BoundStatement>)>();
                var outer = _replyGuard;
                BoundCondition? earlier = null;
                foreach (var (condition, body) in node.Arms)
                {
                    var current = BindCondition(condition);
                    var reached = earlier is null ? current : Both(new BoundCondition.Not(earlier), current);
                    _replyGuard = Both(outer, reached);
                    arms.Add((current, BindBlock(body)));
                    earlier = earlier is null ? current : new BoundCondition.Or(earlier, current);
                }
                _replyGuard = Both(outer, earlier is null ? null : new BoundCondition.Not(earlier));
                var elseBody = node.ElseBody is null ? null : BindBlock(node.ElseBody);
                _replyGuard = outer;
                if (arms.All(a => a.Item2.Count == 0) && (elseBody is null || elseBody.Count == 0))
                    _diagnostics.Warning(DiagnosticId.EmptyBlock, node.Span, "This 'If' does nothing either way.");
                return new BoundStatement.If(node.Span, arms, elseBody);
            }

            case StatementSyntax.Switch node:
                return BindSwitch(node);

            case StatementSyntax.ButtonBlock button:
                _diagnostics.Error(DiagnosticId.ButtonWithoutMessage, button.HeaderSpan,
                    "A 'Button' has to follow the 'Say' it belongs to.",
                    "Move it directly under its 'Say', with nothing in between.");
                BindBlock(button.Body);
                return null;

            default:
                return null;
        }
    }

    private BoundStatement BindSwitch(StatementSyntax.Switch node)
    {
        var cases = new List<(IReadOnlyList<long>, IReadOnlyList<BoundStatement>)>();
        var seen = new Dictionary<long, TextSpan>();

        foreach (var clause in node.Cases)
        {
            var labels = new List<long>();
            foreach (var label in clause.Labels)
            {
                var value = ResolveCaseLabel(node.Selector, label);
                if (value is null)
                    continue;
                var last = label.Last ?? value.Value;
                if (last < value.Value || last - value.Value > 100000
                    || label.Last is not null && node.Selector is not (SwitchSelectorKind.Roll or SwitchSelectorKind.Reward))
                {
                    _diagnostics.Error(DiagnosticId.BadArgumentCount, label.Span,
                        "A numeric case range must ascend and contain at most 100001 values.");
                    continue;
                }
                if (node.Selector == SwitchSelectorKind.Reward && (value < -1 || last > 4))
                    _diagnostics.Error(DiagnosticId.BadArgumentCount, label.Span,
                        "Reward choices are 0 through 4; -1 means no selection.");
                if (node.Selector == SwitchSelectorKind.Roll && (value < 0 || last > node.Max))
                    _diagnostics.Warning(DiagnosticId.CaseTypeMismatch, label.Span,
                        $"A roll of {node.Max} never comes up {value.Value}.");
                for (var number = value.Value; number <= last; number++)
                {
                    if (!seen.TryAdd(number, label.Span))
                    {
                        _diagnostics.Error(DiagnosticId.DuplicateCaseLabel, label.Span,
                            $"'{number}' is already covered further up.");
                        break;
                    }
                    labels.Add(number);
                    if (number == long.MaxValue)
                        break;
                }
            }
            cases.Add((labels, BindBlock(clause.Body)));
        }

        if (node.DefaultBody is null)
        {
            var missing = node.Selector switch
            {
                SwitchSelectorKind.PlayerClass => QuestVocabulary.ClassGroupNames
                    .Where(name => !seen.ContainsKey(QuestVocabulary.ClassGroups[name])).ToArray(),
                SwitchSelectorKind.PlayerNation => QuestVocabulary.NationNames
                    .Where(name => !seen.ContainsKey(QuestVocabulary.Nations[name])).ToArray(),
                SwitchSelectorKind.Roll => Enumerable.Range(0, (int)Math.Min(node.Max, 1024))
                    .Where(number => !seen.ContainsKey(number))
                    .Select(number => number.ToString()).ToArray(),
                _ => [],
            };
            if (missing.Length > 0)
                _diagnostics.Warning(DiagnosticId.CaseTypeMismatch, node.SelectorSpan,
                    $"Nothing here answers {Listed(missing)}.",
                    "Add the missing 'For' lines, or an 'Else' so everyone gets an answer.");
        }

        return new BoundStatement.Switch(
            node.Span, node.Selector, cases, node.DefaultBody is null ? null : BindBlock(node.DefaultBody),
            (int)node.Max);
    }

    private static string Listed(IReadOnlyList<string> items) =>
        items.Count <= 8
            ? string.Join(", ", items)
            : $"{string.Join(", ", items.Take(8))} and {items.Count - 8} more";

    private long? ResolveCaseLabel(SwitchSelectorKind selector, CaseLabelSyntax label)
    {
        var shown = label.Name ?? label.Number?.ToString() ?? "?";
        switch (selector)
        {
            case SwitchSelectorKind.Reward:
            case SwitchSelectorKind.Roll:
                if (label.Number is { } number)
                    return number;
                _diagnostics.Error(DiagnosticId.UnknownEnumMember, label.Span,
                    $"A roll comes up a number, not '{shown}'.",
                    "Write the numbers the roll can come up, as in '0, 1, 2'.");
                return null;

            case SwitchSelectorKind.PlayerClass:
                if (label.Name is { } className && QuestVocabulary.ClassGroups.TryGetValue(className, out var classValue))
                    return classValue;
                _diagnostics.Error(DiagnosticId.UnknownEnumMember, label.Span,
                    $"'{shown}' is not a class.",
                    $"The classes are: {string.Join(", ", QuestVocabulary.ClassGroupNames)}.");
                return null;

            case SwitchSelectorKind.PlayerNation:
                if (label.Name is { } nationName && QuestVocabulary.Nations.TryGetValue(nationName, out var nationValue))
                    return nationValue;
                _diagnostics.Error(DiagnosticId.UnknownEnumMember, label.Span,
                    $"'{shown}' is not a nation.",
                    $"The nations are: {string.Join(", ", QuestVocabulary.NationNames)}.");
                return null;

            case SwitchSelectorKind.Event:
                if (label.Name is { } eventName && _eventIds.TryGetValue(eventName, out var eventId))
                {
                    _usedNames.Add(eventName);
                    _referencedEvents.Add(eventId);
                    return eventId;
                }
                _diagnostics.Error(DiagnosticId.UndeclaredName, label.Span,
                    $"Nothing in this file names \"{shown}\" as an event.");
                return null;

            default:
                return null;
        }
    }

    private BoundStatement BindAction(StatementSyntax.Action action)
    {
        var kind = action.Signature.Kind;
        var arguments = action.Match.Arguments;

        if (kind is QuestActionKind.Button or QuestActionKind.Button)
        {
            _diagnostics.Error(DiagnosticId.ButtonWithoutMessage, action.Span,
                "A 'Button' has to follow the 'Say' it belongs to.",
                "Move it directly under its 'Say', with nothing in between.");
            return new BoundStatement.Action(action.Span, kind, ArgumentSet.Empty);
        }

        if (kind == QuestActionKind.Todo)
        {
            _diagnostics.Warning(DiagnosticId.UnfinishedStatement, action.Span,
                "This is a note, not behaviour: nothing happens here.",
                "A note is kept where the original script could not be expressed.");
            return new BoundStatement.Action(action.Span, kind, ArgumentSet.Empty);
        }

        if (kind == QuestActionKind.Goto)
            return new BoundStatement.Goto(action.Span, ResolveEventTarget(arguments["target"]));

        if (kind == QuestActionKind.Announce)
        {
            var lines = new List<DialogLine>();
            foreach (var name in new[] { "text", "text2", "text3", "text4" })
            {
                if (!arguments.TryGetValue(name, out var token))
                    continue;
                lines.Add(ResolveLine(SlotKind.TalkTextId, token));
            }
            return new BoundStatement.Say(action.Span, lines);
        }

        var values = ResolveSlots(action.Signature.Pattern, arguments);
        if (kind == QuestActionKind.ClaimQuest && (_file.QuestRewards is null
            || values.GetValueOrDefault("quest") != _defaultQuest))
            _diagnostics.Error(DiagnosticId.UnknownAction, action.Span,
                "Claim needs a Rewards declaration for this file's quest.");
        if (kind == QuestActionKind.ShowMap && _scopeQuest > 0)
            values["quest"] = _scopeQuest;
        if (action.Signature.Defaults is { } defaults)
        {
            foreach (var pair in defaults)
                values[pair.Key] = pair.Value;
        }

        return new BoundStatement.Action(action.Span, kind, new ArgumentSet(values));
    }

    private long ResolveNamed(SlotKind kind, Token token, long fallback)
    {
        if (kind == SlotKind.QuestId && token.IsWord("quest"))
        {
            if (_scopeQuest > 0)
                return _scopeQuest;
            _diagnostics.Error(DiagnosticId.EntryWithoutQuest, token.Span,
                "Declare Quest <id> or put 'for quest <id>' on this handler.");
            return fallback;
        }
        if (!_named.TryGetValue(token.Text, out var named))
        {
            _diagnostics.Error(DiagnosticId.UndeclaredName, token.Span,
                $"Nothing names \"{token.Text}\" as {SlotKinds.Name(kind)}.",
                "Declare it at the top of this file, or 'include' the file that does.");
            return fallback;
        }

        _usedNames.Add(token.Text);
        _namedReferences.Add(new NamedReference(
            token.Span, named.Kind, token.Text, DetailFor(named.Kind, named.Value)));

        if (named.Kind != kind)
        {
            _diagnostics.Error(DiagnosticId.WrongNameKind, token.Span,
                $"\"{token.Text}\" names {SlotKinds.Name(named.Kind)} {named.Value}, "
                + $"but {SlotKinds.Name(kind)} is wanted here.");
            return fallback;
        }

        return named.Value;
    }

    private string? DetailFor(SlotKind kind, long value)
    {
        if (kind == SlotKind.MapId)
        {
            if (value < 0 || value >= _locations.Count)
                return null;
            var location = _locations[(int)value];
            return location.Where is { Length: > 0 }
                ? $"{location.Title} \u2014 {location.Where}"
                : location.Title;
        }

        return kind switch
        {
            SlotKind.ItemId => _catalog.ItemName(value),
            SlotKind.NpcId => _catalog.NpcName(value),
            SlotKind.ZoneId => _catalog.ZoneName(value),
            SlotKind.QuestId => _catalog.QuestTitle(value) ?? _catalog.QuestSummary(value),
            _ => null,
        };
    }

    private Dictionary<string, long> ResolveSlots(
        PhrasePattern pattern,
        IReadOnlyDictionary<string, Token> arguments)
    {
        var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in pattern.Slots)
        {
            if (!arguments.TryGetValue(slot.Name, out var token))
                continue;

            if (slot.Kind == SlotKind.EventRef)
            {
                values[slot.Name] = ResolveEventTarget(token);
                continue;
            }

            if (slot.Kind == SlotKind.ClassGroup)
            {
                if (QuestVocabulary.ClassGroups.TryGetValue(token.Text, out var classValue))
                    values[slot.Name] = classValue;
                else
                    _diagnostics.Error(DiagnosticId.UnknownEnumMember, token.Span,
                        $"'{token.Text}' is not a class.",
                        $"The classes are: {string.Join(", ", QuestVocabulary.ClassGroupNames)}.");
                continue;
            }

            if (slot.Kind == SlotKind.Nation)
            {
                if (QuestVocabulary.Nations.TryGetValue(token.Text, out var nationValue))
                    values[slot.Name] = nationValue;
                else
                    _diagnostics.Error(DiagnosticId.UnknownEnumMember, token.Span,
                        $"'{token.Text}' is not a nation.",
                        $"The nations are: {string.Join(", ", QuestVocabulary.NationNames)}.");
                continue;
            }

            if (slot.Kind == SlotKind.PremiumType)
            {
                if (QuestVocabulary.PremiumTypes.TryGetValue(token.Text, out var premiumValue))
                    values[slot.Name] = premiumValue;
                else
                    _diagnostics.Error(DiagnosticId.UnknownEnumMember, token.Span,
                        $"'{token.Text}' is not a premium service.",
                        $"The named ones are: {string.Join(", ", QuestVocabulary.PremiumTypes.Keys)}. "
                        + "Any other service is written as its number.");
                continue;
            }

            if (slot.Kind == SlotKind.ClanRank)
            {
                if (QuestVocabulary.ClanRanks.TryGetValue(token.Text, out var rankValue))
                    values[slot.Name] = rankValue;
                else
                    _diagnostics.Error(DiagnosticId.UnknownEnumMember, token.Span,
                        $"'{token.Text}' is not a clan rank.",
                        $"The ranks are: {string.Join(", ", QuestVocabulary.ClanRankNames)}.");
                continue;
            }

            if (token.Kind is TokenKind.String or TokenKind.Word)
            {
                values[slot.Name] = ResolveNamed(slot.Kind, token, 0);
                continue;
            }

            if (token.Kind != TokenKind.Number)
                continue;

            if (slot.Kind == SlotKind.KillGroup && token.Value is < 1 or > 4)
                _diagnostics.Error(DiagnosticId.BadArgumentCount, token.Span,
                    "A kill quest counts four monster groups, numbered 1 to 4.");

            CheckCatalog(slot.Kind, token.Value, token.Span);
            values[slot.Name] = token.Value;
        }
        return values;
    }

    private long ResolveValue(SlotKind kind, Token token, long fallback)
    {
        if (token.Kind is TokenKind.String or TokenKind.Word)
            return ResolveNamed(kind, token, fallback);

        if (token.Kind == TokenKind.Number)
        {
            CheckCatalog(kind, token.Value, token.Span);
            return token.Value;
        }

        _diagnostics.Error(DiagnosticId.UnexpectedToken, token.Span,
            $"Expected {SlotKinds.Name(kind)} id, found {token}.");
        return fallback;
    }

    private DialogLine ResolveLine(SlotKind kind, Token token, bool allowEmpty = false)
    {
        if (token.Kind == TokenKind.String)
        {
            if (token.Text.Trim().Length == 0)
            {
                if (!allowEmpty)
                    _diagnostics.Error(DiagnosticId.EmptyDialogText, token.Span,
                        "The player would see an empty line here.");
                return DialogLine.None;
            }
            _translatable.Add(new TranslatableText(token.Span, token.Text, kind));
            return DialogLine.FromText(token.Text);
        }

        if (token.Kind == TokenKind.Word)
        {
            if (_texts.TryGetValue(token.Text, out var words))
            {
                _usedNames.Add(token.Text);
                _namedReferences.Add(new NamedReference(
                    token.Span, SlotKind.TalkTextId, token.Text, words));
                return DialogLine.FromText(words);
            }

            _diagnostics.Error(DiagnosticId.UndeclaredName, token.Span,
                $"Nothing names \"{token.Text}\" as text.",
                "Declare it at the top of this file, or 'include' the file that does.");
            return DialogLine.None;
        }

        if (token.Kind == TokenKind.Number)
        {
            CheckCatalog(kind, token.Value, token.Span);
            return DialogLine.FromId((int)token.Value);
        }

        _diagnostics.Error(DiagnosticId.UnexpectedToken, token.Span,
            $"Expected words in quotes or a {SlotKinds.Name(kind)} id, found {token}.");
        return DialogLine.None;
    }

    private int ResolveEventTarget(Token token)
    {
        if (_file.HasBinding && token.IsWord(QuestProgram.GreetingEvent))
            return -2;
        if (token.Kind == TokenKind.Word && token.IsWord(QuestVocabulary.CloseTarget))
            return -1;

        if (!_eventIds.TryGetValue(token.Text, out var id))
        {
            var closest = ClosestEventName(token.Text);
            _diagnostics.Error(DiagnosticId.UndeclaredName, token.Span,
                $"Nothing in this file lists \"{token.Text}\" as an event.",
                closest is not null
                    ? $"Did you mean \"{closest}\"?"
                    : $"Add \"{token.Text}\" is a local event at the top of the file.");
            return -1;
        }

        _usedNames.Add(token.Text);
        _referencedEvents.Add(id);
        _resolvedIds.Add(new ResolvedId(token.Span, SlotKind.EventRef, id));
        if (!_handledEvents.Contains(id) && !_sharedEvents.Contains(id))
            _diagnostics.Error(DiagnosticId.UnknownEventTarget, token.Span,
                $"\"{token.Text}\" is listed but no 'When' block answers it.",
                "Add the block, or point this somewhere that has one.");
        return id;
    }

    private void CheckCatalog(SlotKind kind, long value, TextSpan span)
    {
        _resolvedIds.Add(new ResolvedId(span, kind, value));

        if (value is < int.MinValue or > int.MaxValue)
        {
            _diagnostics.Error(DiagnosticId.BadArgumentCount, span, "This value does not fit a 32-bit quest argument.");
            return;
        }

        switch (kind)
        {
            case SlotKind.ItemId:
                if (_catalog.KnowsItems && _catalog.ItemName(value) is null)
                    _diagnostics.Error(DiagnosticId.UnknownItem, span, $"There is no item {value}.");
                break;

            case SlotKind.ExchangeId:
                if (_catalog.KnowsExchanges && _catalog.ExchangeSummary(value) is null)
                    _diagnostics.Error(DiagnosticId.UnknownExchange, span, $"There is no exchange {value}.");
                break;

            case SlotKind.NpcId:
                if (_catalog.KnowsNpcs && _catalog.NpcName(value) is null)
                    _diagnostics.Error(DiagnosticId.UnknownNpc, span, $"There is no NPC {value}.");
                break;

            case SlotKind.ZoneId:
                if (_catalog.KnowsZones && _catalog.ZoneName(value) is null)
                    _diagnostics.Error(DiagnosticId.UnknownZone, span, $"Zone {value} is not on this server.");
                break;

            case SlotKind.TalkTextId:
                if (_catalog.KnowsText && value > 0 && _catalog.TalkText(value) is null)
                    _diagnostics.Warning(DiagnosticId.UnknownTalkText, span,
                        $"Dialogue {value} is not in the text table, so the player would see an empty dialog.");
                break;

            case SlotKind.MenuTextId:
                if (_catalog.KnowsText && value > 0 && _catalog.MenuText(value) is null)
                    _diagnostics.Warning(DiagnosticId.UnknownMenuText, span,
                        $"Button label {value} is not in the text table, so the player would see a blank button.");
                break;
        }
    }

    private BoundCondition BindCondition(ConditionSyntax condition) => condition switch
    {
        ConditionSyntax.Or node => new BoundCondition.Or(BindCondition(node.Left), BindCondition(node.Right)),
        ConditionSyntax.And node => new BoundCondition.And(BindCondition(node.Left), BindCondition(node.Right)),
        ConditionSyntax.Not node => new BoundCondition.Not(BindCondition(node.Operand)),
        ConditionSyntax.Predicate node => BindPredicate(node),
        _ => new BoundCondition.AlwaysFalse(),
    };

    private BoundCondition BindPredicate(ConditionSyntax.Predicate node)
    {
        if (node.Signature.Kind == QuestConditionKind.NoTopicFits)
        {
            _diagnostics.Error(DiagnosticId.MisplacedFallback, node.Span,
                "'no topic fits' answers a dialog, so it only follows that dialog's choices.",
                "Move it under the message whose topics it stands in for.");
            return new BoundCondition.AlwaysFalse();
        }

        var values = ResolveSlots(node.Match.Pattern, node.Match.Arguments);
        var comparison = CompareOperator.Equal;
        var negated = false;

        if (node.Match.Arguments.TryGetValue("op", out var op))
        {
            comparison = op.Text switch
            {
                "=" or "==" => CompareOperator.Equal,
                "!=" or "<>" => CompareOperator.NotEqual,
                "<" => CompareOperator.Less,
                "<=" => CompareOperator.LessOrEqual,
                ">" => CompareOperator.Greater,
                ">=" => CompareOperator.GreaterOrEqual,
                _ => CompareOperator.Equal,
            };
        }

        if (node.Signature.Defaults is { } defaults)
        {
            foreach (var pair in defaults)
            {
                if (pair.Key == QuestVocabulary.OperatorArgument)
                {
                    comparison = (CompareOperator)pair.Value;
                    continue;
                }
                if (pair.Key == QuestVocabulary.NegatedArgument)
                {
                    negated = pair.Value != 0;
                    continue;
                }
                values[pair.Key] = pair.Value;
            }
        }

        if (values.GetValueOrDefault("quest") == -2)
        {
            if (_scopeQuest <= 0)
                _diagnostics.Error(DiagnosticId.EntryWithoutQuest, node.Span,
                    "Declare Quest <id> before using 'quest' without an id.");
            values["quest"] = _scopeQuest;
        }
        BoundCondition bound =
            new BoundCondition.Predicate(node.Signature.Kind, comparison, new ArgumentSet(values), node.Span);
        return negated ? new BoundCondition.Not(bound) : bound;
    }

    private string? ClosestEventName(string name)
    {
        if (_eventIds.Count == 0)
            return null;
        var best = _eventIds.Keys.OrderBy(c => LevenshteinDistance(c, name)).First();
        return LevenshteinDistance(best, name) <= Math.Max(2, name.Length / 3) ? best : null;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}
