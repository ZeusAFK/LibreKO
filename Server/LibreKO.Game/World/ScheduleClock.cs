using LibreKO.Game.Configuration;

namespace LibreKO.Game.World;

public sealed class ScheduleClock
{
    private long offsetTicks;

    public TimeSpan Offset => TimeSpan.FromTicks(Interlocked.Read(ref offsetTicks));

    public DateTime UtcNow => DateTime.UtcNow + Offset;

    public void MoveTo(EventSettings events, int hour, int minute)
    {
        var scheduleNow = ScheduleTime.Now(events, DateTime.UtcNow);
        var target = scheduleNow.Date.AddHours(hour).AddMinutes(minute);
        Interlocked.Exchange(ref offsetTicks, (target - scheduleNow).Ticks);
    }

    public void Reset() => Interlocked.Exchange(ref offsetTicks, 0);
}
