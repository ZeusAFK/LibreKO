namespace LibreKO.Session;

public sealed class EventScheduleSession
{
    public event Action<EventSchedule>? EventScheduleEvent;

    internal void RaiseEventSchedule(EventSchedule eventSchedule) => EventScheduleEvent?.Invoke(eventSchedule);
}
