namespace LibreKO.Network.Readers;

internal sealed class NpcQuestReader : IPacketReader
{
    private readonly ChaosSession chaos;
    private readonly NpcQuestSession npcQuest;
    private readonly QuestBook questBook;

    private const int DashboardSubOffsetBytes = 8;

    public NpcQuestReader(ChaosSession chaos, NpcQuestSession npcQuest, QuestBook questBook)
    {
        this.chaos = chaos;
        this.npcQuest = npcQuest;
        this.questBook = questBook;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_SELECT_MSG, HandleSelectMsg);
        dispatcher.On(GameOpcodes.GS_NPC_SAY, HandleNpcSay);
        dispatcher.On(GameOpcodes.GS_QUEST, HandleQuest);
    }

    private bool TryHandleJuraidWarning(Packet p)
    {
        if (p.RemainingBytes < 13) return false;
        p.ReadInt();
        if ((NpcDialogStyle)p.ReadByte() != NpcDialogStyle.JuraidWarning)
        {
            p.ResetOffset();
            return false;
        }
        npcQuest.RaiseJuraidWarning(p.ReadInt(), p.ReadInt());
        return true;
    }

    private void HandleSelectMsg(Packet p)
    {
        if (TryHandleDashboard(p)) return;
        if (TryHandleJuraidWarning(p)) return;
        if (p.RemainingBytes < NpcDialog.MinimumBytes) return;
        try
        {
            npcQuest.RaiseNpcDialog(NpcDialog.Read(p));
        }
        catch (Exception e)
        {
            Diag.Report("dialog text parse", e);
        }
    }

    private static List<string> ReadDialogText(Packet p, out string? headerText)
    {
        try
        {
            return NpcDialog.ReadText(p, false, out headerText);
        }
        catch (Exception e)
        {
            Diag.Report("dialog text parse", e);
            headerText = null;
            return [];
        }
    }

    private void HandleNpcSay(Packet p)
    {
        if (p.RemainingBytes >= 4) p.ReadInt();
        if (p.RemainingBytes >= 4) p.ReadInt();
        var ids = new List<int>(8);
        for (int i = 0; i < 8 && p.RemainingBytes >= 4; i++)
        {
            int id = p.ReadInt();
            if (id >= 0) ids.Add(id);
        }
        var spoken = ReadDialogText(p, out _);
        if (spoken.Count > 0)
        {
            npcQuest.RaiseNpcSayText([.. spoken.Where(line => line.Length > 0)]);
            return;
        }
        if (ids.Count > 0) npcQuest.RaiseNpcSay(ids.ToArray());
    }

    private void HandleQuest(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (QuestSubOpcode)p.ReadByte();
        switch (sub)
        {
            case QuestSubOpcode.QuestList:
                {
                    if (p.RemainingBytes < 2) return;
                    int count = p.ReadShort();
                    var list = new List<QuestEntry>(count);
                    for (int i = 0; i < count && p.RemainingBytes >= 3; i++)
                    {
                        int questId = p.ReadShort();
                        int state = p.ReadByte();
                        list.Add(new QuestEntry(questId, state));
                    }
                    questBook.ApplyLog(list);
                    npcQuest.RaiseQuestLog(list);
                    break;
                }
            case QuestSubOpcode.StateChange:
                {
                    if (p.RemainingBytes < 3) return;
                    int questId = p.ReadShort();
                    int state = p.ReadByte();
                    questBook.ApplyState(questId, state);
                    npcQuest.RaiseQuestState(questId, state);
                    break;
                }
            case QuestSubOpcode.NpcEvent:
                {
                    if (p.RemainingBytes < 6) return;
                    int textId = p.ReadInt();
                    int npcId = p.ReadShort();
                    npcQuest.RaiseNpcMsg(textId, npcId);
                    break;
                }
            case QuestSubOpcode.Clock:
                break;
            case QuestSubOpcode.Objectives:
                {
                    var objectives = QuestObjectives.Read(p);
                    questBook.ApplyObjectives(objectives);
                    npcQuest.RaiseQuestObjectives(objectives);
                    break;
                }
            case QuestSubOpcode.TargetDetail:
                npcQuest.RaiseQuestTarget(QuestTargetDetail.Read(p));
                break;
            case QuestSubOpcode.RewardReceipt:
                {
                    var receipt = QuestReceipt.Read(p);
                    questBook.RewardSelection.TurnedIn(receipt.QuestId);
                    npcQuest.RaiseQuestReceipt(receipt);
                    break;
                }
            case QuestSubOpcode.View:
                {
                    var view = QuestView.Read(p);
                    questBook.ApplyView(view);
                    npcQuest.RaiseQuestView(view);
                    break;
                }
            case QuestSubOpcode.Text:
                {
                    var texts = QuestStrings.ReadList(p);
                    questBook.ApplyStrings(texts);
                    npcQuest.RaiseQuestStrings(texts);
                    break;
                }
            case QuestSubOpcode.RewardRefused:
                {
                    if (p.RemainingBytes < 1) return;
                    npcQuest.RaiseQuestRewardRefused((QuestRewardRefusal)p.ReadByte());
                    break;
                }
            case QuestSubOpcode.KillCounts:
                {
                    if (p.RemainingBytes < 1) return;
                    var form = (QuestKillCountForm)p.ReadByte();
                    if (form == QuestKillCountForm.All)
                    {
                        if (p.RemainingBytes < 2) return;
                        int questId = p.ReadShort();
                        var counts = new ushort[QuestBook.KillGroupCount];
                        for (int i = 0; i < counts.Length; i++)
                            counts[i] = p.RemainingBytes >= 2 ? (ushort)p.ReadShort() : (ushort)0;
                        questBook.ApplyKillCounts(questId, counts);
                        npcQuest.RaiseQuestKillCounts(questId, counts);
                    }
                    else if (form == QuestKillCountForm.Single)
                    {
                        if (p.RemainingBytes < 5) return;
                        int questId = p.ReadShort();
                        int group = p.ReadByte();
                        int cnt = (ushort)p.ReadShort();
                        questBook.ApplyKillUpdate(questId, group, cnt);
                        npcQuest.RaiseQuestKillUpdate(questId, group, cnt);
                    }
                    break;
                }
        }
    }

    private bool TryHandleDashboard(Packet p)
    {
        if (p.RemainingBytes < 5) return false;
        p.ReadInt();
        var style = (NpcDialogStyle)p.ReadByte();
        if (style != NpcDialogStyle.Dashboard)
        {
            p.ResetOffset();
            return false;
        }
        if (p.RemainingBytes < DashboardSubOffsetBytes + 4) return true;
        p.ReadBytes(DashboardSubOffsetBytes);
        var sub = (InstanceDashboardSubOpcode)p.ReadUInt();
        if (sub is InstanceDashboardSubOpcode.PlainEventClock or InstanceDashboardSubOpcode.EventClock or InstanceDashboardSubOpcode.ResetEventClock && p.RemainingBytes >= 1)
            chaos.ApplyDashboardEvent(p.ReadByte());
        return true;
    }
}
