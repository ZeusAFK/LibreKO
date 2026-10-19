namespace LibreKO.Network;

public enum NpcDialogStyle : byte
{
    Dashboard = 7,
    FamiliarPanel = 9,
    FamiliarShop = 14,
    FortunePanel = 16,
    ItemCombinePanel = 18,
    CombineRecipeBook = 21,
    YutGame = 22,
    DisassemblePanel = 27,
    RepurchaseWindow = 30,
    RebirthPanel = 48,
    SmallJar = 49,
    LargeJar = 50,
    RebirthJar = 51,
    RebirthResetPanel = 52,
    GenderChangePanel = 53,
    DrakiRankBoard = 54,
    FullMoonBoard = 57,
    SpecialAuctionPanel = 58,
    JuraidWarning = 67,
}

public static class NpcQuestWire
{
    public const float InteractRange = 11f;
}

public enum QuestSubOpcode : byte
{
    QuestList = 1,
    StateChange = 2,
    Accept = 3,
    CheckFulfill = 4,
    Abandon = 5,
    AcceptUnlessCompleted = 6,
    NpcEvent = 7,
    Clock = 8,
    KillCounts = 9,
    RewardReceipt = 10,
    TargetDetail = 11,
    AcceptUnlessReadyToTurnIn = 12,
    RewardRefused = 13,
    Objectives = 14,
    Text = 15,
    View = 16,
    NotificationReply = 17,
}

public enum QuestKillCountForm : byte
{
    All = 1,
    Single = 2,
    Batch = 3,
}

public enum QuestRewardRefusal : byte
{
    WeightExceeded = 1,
    CoinsExceeded = 2,
    InventoryFull = 3,
}

public enum InstanceDashboardSubOpcode : uint
{
    PlainEventClock = 8,
    EventClock = 9,
    ResetEventClock = 13,
}
