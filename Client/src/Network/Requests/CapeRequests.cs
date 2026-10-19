namespace LibreKO.Network.Requests;

public interface ICapeRequests
{
    bool Buy(byte op, int capeId, byte r, byte g, byte b);
}

public sealed class CapeRequests : ICapeRequests
{
    private const int GreenShift = 8;
    private const int BlueShift = 16;

    private readonly IPacketSink sink;
    private readonly CapeSession cape;

    public CapeRequests(IPacketSink sink, CapeSession cape)
    {
        this.sink = sink;
        this.cape = cape;
    }

    public bool Buy(byte op, int capeId, byte r, byte g, byte b)
    {
        if (cape.Pending || !CapeWire.IsRequest((CapeSubOpcode)op, capeId)) return false;
        cape.Pending = true;
        var p = new Packet(GameOpcodes.GS_CAPE);
        p.WriteByte(op);
        p.WriteShort((short)capeId);
        p.WriteInt(r | (g << GreenShift) | (b << BlueShift));
        sink.Send(p);
        return true;
    }
}
