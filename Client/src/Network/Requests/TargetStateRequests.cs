namespace LibreKO.Network.Requests;

public interface ITargetStateRequests
{
    void DropListRequest(int targetId);
    void TargetEffectsRequest(int targetId);
}

public sealed class TargetStateRequests : ITargetStateRequests
{
    private readonly IPacketSink sink;

    public TargetStateRequests(IPacketSink sink) => this.sink = sink;

    public void DropListRequest(int targetId)
    {
        var p = new Packet(GameOpcodes.GS_TARGET_STATE);
        p.WriteByte((byte)TargetStateSubOpcode.DropList);
        p.WriteInt(targetId);
        sink.Send(p);
    }

    public void TargetEffectsRequest(int targetId)
    {
        var p = new Packet(GameOpcodes.GS_TARGET_STATE);
        p.WriteByte((byte)TargetStateSubOpcode.Effects);
        p.WriteInt(targetId);
        sink.Send(p);
    }
}
