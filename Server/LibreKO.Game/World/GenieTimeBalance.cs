namespace LibreKO.Game.World;

// A monotonic active-Genie time balance. Reading/saving never rounds up stored credit.
public sealed class GenieTimeBalance(TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _gate = new();
    private double _seconds;
    private long _last;
    private bool _online;

    public bool IsRunning { get { lock (_gate) return _online; } }

    public double RemainingSeconds { get { lock (_gate) { Settle(); return _seconds; } } }

    public void Load(double seconds)
    {
        lock (_gate)
        {
            _seconds = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;
            _last = _clock.GetTimestamp();
        }
    }

    public void AddSeconds(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        lock (_gate) { Settle(); _seconds += seconds; }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (_online) return;
            _last = _clock.GetTimestamp();
            _online = true;
        }
    }

    public void Pause()
    {
        lock (_gate) { Settle(); _online = false; }
    }

    private void Settle()
    {
        if (!_online) return;
        long now = _clock.GetTimestamp();
        _seconds = Math.Max(0, _seconds - Math.Max(0, _clock.GetElapsedTime(_last, now).TotalSeconds));
        _last = now;
    }
}
