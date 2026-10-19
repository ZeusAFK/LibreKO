namespace LibreKO.Network.Requests;

public interface IBattleEventRequests
{
    void BattleStatusRequest();
    void MapEvent(byte eventType = (byte)MapEventSubOpcode.Points);
}

public sealed class BattleEventRequests : IBattleEventRequests
{
    private readonly IPacketSink sink;

    public BattleEventRequests(IPacketSink sink) => this.sink = sink;

    public void BattleStatusRequest()
    {
        var p = new Packet(GameOpcodes.GS_BATTLE_EVENT);
        p.WriteByte((byte)BattleEventSubOpcode.Open);
        sink.Send(p);
    }

    public void MapEvent(byte eventType = (byte)MapEventSubOpcode.Points)
    {
        var p = new Packet(GameOpcodes.GS_MAP_EVENT);
        p.WriteByte(eventType);
        sink.Send(p);
    }
}
