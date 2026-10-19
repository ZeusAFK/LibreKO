namespace LibreKO.Network.Requests;

public interface IGuardPetRequests
{
    void Status();
}

public sealed class GuardPetRequests : IGuardPetRequests
{
    private readonly IPacketSink sink;

    public GuardPetRequests(IPacketSink sink) => this.sink = sink;

    public void Status()
    {
        var p = new Packet(GameOpcodes.GS_GUARD_PET);
        p.WriteByte((byte)GuardPetSubOpcode.Status);
        sink.Send(p);
    }
}
