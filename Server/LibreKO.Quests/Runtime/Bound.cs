using LibreKO.Quests.Binding;
using LibreKO.Quests.Text;

namespace LibreKO.Quests.Runtime;

public sealed class ArgumentSet
{
    public static readonly ArgumentSet Empty = new(new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase));

    private readonly IReadOnlyDictionary<string, long> _values;

    public ArgumentSet(IReadOnlyDictionary<string, long> values) => _values = values;

    public IReadOnlyDictionary<string, long> Values => _values;

    public bool Has(string name) => _values.ContainsKey(name);

    public long Get(string name, long fallback = 0) =>
        _values.TryGetValue(name, out var value) ? value : fallback;

    public int GetInt(string name, int fallback = 0) => (int)Get(name, fallback);
}

public enum CompareOperator
{
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
}

public abstract record BoundCondition
{
    public sealed record ViewState(int QuestId, QuestViewState State) : BoundCondition;

    public sealed record Or(BoundCondition Left, BoundCondition Right) : BoundCondition;

    public sealed record And(BoundCondition Left, BoundCondition Right) : BoundCondition;

    public sealed record Not(BoundCondition Operand) : BoundCondition;

    public sealed record Predicate(
        QuestConditionKind Kind,
        CompareOperator Operator,
        ArgumentSet Arguments,
        TextSpan Span) : BoundCondition;

    public sealed record AlwaysFalse : BoundCondition;
}

public sealed record DialogLine(int TextId, string? Text)
{
    public bool HasText => Text is { Length: > 0 };

    public static DialogLine FromId(int textId) => new(textId, null);

    public static DialogLine FromText(string text) => new(-1, text);

    public static readonly DialogLine None = new(-1, null);
}

public sealed record DialogButton(DialogLine Label, int TargetEvent, int SelectedReward = -1);

public sealed record DialogChoice(BoundCondition? When, DialogButton Button, int QuestId = 0);

public enum QuestPageKind : byte
{
    Conversation,
    Quest,
}

public abstract record BoundStatement(TextSpan Span)
{
    public sealed record View(TextSpan Span, int QuestId, bool Silent = false) : BoundStatement(Span);

    public sealed record Reward(
        TextSpan Span,
        IReadOnlyList<BoundStatement> Body,
        IReadOnlyList<BoundStatement>? ElseBody = null) : BoundStatement(Span);

    public sealed record Dialog(
        TextSpan Span,
        DialogStyle Style,
        int QuestId,
        DialogLine Header,
        IReadOnlyList<DialogChoice> Choices,
        IReadOnlyList<BoundStatement>? Fallback = null,
        QuestPageKind Page = QuestPageKind.Conversation) : BoundStatement(Span);

    public sealed record Say(TextSpan Span, IReadOnlyList<DialogLine> Lines) : BoundStatement(Span);

    public sealed record Action(
        TextSpan Span,
        QuestActionKind Kind,
        ArgumentSet Arguments) : BoundStatement(Span);

    public sealed record Goto(TextSpan Span, int TargetEvent) : BoundStatement(Span);

    public sealed record If(
        TextSpan Span,
        IReadOnlyList<(BoundCondition Condition, IReadOnlyList<BoundStatement> Body)> Arms,
        IReadOnlyList<BoundStatement>? ElseBody) : BoundStatement(Span);

    public sealed record Switch(
        TextSpan Span,
        SwitchSelectorKind Selector,
        IReadOnlyList<(IReadOnlyList<long> Labels, IReadOnlyList<BoundStatement> Body)> Cases,
        IReadOnlyList<BoundStatement>? DefaultBody,
        int Max = 0) : BoundStatement(Span);
}

public sealed record ResolvedId(TextSpan Span, Binding.SlotKind Kind, long Value);

public sealed record NamedReference(
    TextSpan Span, Binding.SlotKind Kind, string Name, string? Detail);

public sealed record QuestLocation(
    string Name, string Title, string? Where, long? X, long? Y, long? Zone,
    string? About = null, long? ElMoradX = null, long? ElMoradY = null);

public sealed record RewardChoice(int Item, int Count, long Weight);

public sealed record RewardDefinition(string Name, IReadOnlyList<IReadOnlyList<RewardChoice>> Rows);

public sealed record TranslatableText(TextSpan Span, string Text, Binding.SlotKind Kind);

