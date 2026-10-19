namespace LibreKO.Network.Requests;

public interface IChangeHairRequests
{
    bool Change(byte couponPosition, int face, int hair);
}

public sealed class ChangeHairRequests : IChangeHairRequests
{
    private readonly IPacketSink sink;
    private readonly ChangeHairSession changeHair;
    private readonly SelfSession self;

    public ChangeHairRequests(IPacketSink sink, ChangeHairSession hair, SelfSession self)
    {
        this.sink = sink;
        changeHair = hair;
        this.self = self;
    }

    public bool Change(byte couponPosition, int face, int hair)
    {
        if (string.IsNullOrEmpty(self.LastEnter.Name) || !changeHair.Request.TryBegin(face, hair)) return false;
        var p = new Packet(GameOpcodes.GS_CHANGE_HAIR);
        p.WriteByte((byte)ChangeHairSubOpcode.InGame);
        p.WriteByte(couponPosition);
        p.WriteByte((byte)face);
        p.WriteInt(hair);
        sink.Send(p);
        return true;
    }
}
