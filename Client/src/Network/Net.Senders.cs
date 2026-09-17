using Godot;

namespace LibreKO.Network;

public partial class Net
{
    private void SendVersionCheck()
    {
        var p = new Packet(GameOpcodes.GS_VERSION_CHECK);
        p.WriteUShort((ushort)Config.ServerVersion);
        _conn.Send(p);
    }

    public void Login(string account, string password)
    {
        _account = account;
        _password = password;
        var p = new Packet(GameOpcodes.GS_LOGIN);
        p.WriteString(account);
        p.WriteString(KoPassword.Encode(password));
        _conn.Send(p);
    }

    public void SelectNation(int nation)
    {
        var p = new Packet(GameOpcodes.GS_NATION_SELECT);
        p.WriteByte((byte)nation);
        _conn.Send(p);
    }

    public void CreateCharacter(int slot, string name, int race, int cls, int face, int hair,
        int str, int sta, int dex, int intel, int mag)
    {
        var p = new Packet(GameOpcodes.GS_CREATE_CHARACTER);
        p.WriteByte((byte)slot);
        p.WriteString(name);
        p.WriteByte((byte)race);
        p.WriteShort((short)cls);
        p.WriteByte((byte)face);
        p.WriteInt(hair);
        p.WriteByte((byte)str);
        p.WriteByte((byte)sta);
        p.WriteByte((byte)dex);
        p.WriteByte((byte)intel);
        p.WriteByte((byte)mag);
        _conn.Send(p);
    }

    public void RequestCharList()
    {
        var loading = new Packet(GameOpcodes.GS_LOADING_LOGIN);
        loading.WriteByte(1);
        _conn.Send(loading);

        var p = new Packet(GameOpcodes.GS_ALLCHAR_INFO_REQ);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SelectChar(string charName)
    {
        SelectedChar = charName;
        _selectRetries = 0;
        SendSelect();
    }

    private void SendSelect()
    {
        var p = new Packet(GameOpcodes.GS_SELECT_CHARACTER);
        p.WriteString(_account);
        p.WriteString(SelectedChar);
        p.WriteByte(1);
        _conn.Send(p);
    }

    private void SendGameStart(byte sub)
    {
        var p = new Packet(GameOpcodes.GS_GAMESTART);
        p.WriteByte(sub);
        p.WriteByte(0);
        p.WriteSByteString(SelectedChar);
        _conn.Send(p);
    }

    public void SendReady() => SendGameStart(2);

    private const float MaxWireSpeed = 90f;

    public void SendMove(float x, float z, float y, float speed)
    {
        var wx = (ushort)(x * 10);
        var wz = (ushort)(z * 10);
        var wy = (ushort)(y * 10);
        var p = new Packet(GameOpcodes.GS_MOVE);
        p.WriteUShort(wx); p.WriteUShort(wz); p.WriteUShort(wy);
        p.WriteShort((short)Mathf.Clamp(speed * 10f, -MaxWireSpeed, MaxWireSpeed));
        p.WriteByte(3);
        p.WriteUShort(wx); p.WriteUShort(wz); p.WriteUShort(wy);
        _conn.Send(p);
    }

    public void SendTargetHpRequest(int targetId)
    {
        var p = new Packet(GameOpcodes.GS_TARGET_HP);
        p.WriteInt(targetId);
        p.WriteByte(TargetHpPollEcho);
        _conn.Send(p);
    }

    public void SendRotate(float degrees)
    {
        var p = new Packet(GameOpcodes.GS_ROTATE);
        p.WriteShort(Coord.HeadingToWire(degrees));
        _conn.Send(p);
    }

    public void SendSitting(bool sitting)
    {
        var p = new Packet(GameOpcodes.GS_STATE_CHANGE);
        p.WriteByte(StateChange.Pose);
        p.WriteInt(sitting ? UserPose.Sitting : UserPose.Standing);
        _conn.Send(p);
    }

    public void SendCombatStance(bool ready)
    {
        var p = new Packet(GameOpcodes.GS_STATE_CHANGE);
        p.WriteByte(StateChange.CombatStance);
        p.WriteInt(ready ? StateChange.StanceReady : StateChange.StanceRelaxed);
        _conn.Send(p);
    }

    public void SendAttack(int targetId, short swingDelay, int attackType = 1, bool critical = false)
    {
        var p = new Packet(GameOpcodes.GS_ATTACK);
        p.WriteByte((byte)attackType);
        p.WriteByte(0);
        p.WriteInt(targetId);
        p.WriteShort(swingDelay);
        p.WriteShort(0);
        p.WriteByte(critical ? (byte)1 : (byte)0);
        p.WriteByte(0);
        _conn.Send(p);
    }

    public void SendMagic(int subOpcode, int skillId, int targetId, short[]? data = null)
    {
        var p = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        p.WriteByte((byte)subOpcode);
        p.WriteInt(skillId);
        p.WriteInt(MyCharId);
        p.WriteInt(targetId);
        for (int i = 0; i < 7; i++)
            p.WriteInt(data != null && i < data.Length ? data[i] : 0);
        _conn.Send(p);
    }

    public void SendRegene()
    {
        var p = new Packet(GameOpcodes.GS_REGENE);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SendSkillDataSave(int[] slotIds)
    {
        int count = System.Math.Clamp(slotIds.Length, 1, SkillBarMaxSlots);
        var p = new Packet(GameOpcodes.GS_SKILLDATA);
        p.WriteByte(SkillBarSave);
        p.WriteShort((short)count);
        for (int i = 0; i < count; i++)
            p.WriteInt(i < slotIds.Length ? slotIds[i] : 0);
        _conn.Send(p);
    }

    public void SendSkillDataLoad()
    {
        var p = new Packet(GameOpcodes.GS_SKILLDATA);
        p.WriteByte(SkillBarLoad);
        _conn.Send(p);
    }

    public void SendPointChange(int type)
    {
        var p = new Packet(GameOpcodes.GS_POINT_CHANGE);
        p.WriteByte((byte)type);
        p.WriteShort(1);
        _conn.Send(p);
    }

    public void SendInventoryArrange()
    {
        var p = new Packet(GameOpcodes.GS_ITEM_MOVE);
        p.WriteByte(ItemMove.ArrangeRequest);
        _conn.Send(p);
    }

    public void SendItemMove(byte direction, int itemId, byte sourcePos, byte destPos)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_MOVE);
        p.WriteByte(ItemMove.MoveRequest);
        p.WriteByte(direction);
        p.WriteInt(itemId);
        p.WriteByte(sourcePos);
        p.WriteByte(destPos);
        _pendingItemMove = new PendingItemMove(direction, sourcePos, destPos);
        _conn.Send(p);
    }

