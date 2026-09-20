namespace LibreKO.Quests.Binding;

public enum QuestActionKind
{
    Say,
    ShowQuestPage,
    Button,
    Announce,
    SetQuestState,
    ClaimQuest,
    Goto,
    GiveItem,
    GiveRandomReward,
    TakeItem,
    GiveGold,
    TakeGold,
    GiveExperience,
    GiveNationalPoints,
    TakeNationalPoints,
    GiveCash,
    ChangeJob,
    Exchange,
    QuestExchange,
    ExchangeTimes,
    ExchangeRandom,
    MiningExchange,
    GenieExchange,
    ShowMap,
    Warp,
    Cast,
    Effect,
    NpcEffect,
    DespawnNpc,
    Summon,
    Promote,
    PromoteNovice,
    PromoteClan,
    TakeClanPoints,
    ResetStats,
    ResetSkills,
    OpenStatSkillPanel,
    OpenRenamePanel,
    OpenJobChangePanel,
    OpenClanRenamePanel,
    GivePremium,
    GiveClanPremium,
    GiveAchievement,
    JoinTempleEvent,
    SetLevel,
    SetDrakiRift,
    Todo,
    DoNothing,
    RefuseReward,
}

public enum QuestConditionKind
{
    ItemCount,
    HasItem,
    RoomFor,
    CanReceiveItem,
    CanReceiveStacks,
    PlayerWeight,
    PlayerExperience,
    QuestStatus,
    QuestStatusIn,
    PlayerClass,
    PlayerClassSubtype,
    PlayerNation,
    PlayerLevel,
    PlayerGold,
    PlayerNationalPoints,
    LastStepFailed,
    PlayerZone,
    MonumentNation,
    KillCount,
    HasKillQuest,
    LeadsClan,
    InClan,
    InParty,
    LeadsParty,
    IsKing,
    DailyAvailable,
    HasPremium,
    Chance,
    RollUnder,
    ReachedLevel,
    ClanRank,
    ClanGrade,
    ClanPoints,
    SkillPoints,
    NoTopicFits,
}

public enum SwitchSelectorKind
{
    PlayerClass,
    PlayerNation,
    Event,
    Roll,
    Reward,
}

public enum DialogStyle
{
    Notice = 1,
    Talk = 2,
    Menu = 3,
    QuestOffer = 4,
    RewardPick = 5,
}

public sealed record ActionSignature(
    QuestActionKind Kind,
    PhrasePattern Pattern,
    IReadOnlyDictionary<string, long>? Defaults = null)
{
    public string Text => Pattern.Text;
}

public sealed record ConditionSignature(
    QuestConditionKind Kind,
    PhrasePattern Pattern,
    IReadOnlyDictionary<string, long>? Defaults = null)
{
    public string Text => Pattern.Text;
}

public static class QuestVocabulary
{
    public const int ClassGroupWarrior = 1;
    public const int ClassGroupRogue = 2;
    public const int ClassGroupMage = 3;
    public const int ClassGroupPriest = 4;
    public const int ClassGroupKurian = 13;

    public const int MaxDialogButtons = 256;
    public const int HoursPerDay = 24;

    public const string OperatorArgument = "$op";
    public const string NegatedArgument = "$negated";
    public const string CloseTarget = "close";

    public static IReadOnlyList<ActionSignature> Actions { get; } = BuildActions();

    public static IReadOnlyList<ConditionSignature> Conditions { get; } = BuildConditions().Concat(BuildConditions()
        .Where(c => c.Text.Contains("quest {quest:QuestId}"))
        .Select(c => new ConditionSignature(c.Kind,
            PhrasePattern.Parse(c.Text.Replace("quest {quest:QuestId}", "quest")),
            new Dictionary<string, long>(c.Defaults ?? new Dictionary<string, long>()) { ["quest"] = -2 }))).ToArray();

    public static IReadOnlyDictionary<string, int> ClassGroups { get; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["warrior"] = ClassGroupWarrior,
            ["rogue"] = ClassGroupRogue,
            ["mage"] = ClassGroupMage,
            ["priest"] = ClassGroupPriest,
            ["kurian"] = ClassGroupKurian,
        };

    public static IReadOnlyDictionary<string, int> PremiumTypes { get; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["bronze"] = 3,
            ["gold"] = 5,
            ["platinum"] = 7,
            ["dc"] = 10,
            ["exp"] = 11,
            ["war"] = 12,
            ["switch"] = 13,
        };

    public static IReadOnlyDictionary<string, int> Nations { get; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["karus"] = 1,
            ["elmorad"] = 2,
        };

    public static IReadOnlyList<string> ClassGroupNames { get; } =
        ["Warrior", "Rogue", "Mage", "Priest", "Kurian"];

    public static IReadOnlyList<string> NationNames { get; } = ["Karus", "ElMorad"];

