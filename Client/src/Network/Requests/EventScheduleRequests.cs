namespace LibreKO.Network.Requests;

public interface IEventScheduleRequests
{
    void EventScheduleRequest();
}

public sealed class EventScheduleRequests : IEventScheduleRequests
{
    private readonly IPacketSink sink;

    public EventScheduleRequests(IPacketSink sink) => this.sink = sink;

    public void EventScheduleRequest()
    {
        var p = new Packet(GameOpcodes.GS_EVENT_SCHEDULE);
        p.WriteByte((byte)EventScheduleSubOpcode.Schedule);
        sink.Send(p);
    }
}
