using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public enum QuestSub : byte
{
    QuestList = 1,
    StateChange = 2,
    NpcEvent = 7,
    Clock = 8,
    KillCounts = 9,
    RewardReceipt = 10,
    TargetDetail = 11,
    RewardRefused = 13,
    Objectives = 14,
    Text = 15,
    View = 16,
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

public partial class Net
{
    public const float NpcInteractRange = 11f;

    public event Action<NpcDialog>? NpcDialogEvent;

    public event Action<int[]>? NpcSayEvent;

    public event Action<List<QuestEntry>>? QuestLogEvent;

    public event Action<int, int>? QuestStateEvent;

    public event Action<int, int>? NpcMsgEvent;

    public event Action<int, ushort[]>? QuestKillCountsEvent;
    public event Action<QuestObjectives>? QuestObjectivesEvent;
    public event Action<QuestView>? QuestViewEvent;
    public event Action<QuestTargetDetail>? QuestTargetEvent;
    public event Action<QuestReceipt>? QuestReceiptEvent;
    public event Action<List<QuestStrings>>? QuestStringsEvent;

    public event Action<int, int, int>? QuestKillUpdateEvent;

    public event Action<QuestRewardRefusal>? QuestRewardRefusedEvent;

    public event Action<GameOpcodes>? NpcWindowEvent;
    public event Action<string[]>? NpcSayTextEvent;

    private void HandleSelectMsg(Packet p)
    {
        try
        {
            NpcDialogEvent?.Invoke(NpcDialog.Read(p));
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
            NpcSayTextEvent?.Invoke([.. spoken.Where(line => line.Length > 0)]);
            return;
        }
        if (ids.Count > 0) NpcSayEvent?.Invoke(ids.ToArray());
    }

    private void HandleQuest(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (QuestSub)p.ReadByte();
        switch (sub)
        {
            case QuestSub.QuestList:
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
                QuestLogEvent?.Invoke(list);
                break;
            }
            case QuestSub.StateChange:
            {
                if (p.RemainingBytes < 3) return;
                int questId = p.ReadShort();
                int state = p.ReadByte();
                QuestStateEvent?.Invoke(questId, state);
                break;
            }
            case QuestSub.NpcEvent:
            {
                if (p.RemainingBytes < 6) return;
                int textId = p.ReadInt();
                int npcId = p.ReadShort();
                NpcMsgEvent?.Invoke(textId, npcId);
                break;
            }
            case QuestSub.Clock:
                break;
            case QuestSub.Objectives:
                QuestObjectivesEvent?.Invoke(QuestObjectives.Read(p));
                break;
            case QuestSub.TargetDetail:
                QuestTargetEvent?.Invoke(QuestTargetDetail.Read(p));
                break;
            case QuestSub.RewardReceipt:
                QuestReceiptEvent?.Invoke(QuestReceipt.Read(p));
                break;
            case QuestSub.View:
                QuestViewEvent?.Invoke(QuestView.Read(p));
                break;
            case QuestSub.Text:
                QuestStringsEvent?.Invoke(QuestStrings.ReadList(p));
                break;
            case QuestSub.RewardRefused:
            {
                if (p.RemainingBytes < 1) return;
                QuestRewardRefusedEvent?.Invoke((QuestRewardRefusal)p.ReadByte());
                break;
            }
            case QuestSub.KillCounts:
            {
                if (p.RemainingBytes < 1) return;
                var form = (QuestKillCountForm)p.ReadByte();
                if (form == QuestKillCountForm.All)
                {
                    if (p.RemainingBytes < 2) return;
                    int questId = p.ReadShort();
                    var counts = new ushort[4];
                    for (int i = 0; i < 4; i++)
                        counts[i] = p.RemainingBytes >= 2 ? (ushort)p.ReadShort() : (ushort)0;
                    QuestKillCountsEvent?.Invoke(questId, counts);
                }
                else if (form == QuestKillCountForm.Single)
                {
                    if (p.RemainingBytes < 5) return;
                    int questId = p.ReadShort();
                    int group = p.ReadByte();
                    int cnt = (ushort)p.ReadShort();
                    QuestKillUpdateEvent?.Invoke(questId, group, cnt);
                }
                break;
            }
        }
    }

    private void HandleNpcWindow(Packet p) => NpcWindowEvent?.Invoke((GameOpcodes)p.GetOpcode());
}
