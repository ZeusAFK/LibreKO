namespace LibreKO.Network.Requests;

public interface INameChangeRequests
{
    void ClanNameChangeConfirm(string newName);
    void Confirm(string newName);
}

public sealed class NameChangeRequests : INameChangeRequests
{
    private readonly IPacketSink sink;

    public NameChangeRequests(IPacketSink sink) => this.sink = sink;

    public void ClanNameChangeConfirm(string newName)
    {
        var p = new Packet(GameOpcodes.GS_NAME_CHANGE);
        p.WriteByte((byte)NameChangeSubOpcode.Clan);
        p.WriteString(newName);
        sink.Send(p);
    }

    public void Confirm(string newName)
    {
        var p = new Packet(GameOpcodes.GS_NAME_CHANGE);
        p.WriteByte((byte)NameChangeSubOpcode.Character);
        p.WriteString(newName);
        sink.Send(p);
    }
}