public sealed record KillObjective(int Count, IReadOnlyList<int> Monsters, int Target = -1);

public enum ObjectiveRule
{
    All,
    Any,
}

public sealed record QuestObjectives(
    int QuestId,
    IReadOnlyList<KillObjective> Groups,
    ObjectiveRule Rule = ObjectiveRule.All);

public sealed record QuestBinding(int NpcId, int ZoneId, int Nation, int ClassGroup = 0);

public sealed record QuestText(int QuestId, string? Title, string? Journal, bool Daily = false,
    int Nation = 0, int ClassGroup = 0, bool Repeat = false, bool FulfilElsewhere = false);

public sealed record QuestRewards(int QuestId, IReadOnlyList<BoundStatement.Action> Transfers,
    int ClassGroup = 0)
{
    public IReadOnlyList<BoundStatement.Action> Options { get; init; } = [];
}

public sealed record QuestFlow(int QuestId, BoundCondition? Requires, int ZoneId, bool AutoAccept = false, bool AutoComplete = false)
{
    public IReadOnlyList<QuestBinding> Bindings { get; init; } = [];

    public QuestBinding? BindingFor(int nation, int zone, int classGroup = 0) => Bindings
        .Where(b => (b.Nation == 0 || b.Nation == nation)
            && (b.ClassGroup == 0 || classGroup == 0 || b.ClassGroup == classGroup))
        .OrderByDescending(b => b.ZoneId == zone).ThenByDescending(b => b.Nation == nation)
        .ThenByDescending(b => b.ClassGroup == classGroup)
        .FirstOrDefault();

    public bool Matches(int nation, int zone, int classGroup = 0) => Bindings.Count == 0
        ? ZoneId == 0 || ZoneId == zone
        : Bindings.Any(b => (b.Nation == 0 || b.Nation == nation) && (b.ZoneId == 0 || b.ZoneId == zone)
            && (b.ClassGroup == 0 || classGroup == 0 || b.ClassGroup == classGroup));
}

public enum QuestViewState : byte
{
    Locked,
    Available,
    InProgress,
    Claimable,
    Completed,
}

public sealed record QuestView(QuestText Text, int ZoneId, QuestViewState State, QuestObjectives? Objectives,
    IReadOnlyList<int> Counts, QuestRewards Rewards, DialogLine Dialogue, IReadOnlyList<DialogButton> Topics,
    QuestPageKind Page = QuestPageKind.Quest, bool AutoAccepted = false);

public sealed record QuestReceipt(int QuestId, IReadOnlyList<(int ItemId, int Count)> Granted);

public sealed record QuestEventEntry(
    int Id,
    string? Name,
    TextSpan Span,
    IReadOnlyList<BoundStatement> Body);