    public void SendItemRemove(byte type, byte position, int itemId)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_REMOVE);
        p.WriteByte(type);
        p.WriteByte(position);
        p.WriteInt(itemId);
        _pendingRemoveSlot = type == 1 ? position : type is 0 or 2 ? 14 + position : -1;
        _conn.Send(p);
    }

    public void SendBundleOpen(int bundleId)
    {
        var p = new Packet(GameOpcodes.GS_BUNDLE_OPEN_REQ);
        p.WriteInt(bundleId);
        _conn.Send(p);
    }

    public void SendItemGet(int bundleId, int itemId, int bundleSlot)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_GET);
        p.WriteInt(bundleId);
        p.WriteInt(itemId);
        p.WriteUShort((ushort)bundleSlot);
        _conn.Send(p);
    }

    public void SendItemDrop(byte gridPos, int itemId, ushort count)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_DROP);
        p.WriteByte(gridPos);
        p.WriteInt(itemId);
        p.WriteUShort(count);
        _conn.Send(p);
    }

    public void SendGoTown()
    {
        _conn.Send(new Packet(GameOpcodes.GS_HOME));
    }

    public void SendChat(string message, byte type = 1)
    {
        if (string.IsNullOrEmpty(message)) return;
        if (message.Length > 128) message = message.Substring(0, 128);
        var p = new Packet(GameOpcodes.GS_CHAT);
        p.WriteByte(type);
        p.WriteString(message);
        _conn.Send(p);
    }

    public void SendChatTarget(string targetName)
    {
        if (string.IsNullOrEmpty(targetName)) return;
        if (targetName.Length > 20) targetName = targetName.Substring(0, 20);
        var p = new Packet(GameOpcodes.GS_CHAT_TARGET);
        p.WriteByte(SubChatTargetWhisper);
        p.WriteString(targetName);
        _conn.Send(p);
    }

    public void SendChatBlock(bool block)
    {
        var p = new Packet(GameOpcodes.GS_CHAT_TARGET);
        p.WriteByte(SubChatTargetBlock);
        p.WriteByte(block ? (byte)1 : (byte)0);
        _conn.Send(p);
    }

    public void SendZoneEnterAck()
    {
        var p = new Packet(GameOpcodes.GS_ZONE_CHANGE);
        p.WriteByte(1);
        _conn.Send(p);
    }

    private void SendZoneFinish()
    {
        var p = new Packet(GameOpcodes.GS_ZONE_CHANGE);
        p.WriteByte(2);
        _conn.Send(p);
    }

    public void SendPartyCreate(string targetName) => SendPartyInviteName(PartyRequest.Create, targetName);

    public void SendPartyInvite(string targetName) => SendPartyInviteName(PartyRequest.Insert, targetName);

    private void SendPartyInviteName(byte sub, string targetName)
    {
        if (string.IsNullOrEmpty(targetName)) return;
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteByte(sub);
        p.WriteString(targetName);
        _conn.Send(p);
    }

    public void SendPartyAnswer(bool accept)
    {
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteByte(PartyRequest.Permit);
        p.WriteByte(accept ? (byte)1 : (byte)0);
        _conn.Send(p);
    }

    public void SendPartyLeave(int memberId)
    {
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteByte(PartyRequest.Remove);
        p.WriteInt(memberId);
        _conn.Send(p);
    }

    public void SendPartyDisband()
    {
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteByte(PartyRequest.Delete);
        _conn.Send(p);
    }

    public void SendPartyPromote(int newLeaderId)
    {
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteByte(PartyRequest.Promote);
        p.WriteInt(newLeaderId);
        _conn.Send(p);
    }

    private const byte PartyBbsModeNormal = 0;

    public void SendPartyBbsRegister()
    {
        var p = new Packet(GameOpcodes.GS_PARTY_BBS);
        p.WriteByte(PartyBbsModeNormal);
        p.WriteByte(0x01);
        _conn.Send(p);
    }

    public void SendPartyBbsDelete()
    {
        var p = new Packet(GameOpcodes.GS_PARTY_BBS);
        p.WriteByte(PartyBbsModeNormal);
        p.WriteByte(0x02);
        _conn.Send(p);
    }

    public void SendPartyBbsList(int pageIndex)
    {
        var p = new Packet(GameOpcodes.GS_PARTY_BBS);
        p.WriteByte(PartyBbsModeNormal);
        p.WriteByte(0x03);
        p.WriteShort((short)System.Math.Max(0, pageIndex));
        _conn.Send(p);
    }

    public void SendPartyBbsWanted(int wantedClass, int pageIndex, string message)
    {
        message ??= "";
        if (message.Length > 255) message = message.Substring(0, 255);
        var bytes = System.Text.Encoding.ASCII.GetBytes(message);
        var p = new Packet(GameOpcodes.GS_PARTY_BBS);
        p.WriteByte(PartyBbsModeNormal);
        p.WriteByte(0x04);
        p.WriteShort((short)wantedClass);
        p.WriteShort((short)System.Math.Max(0, pageIndex));
        p.WriteByte((byte)bytes.Length);
        foreach (var b in bytes) p.WriteByte(b);
        _conn.Send(p);
    }

    public void SendNpcEvent(int npcUniqueId)
    {
        var p = new Packet(GameOpcodes.GS_NPC_EVENT);
        p.WriteByte(0);
        p.WriteInt(npcUniqueId);
        p.WriteInt(-1);
        _conn.Send(p);
    }

    public void SendClientEvent(int npcUniqueId)
    {
        var p = new Packet(GameOpcodes.GS_CLIENT_EVENT);
        p.WriteInt(npcUniqueId);
        _conn.Send(p);
    }

    public void SendSelectMsg(int menuIndex, string luaFile, int selectedReward = -1)
    {
        var p = new Packet(GameOpcodes.GS_SELECT_MSG);
        p.WriteByte((byte)menuIndex);
        p.WriteSByteString(luaFile ?? "");
        p.WriteByte((byte)(sbyte)selectedReward);
        _conn.Send(p);
    }

    public void SendQuestLogRequest()
    {
        var p = new Packet(GameOpcodes.GS_QUEST);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SendQuestAccept(int questId) => SendQuestAction(3, questId);

    public void SendQuestComplete(int questId, int chosenReward = -1)
    {
        var p = new Packet(GameOpcodes.GS_QUEST);
        p.WriteByte(4);
        p.WriteInt(questId);
        p.WriteByte((byte)(chosenReward is >= 0 and < 255 ? chosenReward : 255));
        _conn.Send(p);
    }

    public void SendQuestAbandon(int questId) => SendQuestAction(5, questId);

    public void SendQuestNotificationReply(int questId, int choice)
    {
        var packet = new Packet(GameOpcodes.GS_QUEST);
        packet.WriteByte(17);
        packet.WriteInt(questId);
        packet.WriteShort((short)choice);
        _conn.Send(packet);
    }

    public void SendQuestTargetRequest(int questId, int group)
    {
        var packet = new Packet(GameOpcodes.GS_QUEST);
        packet.WriteByte((byte)QuestSub.TargetDetail);
        packet.WriteInt(questId);
        packet.WriteByte((byte)group);
        _conn.Send(packet);
    }

    private void SendQuestAction(byte sub, int questId)
    {
        var p = new Packet(GameOpcodes.GS_QUEST);
        p.WriteByte(sub);
        p.WriteInt(questId);
        _conn.Send(p);
    }

    public void SendQuestKillCountRequest(int questId)
    {
        var p = new Packet(GameOpcodes.GS_QUEST);
        p.WriteByte(9);
        p.WriteByte(1);
        p.WriteShort((short)questId);
        _conn.Send(p);
    }

    private void SendPing()
    {
        var p = new Packet(GameOpcodes.GS_PING);
        p.WriteLong((long)Time.GetTicksMsec());
        _conn.Send(p);
    }
}
