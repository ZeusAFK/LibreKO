namespace LibreKO.Network.Requests;

public interface IClanBattleWarRequests
{
    void ClanBattleChallenge(int targetId);
    void ClanBattleAnswer(bool accept, int challengerId);
    void ClanBattleSurrender();
}

public sealed class ClanBattleWarRequests : IClanBattleWarRequests
{
    private readonly IPacketSink sink;

    public ClanBattleWarRequests(IPacketSink sink) => this.sink = sink;

    public void ClanBattleChallenge(int targetId)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)KnightsSubOpcode.ClanBattleChallenge);
        p.WriteInt(targetId);
        sink.Send(p);
    }

    public void ClanBattleAnswer(bool accept, int challengerId)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)KnightsSubOpcode.ClanBattleAnswer);
        p.WriteByte((byte)(accept ? 1 : 0));
        p.WriteInt(challengerId);
        sink.Send(p);
    }

    public void ClanBattleSurrender()
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)KnightsSubOpcode.ClanBattleSurrender);
        sink.Send(p);
    }
}