public sealed class QuestProgram
{
    public const int LocalEventBase = 0x40000000;
    public const string TopicsEvent = "topics";
    public const string GreetingEvent = "greeting";
    public const string ZoneEntryEvent = "zone_entry";
    public const string AcceptEvent = "accept";
    public const string FulfilEvent = "fulfil";
    public const string AbandonEvent = "abandon";
    public const string ViewEvent = "view";
    public const string ReadyEvent = "ready";
    public static readonly IReadOnlySet<string> StateEvents =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "available", "offer", "in_progress", "claimable", "completed", "started", ReadyEvent };

    public static readonly IReadOnlySet<string> NotificationEvents =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "available", "started", ReadyEvent };

    public static readonly IReadOnlySet<string> ScriptEntries =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { GreetingEvent, TopicsEvent, ZoneEntryEvent };

    public static readonly IReadOnlySet<string> QuestEntries =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { AcceptEvent, FulfilEvent, AbandonEvent, ViewEvent, "available", "offer", "in_progress", "claimable",
          "completed", "started", ReadyEvent };

    public static bool IsEntry(string name) =>
        ScriptEntries.Contains(name) || QuestEntries.Contains(name);

    public static bool IsEntryName(string name)
    {
        if (ScriptEntries.Contains(name))
            return true;
        var suffix = name.LastIndexOf('_');
        return suffix > 0 && QuestEntries.Contains(name[..suffix]) && int.TryParse(name[(suffix + 1)..], out _);
    }

    public static string EntryName(string role, int questId) =>
        QuestEntries.Contains(role) ? $"{role}_{questId}" : role;

    public IReadOnlyList<BorrowedEvent> Borrowed { get; init; } = [];
    public IReadOnlyList<QuestRewards> QuestRewards { get; init; } = [];
    public IReadOnlyList<QuestFlow> Flows { get; init; } = [];
    public string FileName { get; }
    public int NpcId { get; }
    public IReadOnlyList<int> NpcIds { get; }
    public string? NpcName { get; }
    public SourceText Source { get; }
    public IReadOnlyDictionary<int, QuestEventEntry> Events { get; }
    public IReadOnlyDictionary<string, int> EventNames { get; }
    public IReadOnlyList<QuestLocation> Locations { get; }
    public IReadOnlyList<RewardDefinition> Rewards { get; }
    public IReadOnlyList<QuestObjectives> Objectives { get; }
    public IReadOnlyList<QuestText> Texts { get; }
    public int ZoneId { get; }
    public int DefaultQuestId { get; }
    public bool HasBinding { get; }
    public IReadOnlyList<QuestBinding> Bindings { get; init; } = [];
    public IReadOnlyList<DialogChoice> GreetingTopics { get; init; } = [];
    public IReadOnlyList<DialogChoice> AutomaticTopics { get; init; } = [];
    public IReadOnlyList<BoundStatement>? GreetingFallback { get; init; }

    public QuestProgram(
        string fileName,
        int npcId,
        string? npcName,
        SourceText source,
        IReadOnlyDictionary<int, QuestEventEntry> events,
        IReadOnlyDictionary<string, int> eventNames,
        IReadOnlyList<QuestLocation>? locations = null,
        int zoneId = 0,
        IReadOnlyList<QuestObjectives>? objectives = null,
        IReadOnlyList<int>? npcIds = null,
        IReadOnlyList<QuestText>? texts = null,
        int defaultQuestId = -1,
        bool hasBinding = false,
        IReadOnlyList<RewardDefinition>? rewards = null)
    {
        FileName = fileName;
        NpcId = npcId;
        NpcIds = npcIds is { Count: > 0 } ? npcIds : (npcId != 0 ? [npcId] : []);
        NpcName = npcName;
        Source = source;
        Events = events;
        EventNames = eventNames;
        Locations = locations ?? [];
        Rewards = rewards ?? [];
        Objectives = objectives ?? [];
        Texts = texts ?? [];
        ZoneId = zoneId;
        DefaultQuestId = defaultQuestId;
        HasBinding = hasBinding;
    }

    public QuestText? TextFor(int questId, int nation, int classGroup = 0)
    {
        var candidates = Texts.Where(t => t.QuestId == questId
            && (t.ClassGroup == 0 || classGroup == 0 || t.ClassGroup == classGroup)).ToList();
        return candidates.FirstOrDefault(t => t.Nation == nation && t.ClassGroup == classGroup)
            ?? candidates.FirstOrDefault(t => t.ClassGroup == classGroup && classGroup != 0)
            ?? candidates.FirstOrDefault(t => t.Nation == nation)
            ?? candidates.FirstOrDefault(t => t.Nation == 0)
            ?? Texts.FirstOrDefault(t => t.QuestId == questId);
    }

    public QuestRewards? RewardsFor(int questId, int classGroup = 0)
    {
        var candidates = QuestRewards.Where(r => r.QuestId == questId).ToList();
        return candidates.FirstOrDefault(r => r.ClassGroup == classGroup && classGroup != 0)
            ?? candidates.FirstOrDefault(r => r.ClassGroup == 0)
            ?? candidates.FirstOrDefault();
    }

    public bool TryGetReward(int index, out RewardDefinition reward)
    {
        if (index >= 0 && index < Rewards.Count)
        {
            reward = Rewards[index];
            return true;
        }
        reward = null!;
        return false;
    }

    public bool TryGetLocation(int index, out QuestLocation location)
    {
        if (index >= 0 && index < Locations.Count)
        {
            location = Locations[index];
            return true;
        }
        location = null!;
        return false;
    }

    public bool TryGetEvent(int eventId, out QuestEventEntry entry) => Events.TryGetValue(eventId, out entry!);

    public bool TryGetEntry(string role, int questId, out int eventId) =>
        EventNames.TryGetValue(EntryName(role, questId), out eventId);

    public bool TryGetGreeting(out int eventId) => TryGetEntry(GreetingEvent, 0, out eventId);
}