    public static IReadOnlyDictionary<string, int> ClanRanks { get; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["none"] = 0,
            ["training"] = 1,
            ["promoted"] = 2,
            ["accredited5"] = 3,
            ["accredited4"] = 4,
            ["accredited3"] = 5,
            ["accredited2"] = 6,
            ["accredited1"] = 7,
            ["royal5"] = 8,
            ["royal4"] = 9,
            ["royal3"] = 10,
            ["royal2"] = 11,
            ["royal1"] = 12,
        };

    public static IReadOnlyList<string> ClanRankNames { get; } =
    [
        "None", "Training", "Promoted",
        "Accredited5", "Accredited4", "Accredited3", "Accredited2", "Accredited1",
        "Royal5", "Royal4", "Royal3", "Royal2", "Royal1",
    ];

    private static IReadOnlyList<ActionSignature> BuildActions() =>
    [
        Action(QuestActionKind.Say, "Say {text:TalkTextId}", Style(DialogStyle.Talk)),
        Action(QuestActionKind.Say, "Say {text:TalkTextId} notice", Style(DialogStyle.Notice)),
        Action(QuestActionKind.Say, "Say {text:TalkTextId} menu", Style(DialogStyle.Menu)),
        Action(QuestActionKind.Say, "Say {text:TalkTextId} offer", Style(DialogStyle.QuestOffer)),
        Action(QuestActionKind.Say, "Say {text:TalkTextId} flag {style:Int}"),
        Action(QuestActionKind.ShowQuestPage, "Show quest {text:TalkTextId}", Style(DialogStyle.QuestOffer)),

        Action(QuestActionKind.Button, "Topic {label:MenuTextId} [reward {award:Int}] goto {target:EventRef}"),
        Action(QuestActionKind.Button, "Topic {label:MenuTextId} [reward {award:Int}] do"),
        Action(QuestActionKind.Todo, "Todo {note:TalkTextId}"),
        Action(QuestActionKind.DoNothing, "Do nothing"),
        Action(QuestActionKind.RefuseReward, "Refuse reward"),
        Action(QuestActionKind.Announce,
            "Announce {text:TalkTextId} [{text2:TalkTextId}] [{text3:TalkTextId}] [{text4:TalkTextId}]"),

        Action(QuestActionKind.SetQuestState, "Complete {quest:QuestId}",
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase) { ["status"] = 2 }),
        Action(QuestActionKind.ClaimQuest, "Claim {quest:QuestId}"),
        Action(QuestActionKind.SetQuestState, "Start {quest:QuestId}",
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase) { ["status"] = 1 }),
        Action(QuestActionKind.SetQuestState, "Fulfil {quest:QuestId}",
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase) { ["status"] = 3 }),
        Action(QuestActionKind.SetQuestState, "Abandon {quest:QuestId}",
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase) { ["status"] = 4 }),
        Action(QuestActionKind.SetQuestState, "Clear {quest:QuestId}",
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase) { ["status"] = 0 }),
        Action(QuestActionKind.SetQuestState, "Set {quest:QuestId} to {status:Count}"),
        Action(QuestActionKind.Goto, "Goto {target:EventRef}"),

        Action(QuestActionKind.GiveItem, "Give {item:ItemId} [for {days:Count} days]"),
        Action(QuestActionKind.GiveRandomReward, "Give random from {reward:RewardId}"),
        Action(QuestActionKind.GiveItem, "Give {count:Count} of {item:ItemId} [for {days:Count} days]"),
        Action(QuestActionKind.GiveItem, "Give {item:ItemId} for {hours:Count} hours"),
        Action(QuestActionKind.GiveItem, "Give {count:Count} of {item:ItemId} for {hours:Count} hours"),
        Action(QuestActionKind.TakeItem, "Take {item:ItemId}"),
        Action(QuestActionKind.TakeItem, "Take {count:Count} of {item:ItemId}"),

        Action(QuestActionKind.GiveGold, "Give {amount:Count} coins"),
        Action(QuestActionKind.TakeGold, "Take {amount:Count} coins"),
        Action(QuestActionKind.GiveExperience, "Give {amount:Count} experience or {premium:Count} with premium"),
        Action(QuestActionKind.GiveExperience, "Give {amount:Count} experience"),
        Action(QuestActionKind.GiveNationalPoints, "Give {amount:Count} np"),
        Action(QuestActionKind.TakeNationalPoints, "Take {amount:Count} np"),

        Action(QuestActionKind.GiveCash, "Give {amount:Count} cash"),
        Action(QuestActionKind.GivePremium, "Give premium {type:Count} for {days:Count} days"),
        Action(QuestActionKind.GivePremium, "Give {type:PremiumType} premium for {days:Count} days"),
        Action(QuestActionKind.GiveClanPremium, "Give clan premium for {days:Count} days"),
        Action(QuestActionKind.ChangeJob, "Change job to {class:ClassGroup}", Mastered(1)),
        Action(QuestActionKind.ChangeJob, "Change job to {class:ClassGroup} unmastered", Mastered(0)),
        Action(QuestActionKind.Exchange, "Exchange {exchange:ExchangeId}"),
        Action(QuestActionKind.QuestExchange, "Exchange {exchange:ExchangeId} for quest [reward {award:Int}]"),
        Action(QuestActionKind.ExchangeTimes, "Exchange {exchange:ExchangeId} x {count:Count}"),
        Action(QuestActionKind.ExchangeRandom, "Exchange {first:ExchangeId} to {last:ExchangeId} random"),
        Action(QuestActionKind.MiningExchange, "Exchange mining {ore:Count}"),
        Action(QuestActionKind.GenieExchange,
            "Exchange {item:ItemId} for {hours:Count} hours of genie"),

        Action(QuestActionKind.ShowMap, "Map {map:MapId}"),
        Action(QuestActionKind.Warp, "Teleport {zone:ZoneId} [at {x:Int} {z:Int}]"),
        Action(QuestActionKind.Cast, "Cast {skill:SkillId}"),
        Action(QuestActionKind.Effect, "Effect {effect:EffectId}"),
        Action(QuestActionKind.NpcEffect, "Effect {effect:EffectId} on npc"),
        Action(QuestActionKind.DespawnNpc, "Despawn npc"),
        Action(QuestActionKind.Summon, "Summon {count:Count} of {npc:NpcId} [at {x:Int} {z:Int}]"),
        Action(QuestActionKind.Promote, "Promote"),
        Action(QuestActionKind.PromoteNovice, "Promote to novice"),
        Action(QuestActionKind.PromoteClan, "Promote clan to {rank:ClanRank}"),
        Action(QuestActionKind.TakeClanPoints, "Take {amount:Count} clan points"),
        Action(QuestActionKind.ResetStats, "Reset stats"),
        Action(QuestActionKind.ResetSkills, "Reset skills"),
        Action(QuestActionKind.OpenStatSkillPanel, "Open stats panel"),
        Action(QuestActionKind.OpenRenamePanel, "Open rename panel"),
        Action(QuestActionKind.OpenJobChangePanel, "Open job change panel"),
        Action(QuestActionKind.OpenClanRenamePanel, "Open clan rename panel"),
        Action(QuestActionKind.GiveAchievement, "Give achievement {achievement:Count}"),
        Action(QuestActionKind.JoinTempleEvent, "Join temple event"),
        Action(QuestActionKind.SetLevel, "Set player level to {level:Count}"),
        Action(QuestActionKind.SetDrakiRift,
            "Set draki rift stage {stage:Count} substage {substage:Count}"),
    ];

    private static IReadOnlyList<ConditionSignature> BuildConditions() =>
    [
        Condition(QuestConditionKind.ItemCount, "player has {op:CompareOp} {count:Count} of {item:ItemId}"),
        Condition(QuestConditionKind.HasItem, "player has {item:ItemId}"),
        Condition(QuestConditionKind.HasItem, "player lacks {item:ItemId}", Negated()),
        Condition(QuestConditionKind.RoomFor, "player space {op:CompareOp} {count:Count}"),
        Condition(QuestConditionKind.CanReceiveItem, "player can receive {count:Count} of {item:ItemId}"),
        Condition(QuestConditionKind.CanReceiveStacks, "player can receive {count:Count} stacks"),
        Condition(QuestConditionKind.PlayerWeight, "player weight {op:CompareOp} {value:Count}"),
        Condition(QuestConditionKind.PlayerExperience, "player experience {op:CompareOp} {value:Count}"),

        Condition(QuestConditionKind.QuestStatus, "quest {quest:QuestId} is completed", Status(2)),
        Condition(QuestConditionKind.QuestStatus, "quest {quest:QuestId} is started", Status(1)),
        Condition(QuestConditionKind.QuestStatus, "quest {quest:QuestId} is unstarted", Status(0)),
        Condition(QuestConditionKind.QuestStatus, "quest {quest:QuestId} is fulfilled", Status(3)),
        Condition(QuestConditionKind.QuestStatus, "quest {quest:QuestId} is abandoned", Status(4)),
        Condition(QuestConditionKind.QuestStatus, "quest {quest:QuestId} is at {status:Count}"),

        Condition(QuestConditionKind.QuestStatusIn, "quest {quest:QuestId} is available", Mask(0, 4)),
        Condition(QuestConditionKind.QuestStatusIn, "quest {quest:QuestId} is active", Mask(1, 3)),

        Condition(QuestConditionKind.PlayerClass, "player is {class:ClassGroup}"),
        Condition(QuestConditionKind.PlayerClass, "player is not {class:ClassGroup}", Negated()),
        Condition(QuestConditionKind.PlayerClassSubtype, "player class subtype {op:CompareOp} {value:Int}"),
        Condition(QuestConditionKind.PlayerNation, "player is {nation:Nation}"),
        Condition(QuestConditionKind.PlayerNation, "player is not {nation:Nation}", Negated()),

        Condition(QuestConditionKind.PlayerLevel, "player level {op:CompareOp} {value:Count}"),
        Condition(QuestConditionKind.PlayerGold, "player gold {op:CompareOp} {amount:Count}"),
        Condition(QuestConditionKind.PlayerNationalPoints, "player np {op:CompareOp} {amount:Count}"),
        Condition(QuestConditionKind.HasPremium, "player premium {op:CompareOp} {value:Count}"),
        Condition(QuestConditionKind.LastStepFailed, "the last step failed"),
        Condition(QuestConditionKind.LastStepFailed, "the last step worked", Negated()),
        Condition(QuestConditionKind.PlayerZone, "player in zone {zone:ZoneId}"),
        Condition(QuestConditionKind.PlayerZone, "player not in zone {zone:ZoneId}", Negated()),
        Condition(QuestConditionKind.MonumentNation, "monument is {nation:Nation}"),
        Condition(QuestConditionKind.MonumentNation, "monument is not {nation:Nation}", Negated()),

        Condition(QuestConditionKind.KillCount,
            "player kills {op:CompareOp} {count:Count} in group {group:KillGroup} of quest {quest:QuestId}"),
        Condition(QuestConditionKind.KillCount,
            "player kills {op:CompareOp} {count:Count} in all of quest {quest:QuestId}", Group(0)),
        Condition(QuestConditionKind.HasKillQuest, "player has a kill quest"),

        Condition(QuestConditionKind.ClanRank, "player clan is {rank:ClanRank}"),
        Condition(QuestConditionKind.ClanRank, "player clan is not {rank:ClanRank}", Negated()),
        Condition(QuestConditionKind.ClanRank, "player clan rank {op:CompareOp} {rank:ClanRank}"),
        Condition(QuestConditionKind.ClanGrade, "player clan grade {op:CompareOp} {value:Count}"),
        Condition(QuestConditionKind.ClanPoints, "player clan points {op:CompareOp} {amount:Count}"),
        Condition(QuestConditionKind.SkillPoints,
            "player skill points in tree {tree:Count} {op:CompareOp} {amount:Count}"),
        Condition(QuestConditionKind.LeadsClan, "player leads a clan"),
        Condition(QuestConditionKind.InClan, "player in a clan"),
        Condition(QuestConditionKind.InParty, "player in a party"),
        Condition(QuestConditionKind.LeadsParty, "player leads a party"),
        Condition(QuestConditionKind.IsKing, "player is king"),
        Condition(QuestConditionKind.DailyAvailable, "daily {slot:Count} available"),
        Condition(QuestConditionKind.Chance, "chance {percent:Count}"),
        Condition(QuestConditionKind.RollUnder, "roll of {max:Count} {op:CompareOp} {value:Count}"),
        Condition(QuestConditionKind.ReachedLevel,
            "player reached level {level:Count} at {percent:Count} percent"),
        Condition(QuestConditionKind.NoTopicFits, "no topic fits"),
    ];

    private static Dictionary<string, long> Negated() =>
        new(StringComparer.OrdinalIgnoreCase) { [NegatedArgument] = 1 };

    private static Dictionary<string, long> Group(long group) =>
        new(StringComparer.OrdinalIgnoreCase) { ["group"] = group };

    private static Dictionary<string, long> Mastered(long mastered) =>
        new(StringComparer.OrdinalIgnoreCase) { ["mastered"] = mastered };

    private static Dictionary<string, long> Status(long status) =>
        new(StringComparer.OrdinalIgnoreCase) { ["status"] = status };

    private static Dictionary<string, long> Mask(params int[] statuses)
    {
        long mask = 0;
        foreach (var status in statuses)
            mask |= 1L << status;
        return new(StringComparer.OrdinalIgnoreCase) { ["states"] = mask };
    }

    private static Dictionary<string, long> Style(DialogStyle style) =>
        new(StringComparer.OrdinalIgnoreCase) { ["style"] = (long)style };

    private static ActionSignature Action(
        QuestActionKind kind, string pattern, IReadOnlyDictionary<string, long>? defaults = null) =>
        new(kind, PhrasePattern.Parse(pattern), defaults);

    private static ConditionSignature Condition(
        QuestConditionKind kind, string pattern, IReadOnlyDictionary<string, long>? defaults = null) =>
        new(kind, PhrasePattern.Parse(pattern), defaults);
}
