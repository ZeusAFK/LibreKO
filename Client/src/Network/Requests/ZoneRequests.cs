namespace LibreKO.Network.Requests;

public interface IZoneRequests
{
    void EnterAck();
    void Finish();
    void UsersIn(IReadOnlyCollection<int> ids);
    void NpcsIn(IReadOnlyCollection<int> ids);
}

public sealed class ZoneRequests : IZoneRequests
{
    private readonly IPacketSink sink;

    public ZoneRequests(IPacketSink sink) => this.sink = sink;

    public void EnterAck()
    {
        var p = new Packet(GameOpcodes.GS_ZONE_CHANGE);
        p.WriteByte((byte)ZoneChangeSubOpcode.Loading);
        sink.Send(p);
    }

    public void Finish()
    {
        var p = new Packet(GameOpcodes.GS_ZONE_CHANGE);
        p.WriteByte((byte)ZoneChangeSubOpcode.Ready);
        sink.Send(p);
    }

    public void UsersIn(IReadOnlyCollection<int> ids) => SendIds(GameOpcodes.GS_REQ_USERIN, ids);

    public void NpcsIn(IReadOnlyCollection<int> ids) => SendIds(GameOpcodes.GS_REQ_NPCIN, ids);

    private void SendIds(GameOpcodes opcode, IReadOnlyCollection<int> ids)
    {
        var p = new Packet(opcode);
        p.WriteShort((short)ids.Count);
        foreach (int id in ids)
            p.WriteInt(id);
        sink.Send(p);
    }
}
