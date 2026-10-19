namespace LibreKO.Network.Requests;

public interface IMonsterStoneRequests
{
    void Send(int itemId);
    void Exp(int itemId);
}

public sealed class MonsterStoneRequests : IMonsterStoneRequests
{
    private readonly IPacketSink sink;

    public MonsterStoneRequests(IPacketSink sink) => this.sink = sink;

    public void Send(int itemId)
    {
        var p = new Packet(GameOpcodes.GS_EVENT);
        p.WriteByte((byte)TempleSubOpcode.MonsterStone);
        p.WriteInt(itemId);
        sink.Send(p);
    }

    public void Exp(int itemId)
    {
        var p = new Packet(GameOpcodes.GS_EVENT);
        p.WriteByte((byte)TempleSubOpcode.MonsterStoneExp);
        p.WriteInt(itemId);
        sink.Send(p);
    }
}
