namespace LibreKO.Session;

public sealed class SoccerSession
{
    public event Action<SoccerGoal>? SoccerGoalEvent;

    internal void RaiseSoccerGoal(SoccerGoal soccerGoal) => SoccerGoalEvent?.Invoke(soccerGoal);

    public event Action<int>? SoccerClockEvent;

    internal void RaiseSoccerClock(int value) => SoccerClockEvent?.Invoke(value);

    public event Action<int, int, int>? SoccerSyncEvent;

    internal void RaiseSoccerSync(int arg1, int arg2, int arg3) => SoccerSyncEvent?.Invoke(arg1, arg2, arg3);

    public event Action<SoccerEnd>? SoccerEndEvent;

    internal void RaiseSoccerEnd(SoccerEnd soccerEnd) => SoccerEndEvent?.Invoke(soccerEnd);

    public int SoccerTeam { get; private set; }

    internal void ApplyTeam(int team) => SoccerTeam = team;

    internal void LeaveTeam() => SoccerTeam = Soccer.TeamNone;
}
