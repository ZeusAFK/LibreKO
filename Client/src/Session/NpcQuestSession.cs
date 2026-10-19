namespace LibreKO.Session;

public sealed class NpcQuestSession
{
    public int TalkingNpcId { get; private set; } = NoTalker;

    public int LastNpcId { get; private set; }

    public string LastNpcName { get; private set; } = DefaultNpcName;

    public const int NoTalker = -1;

    internal void TalkTo(int npcCharId) => TalkingNpcId = npcCharId;

    internal void EndTalk() => TalkingNpcId = NoTalker;

    internal void RememberNpc(int npcId, string name)
    {
        LastNpcId = npcId;
        LastNpcName = name;
    }

    public const string DefaultNpcName = "Merchant";

    public event Action<NpcDialog>? NpcDialogEvent;

    internal void RaiseNpcDialog(NpcDialog npcDialog) => NpcDialogEvent?.Invoke(npcDialog);

    public event Action<int[]>? NpcSayEvent;

    internal void RaiseNpcSay(int[] value) => NpcSayEvent?.Invoke(value);

    public event Action<List<QuestEntry>>? QuestLogEvent;

    internal void RaiseQuestLog(List<QuestEntry> list) => QuestLogEvent?.Invoke(list);

    public event Action<int, int>? QuestStateEvent;

    internal void RaiseQuestState(int arg1, int arg2) => QuestStateEvent?.Invoke(arg1, arg2);

    public event Action<int, int>? NpcMsgEvent;

    internal void RaiseNpcMsg(int arg1, int arg2) => NpcMsgEvent?.Invoke(arg1, arg2);

    public event Action<int, ushort[]>? QuestKillCountsEvent;

    internal void RaiseQuestKillCounts(int arg1, ushort[] arg2) => QuestKillCountsEvent?.Invoke(arg1, arg2);

    public event Action<QuestObjectives>? QuestObjectivesEvent;

    internal void RaiseQuestObjectives(QuestObjectives questObjectives) => QuestObjectivesEvent?.Invoke(questObjectives);

    public event Action<QuestView>? QuestViewEvent;

    internal void RaiseQuestView(QuestView questView) => QuestViewEvent?.Invoke(questView);

    public event Action<QuestTargetDetail>? QuestTargetEvent;

    internal void RaiseQuestTarget(QuestTargetDetail questTargetDetail) => QuestTargetEvent?.Invoke(questTargetDetail);

    public event Action<QuestReceipt>? QuestReceiptEvent;

    internal void RaiseQuestReceipt(QuestReceipt questReceipt) => QuestReceiptEvent?.Invoke(questReceipt);

    public event Action<List<QuestStrings>>? QuestStringsEvent;

    internal void RaiseQuestStrings(List<QuestStrings> list) => QuestStringsEvent?.Invoke(list);

    public event Action<int, int, int>? QuestKillUpdateEvent;

    internal void RaiseQuestKillUpdate(int arg1, int arg2, int arg3) => QuestKillUpdateEvent?.Invoke(arg1, arg2, arg3);

    public event Action<QuestRewardRefusal>? QuestRewardRefusedEvent;

    internal void RaiseQuestRewardRefused(QuestRewardRefusal questRewardRefusal) => QuestRewardRefusedEvent?.Invoke(questRewardRefusal);

    public event Action<GameOpcodes>? NpcWindowEvent;

    internal void RaiseNpcWindow(GameOpcodes gameOpcodes) => NpcWindowEvent?.Invoke(gameOpcodes);

    public event Action<string[]>? NpcSayTextEvent;

    internal void RaiseNpcSayText(string[] value) => NpcSayTextEvent?.Invoke(value);

    public event Action<int, int>? JuraidWarningEvent;

    internal void RaiseJuraidWarning(int arg1, int arg2) => JuraidWarningEvent?.Invoke(arg1, arg2);
}
