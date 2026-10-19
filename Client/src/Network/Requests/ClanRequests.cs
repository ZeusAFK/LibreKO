namespace LibreKO.Network.Requests;

public interface IClanRequests
{
    void MarkRegister(byte[] mark);
    void MarkRequest(int clanId);
    void List(int page);
    void Top10();
    void DonationList();
    void LeaderPoints();
    void PointStatus();
    void MembersRequest();
    void Withdraw();
    void Destroy();
    void HandoverList();
    void AllianceList();
    void AllianceLeave();
    void Create(string name);
    void Kick(string name);
    void Admit(string name);
    void Reject(string name);
    void PromoteVice(string name);
    void PromoteOfficer(string name);
    void Handover(string name);
    void Notice(string notice);
    void Memo(string memo);
    void Invite(int charId);
    void InviteAnswer(bool accept, int inviterId, int clanId);
    void Donate(int amount);
    void PointMethod(byte method);
    void AllianceCreate(int chiefCharId);
    void AllianceInsert(int chiefCharId);
    void AllianceAnswer(bool accept);
    void AlliancePunish(int clanId);
    void Promote();
}

public sealed class ClanRequests : IClanRequests
{
    private readonly IPacketSink sink;

    public ClanRequests(IPacketSink sink) => this.sink = sink;

    public void MarkRegister(byte[] mark)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)KnightsSubOpcode.MarkRegister);
        p.WriteUShort((ushort)mark.Length);
        p.WriteBytes(mark);
        sink.Send(p);
    }

    public void MarkRequest(int clanId)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)KnightsSubOpcode.MarkReq);
        p.WriteUShort((ushort)clanId);
        sink.Send(p);
    }

    public void List(int page)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)KnightsSubOpcode.AllListRequest);
        p.WriteUShort((ushort)page);
        sink.Send(p);
    }

    public void Top10() => SendKnightsByte(KnightsSubOpcode.Top10);

    public void DonationList() => SendKnightsByte(KnightsSubOpcode.DonationList);

    public void LeaderPoints() => SendKnightsByte(KnightsSubOpcode.LeaderPoints);

    public void PointStatus() => SendKnightsByte(KnightsSubOpcode.PointRequest);

    public void MembersRequest() => SendKnightsByte(KnightsSubOpcode.MemberRequest);

    public void Withdraw() => SendKnightsByte(KnightsSubOpcode.Withdraw);

    public void Destroy() => SendKnightsByte(KnightsSubOpcode.Destroy);

    public void HandoverList() => SendKnightsByte(KnightsSubOpcode.HandoverList);

    public void AllianceList() => SendKnightsByte(KnightsSubOpcode.AllyList);

    public void AllianceLeave() => SendKnightsByte(KnightsSubOpcode.AllyRemove);

    public void Create(string name) => SendKnightsString(KnightsSubOpcode.Create, name);

    public void Kick(string name) => SendKnightsString(KnightsSubOpcode.Remove, name);

    public void Admit(string name) => SendKnightsString(KnightsSubOpcode.Admit, name);

    public void Reject(string name) => SendKnightsString(KnightsSubOpcode.Reject, name);

    public void PromoteVice(string name) => SendKnightsString(KnightsSubOpcode.Vicechief, name);

    public void PromoteOfficer(string name) => SendKnightsString(KnightsSubOpcode.Officer, name);

    public void Handover(string name) => SendKnightsString(KnightsSubOpcode.HandoverReq, name);

    public void Notice(string notice) => SendKnightsString(KnightsSubOpcode.UpdateNotice, notice);

    public void Memo(string memo)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)KnightsSubOpcode.UpdateMemo);
        p.WriteByte(Domain.ClanMemo.WriteKind);
        p.WriteString(memo);
        sink.Send(p);
    }

    public void Invite(int charId)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)KnightsSubOpcode.Join);
        p.WriteInt(charId);
        sink.Send(p);
    }

    public void InviteAnswer(bool accept, int inviterId, int clanId)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)KnightsSubOpcode.Invite);
        p.WriteByte((byte)(accept ? 1 : 0));
        p.WriteInt(inviterId);
        p.WriteShort((short)clanId);
        sink.Send(p);
    }

    public void Donate(int amount)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)KnightsSubOpcode.DonatePoints);
        p.WriteInt(amount);
        sink.Send(p);
    }

    public void PointMethod(byte method)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)KnightsSubOpcode.PointMethod);
        p.WriteByte((byte)(method + 1));
        sink.Send(p);
    }

    public void AllianceCreate(int chiefCharId) => SendKnightsInt(KnightsSubOpcode.AllyCreate, chiefCharId);

    public void AllianceInsert(int chiefCharId) => SendKnightsInt(KnightsSubOpcode.AllyInsert, chiefCharId);

    public void AllianceAnswer(bool accept)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)KnightsSubOpcode.AllyReq);
        p.WriteByte((byte)(accept ? 1 : 0));
        sink.Send(p);
    }

    public void AlliancePunish(int clanId)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)KnightsSubOpcode.AllyPunish);
        p.WriteShort((short)clanId);
        sink.Send(p);
    }

    public void Promote() => SendKnightsByte(KnightsSubOpcode.ClanAdvancePromote);

    private void SendKnightsByte(KnightsSubOpcode sub)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)sub);
        sink.Send(p);
    }

    private void SendKnightsInt(KnightsSubOpcode sub, int value)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)sub);
        p.WriteInt(value);
        sink.Send(p);
    }

    private void SendKnightsString(KnightsSubOpcode sub, string s)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)sub);
        p.WriteString(s);
        sink.Send(p);
    }
}
