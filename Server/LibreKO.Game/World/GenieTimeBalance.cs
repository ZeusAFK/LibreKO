namespace LibreKO.Game.World;

// A monotonic active-Genie time balance. Reading/saving never rounds up stored credit.
public sealed class GenieTimeBalance(TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly object gate = new();
    private double seconds;
    private long last;
    private bool online;

    public bool IsRunning { get { lock (gate) return online; } }

    public double RemainingSeconds { get { lock (gate) { Settle(); return seconds; } } }

    public void Load(double seconds)
    {
        lock (gate)
        {
            this.seconds = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;
            last = clock.GetTimestamp();
        }
    }

    public void AddSeconds(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        lock (gate) { Settle(); this.seconds += seconds; }
    }

    public void Resume()
    {
        lock (gate)
        {
            if (online) return;
            last = clock.GetTimestamp();
            online = true;
        }
    }

    public void Pause()
    {
        lock (gate) { Settle(); online = false; }
    }

    private void Settle()
    {
        if (!online) return;
        long now = clock.GetTimestamp();
        seconds = Math.Max(0, seconds - Math.Max(0, clock.GetElapsedTime(last, now).TotalSeconds));
        last = now;
    }
}
