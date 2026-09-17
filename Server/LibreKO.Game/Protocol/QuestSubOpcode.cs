namespace LibreKO.Game.Protocol;

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
