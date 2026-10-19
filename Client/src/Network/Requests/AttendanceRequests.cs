namespace LibreKO.Network.Requests;

public interface IAttendanceRequests
{
    void BoardRequest();
    void Claim(int slot, byte state);
}

public sealed class AttendanceRequests : IAttendanceRequests
{
    private readonly IPacketSink sink;

    public AttendanceRequests(IPacketSink sink) => this.sink = sink;

    public void BoardRequest()
    {
        var p = new Packet(GameOpcodes.GS_EVENT_BOARD);
        p.WriteByte((byte)EventBoardSubOpcode.AttendanceBoard);
        p.WriteUInt(EventBoardWire.Channel);
        sink.Send(p);
    }

    public void Claim(int slot, byte state)
    {
        if (state < AttendanceSlotState.Claimable) return;
        var p = new Packet(GameOpcodes.GS_EVENT_BOARD);
        p.WriteByte((byte)EventBoardSubOpcode.AttendanceClaim);
        p.WriteUInt(EventBoardWire.Channel);
        p.WriteInt(slot);
        p.WriteByte((byte)(state - 2));
        sink.Send(p);
    }
}
