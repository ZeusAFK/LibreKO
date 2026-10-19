namespace LibreKO.Network.Requests;

public interface INpcQuestRequests
{
    void NpcTouch(int npcUniqueId);
    void NpcEvent(int npcUniqueId);
    void ClientEvent(int npcUniqueId);
    void SelectMsg(int menuIndex, string luaFile, int selectedReward = -1);
    void QuestLogRequest();
    void QuestAccept(int questId);
    void QuestComplete(int questId, int chosenReward = -1);
    void QuestAbandon(int questId);
    void QuestNotificationReply(int questId, int choice);
    void QuestTargetRequest(int questId, int group);
    void QuestKillCountRequest(int questId);
}

public sealed class NpcQuestRequests : INpcQuestRequests
{
    private readonly IPacketSink sink;

    public NpcQuestRequests(IPacketSink sink) => this.sink = sink;

    public void NpcTouch(int npcUniqueId)
    {
        var p = new Packet(GameOpcodes.GS_NPC_EVENT);
        p.WriteByte((byte)NpcEventSubOpcode.Touch);
        p.WriteInt(npcUniqueId);
        p.WriteInt(-1);
        sink.Send(p);
    }

    public void NpcEvent(int npcUniqueId)
    {
        var p = new Packet(GameOpcodes.GS_NPC_EVENT);
        p.WriteByte(0);
        p.WriteInt(npcUniqueId);
        p.WriteInt(-1);
        sink.Send(p);
    }

    public void ClientEvent(int npcUniqueId)
    {
        var p = new Packet(GameOpcodes.GS_CLIENT_EVENT);
        p.WriteInt(npcUniqueId);
        sink.Send(p);
    }

    public void SelectMsg(int menuIndex, string luaFile, int selectedReward = -1)
    {
        var p = new Packet(GameOpcodes.GS_SELECT_MSG);
        p.WriteByte((byte)menuIndex);
        p.WriteSByteString(luaFile ?? "");
        p.WriteByte((byte)(sbyte)selectedReward);
        sink.Send(p);
    }

    public void QuestLogRequest()
    {
        var p = new Packet(GameOpcodes.GS_QUEST);
        p.WriteByte((byte)QuestSubOpcode.QuestList);
        sink.Send(p);
    }

    public void QuestAccept(int questId) => SendQuestAction(QuestSubOpcode.Accept, questId);

    public void QuestComplete(int questId, int chosenReward = -1)
    {
        var p = new Packet(GameOpcodes.GS_QUEST);
        p.WriteByte((byte)QuestSubOpcode.CheckFulfill);
        p.WriteInt(questId);
        p.WriteByte((byte)(chosenReward is >= 0 and < 255 ? chosenReward : 255));
        sink.Send(p);
    }

    public void QuestAbandon(int questId) => SendQuestAction(QuestSubOpcode.Abandon, questId);

    public void QuestNotificationReply(int questId, int choice)
    {
        var packet = new Packet(GameOpcodes.GS_QUEST);
        packet.WriteByte((byte)QuestSubOpcode.NotificationReply);
        packet.WriteInt(questId);
        packet.WriteShort((short)choice);
        sink.Send(packet);
    }

    public void QuestTargetRequest(int questId, int group)
    {
        var packet = new Packet(GameOpcodes.GS_QUEST);
        packet.WriteByte((byte)QuestSubOpcode.TargetDetail);
        packet.WriteInt(questId);
        packet.WriteByte((byte)group);
        sink.Send(packet);
    }

    public void QuestKillCountRequest(int questId)
    {
        var p = new Packet(GameOpcodes.GS_QUEST);
        p.WriteByte((byte)QuestSubOpcode.KillCounts);
        p.WriteByte((byte)QuestKillCountForm.All);
        p.WriteShort((short)questId);
        sink.Send(p);
    }

    private void SendQuestAction(QuestSubOpcode sub, int questId)
    {
        var p = new Packet(GameOpcodes.GS_QUEST);
        p.WriteByte((byte)sub);
        p.WriteInt(questId);
        sink.Send(p);
    }
}
