namespace LibreKO.Network.Requests;

public interface IPartyRequests
{
    void Answer(bool accept);
    void UserVote(int targetId, byte reason, string text);
    void SignalEcho(uint token);
    void UserVoteAnswer(UserVoteAnswer answer);
    void Create(string targetName, PartyBoardType boardType = PartyBoardType.Normal);
    void Invite(string targetName, PartyBoardType boardType = PartyBoardType.Normal);
    void CancelInvite(int inviteeId);
    void Leave(int memberId);
    void Disband();
    void Commander(int memberId);
    void CommanderMark(int targetId, bool clear);
    void CommanderOrder(CommanderOrder order);
    void Promote(int newLeaderId);
    void BbsRegister(PartyBoardType board = PartyBoardType.Normal);
    void BbsInvite(string name, PartyBoardType board = PartyBoardType.Normal);
    void BbsAccept(string inviter, PartyBoardType board = PartyBoardType.Normal);
    void BbsDelete(PartyBoardType board = PartyBoardType.Normal);
    void BbsList(int pageIndex, PartyBoardType board = PartyBoardType.Normal);
    void BbsWanted(int wantedClass, int pageIndex, string message, PartyBoardType board = PartyBoardType.Normal);
}

public sealed class PartyRequests : IPartyRequests
{
    private readonly IPacketSink sink;
    private readonly PartySession party;
    private readonly PartyWarBoardSession warBoard;

    public PartyRequests(IPacketSink sink, PartySession party, PartyWarBoardSession warBoard)
    {
        this.sink = sink;
        this.party = party;
        this.warBoard = warBoard;
    }

    public void UserVote(int targetId, byte reason, string text)
    {
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteByte(PartyRequest.UserVote);
        p.WriteInt(targetId);
        p.WriteByte(reason);
        if (reason == Domain.UserVote.ReasonOther) p.WriteString(text);
        sink.Send(p);
    }

    public void SignalEcho(uint token) => sink.Send(new PartySignalProbe(token).Echo());

    public void UserVoteAnswer(UserVoteAnswer answer)
    {
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteByte(PartyRequest.UserVoteAnswer);
        p.WriteByte((byte)answer);
        sink.Send(p);
    }

    public void Create(string targetName, PartyBoardType boardType = PartyBoardType.Normal)
    {
        if (string.IsNullOrEmpty(targetName)) return;
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteByte(PartyRequest.Create);
        p.WriteString(targetName);
        p.WriteByte((byte)boardType);
        p.WriteByte((byte)Config.PartyRowMeter);
        p.WriteByte((byte)warBoard.PartyBoardMeterMode);
        sink.Send(p);
    }

    public void Invite(string targetName, PartyBoardType boardType = PartyBoardType.Normal) => SendPartyInviteName(PartyRequest.Insert, targetName, boardType);

    public void CancelInvite(int inviteeId)
    {
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteByte(PartyRequest.InviteCancel);
        p.WriteInt(inviteeId);
        sink.Send(p);
    }

    public void Leave(int memberId)
    {
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteByte(PartyRequest.Remove);
        p.WriteInt(memberId);
        sink.Send(p);
    }

    public void Disband()
    {
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteByte(PartyRequest.Delete);
        sink.Send(p);
    }

    public void Commander(int memberId)
    {
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteByte(PartyRequest.CommanderAppoint);
        p.WriteInt(memberId);
        sink.Send(p);
    }

    public void CommanderMark(int targetId, bool clear)
    {
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteByte(PartyRequest.CommanderMark);
        p.WriteInt(targetId);
        p.WriteInt(CommanderOrders.MarkingSkill);
        p.WriteByte((byte)(clear ? 1 : 0));
        sink.Send(p);
    }

    public void CommanderOrder(CommanderOrder order)
    {
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteByte(PartyRequest.CommanderOrder);
        p.WriteByte((byte)order);
        p.WriteInt(CommanderOrders.OrderSkill);
        sink.Send(p);
    }

    public void Promote(int newLeaderId)
    {
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteByte(PartyRequest.Promote);
        p.WriteInt(newLeaderId);
        sink.Send(p);
    }

    public void BbsRegister(PartyBoardType board = PartyBoardType.Normal)
    {
        var p = new Packet(GameOpcodes.GS_PARTY_BBS);
        p.WriteByte((byte)board);
        p.WriteByte((byte)PartyBbsSubOpcode.Register);
        sink.Send(p);
    }

    public void BbsInvite(string name, PartyBoardType board = PartyBoardType.Normal) =>
            SendPartyBbsName(PartyBbsSubOpcode.Invite, name, board);

    public void BbsAccept(string inviter, PartyBoardType board = PartyBoardType.Normal)
    {
        party.Joining = !party.InParty;
        SendPartyBbsName(PartyBbsSubOpcode.InviteAccept, inviter, board);
    }

    public void BbsDelete(PartyBoardType board = PartyBoardType.Normal)
    {
        var p = new Packet(GameOpcodes.GS_PARTY_BBS);
        p.WriteByte((byte)board);
        p.WriteByte((byte)PartyBbsSubOpcode.Delete);
        sink.Send(p);
    }

    public void BbsList(int pageIndex, PartyBoardType board = PartyBoardType.Normal)
    {
        var p = new Packet(GameOpcodes.GS_PARTY_BBS);
        p.WriteByte((byte)board);
        p.WriteByte((byte)PartyBbsSubOpcode.List);
        p.WriteShort((short)System.Math.Max(0, pageIndex));
        p.WriteByte(0);
        p.WriteShort(0);
        p.WriteByte(0);
        sink.Send(p);
    }

    public void BbsWanted(int wantedClass, int pageIndex, string message, PartyBoardType board = PartyBoardType.Normal)
    {
        var p = new Packet(GameOpcodes.GS_PARTY_BBS);
        p.WriteByte((byte)board);
        p.WriteByte((byte)PartyBbsSubOpcode.Wanted);
        p.WriteShort((short)wantedClass);
        p.WriteShort((short)System.Math.Max(0, pageIndex));
        p.WriteSByteString(message ?? "");
        sink.Send(p);
    }

    private void SendPartyInviteName(byte sub, string targetName, PartyBoardType boardType)
    {
        if (string.IsNullOrEmpty(targetName)) return;
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteByte(sub);
        p.WriteString(targetName);
        p.WriteByte((byte)boardType);
        sink.Send(p);
    }

    private void SendPartyBbsName(PartyBbsSubOpcode sub, string name, PartyBoardType board)
    {
        var p = new Packet(GameOpcodes.GS_PARTY_BBS);
        p.WriteByte((byte)board);
        p.WriteByte((byte)sub);
        p.WriteString(name);
        sink.Send(p);
    }

    public void Answer(bool accept)
    {
        party.Joining = accept && !party.InParty;
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteByte(PartyRequest.Permit);
        p.WriteByte(accept ? (byte)1 : (byte)0);
        sink.Send(p);
    }
}
