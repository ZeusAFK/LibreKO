namespace LibreKO.Network.Requests;

public interface IClanBoardsRequests
{
    void ClanBoardOpen(int page);
    void ClanBoardPage(int page);
    void ClanApply(int clanId, string message);
    void ClanApplyCancel(string name);
    void ClanAdvertRegister(string text);
    void ClanAdvertDelete();
    void WishPage(int page, bool refresh);
    void WishRequestList();
    void WishIntroRegister(int age, string text);
    void WishIntroDelete();
    void WishOfflineRequest(int clanId, string poster, string message);
    void WishRequestCancel(int clanId);
    void WishOnlineRequest(int targetId, string name);
    void WishAccept(int clanId);
    void WishPromote();
}

public sealed class ClanBoardsRequests : IClanBoardsRequests
{
    private readonly IPacketSink sink;

    public ClanBoardsRequests(IPacketSink sink) => this.sink = sink;

    public void ClanBoardOpen(int page) => SendClanBoard(ClanAdvanceRequest.Open, page);

    public void ClanBoardPage(int page) => SendClanBoard(ClanAdvanceRequest.Page, page);

    public void ClanApply(int clanId, string message)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)KnightsSubOpcode.ClanAdvanceApply);
        p.WriteByte((byte)ClanBoardAction.Register);
        p.WriteShort((short)clanId);
        p.WriteString(message);
        sink.Send(p);
    }

    public void ClanApplyCancel(string name)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)KnightsSubOpcode.ClanAdvanceApply);
        p.WriteByte((byte)ClanBoardAction.Delete);
        p.WriteString(name);
        sink.Send(p);
    }

    public void ClanAdvertRegister(string text)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)KnightsSubOpcode.ClanAdvanceRegister);
        p.WriteByte((byte)ClanBoardAction.Register);
        p.WriteString(text);
        sink.Send(p);
    }

    public void ClanAdvertDelete()
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)KnightsSubOpcode.ClanAdvanceRegister);
        p.WriteByte((byte)ClanBoardAction.Delete);
        sink.Send(p);
    }

    public void WishPage(int page, bool refresh)
    {
        var p = Wish(WishBoardType.Page);
        p.WriteByte((byte)page);
        p.WriteByte((byte)(refresh ? 1 : 0));
        sink.Send(p);
    }

    public void WishRequestList() => sink.Send(Wish(WishBoardType.Requests));

    public void WishIntroRegister(int age, string text)
    {
        var p = Wish(WishBoardType.Intro);
        p.WriteByte((byte)ClanBoardAction.Register);
        p.WriteByte((byte)age);
        p.WriteSByteString(text);
        sink.Send(p);
    }

    public void WishIntroDelete()
    {
        var p = Wish(WishBoardType.Intro);
        p.WriteByte((byte)ClanBoardAction.Delete);
        sink.Send(p);
    }

    public void WishOfflineRequest(int clanId, string poster, string message)
    {
        var p = Wish(WishBoardType.Request);
        p.WriteByte((byte)ClanBoardAction.Register);
        p.WriteShort((short)clanId);
        p.WriteSByteString(poster);
        p.WriteSByteString(message);
        sink.Send(p);
    }

    public void WishRequestCancel(int clanId)
    {
        var p = Wish(WishBoardType.Request);
        p.WriteByte((byte)ClanBoardAction.Delete);
        p.WriteShort((short)clanId);
        sink.Send(p);
    }

    public void WishOnlineRequest(int targetId, string name)
    {
        var p = Wish(WishBoardType.Online);
        p.WriteInt(targetId);
        p.WriteSByteString(name);
        sink.Send(p);
    }

    public void WishAccept(int clanId)
    {
        var p = Wish(WishBoardType.Accept);
        p.WriteShort((short)clanId);
        sink.Send(p);
    }

    public void WishPromote() => sink.Send(Wish(WishBoardType.Promote));

    private void SendClanBoard(ClanAdvanceRequest mode, int page)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)KnightsSubOpcode.ClanAdvance);
        p.WriteByte((byte)mode);
        p.WriteByte((byte)page);
        sink.Send(p);
    }

    private static Packet Wish(WishBoardType type)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)KnightsSubOpcode.WishBoard);
        p.WriteByte((byte)type);
        return p;
    }
}
