namespace LibreKO.Network.Readers;

internal sealed class EventScheduleReader : IPacketReader
{
    private readonly EventScheduleSession eventSchedule;

    public EventScheduleReader(EventScheduleSession eventSchedule)
    {
        this.eventSchedule = eventSchedule;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_EVENT_SCHEDULE, HandleEventSchedule);
    }

    private void HandleEventSchedule(Packet p)
    {
        if (EventScheduleWire.Read(p) is { } schedule) eventSchedule.RaiseEventSchedule(schedule);
    }
}
