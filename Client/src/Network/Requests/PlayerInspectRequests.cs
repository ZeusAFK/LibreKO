namespace LibreKO.Network.Requests;

public interface IPlayerInspectRequests
{
    void UserInformationRequest(string name);
    void EquipmentViewRequest(string name);
}

public sealed class PlayerInspectRequests : IPlayerInspectRequests
{
    private readonly IPacketSink sink;

    public PlayerInspectRequests(IPacketSink sink) => this.sink = sink;

    public void UserInformationRequest(string name) => SendInspectRequest(PlayerInspectSubOpcode.Detail, name);

    public void EquipmentViewRequest(string name) => SendInspectRequest(PlayerInspectSubOpcode.Equipment, name);

    private void SendInspectRequest(PlayerInspectSubOpcode sub, string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        var p = new Packet(GameOpcodes.GS_USER_INFO);
        p.WriteByte((byte)sub);
        p.WriteSByteString(name);
        sink.Send(p);
    }
}
