namespace LibreKO.Session;

public sealed class TournamentSession
{
    public event Action<bool, int, int>? TournamentStatusEvent;

    internal void RaiseTournamentStatus(bool arg1, int arg2, int arg3) => TournamentStatusEvent?.Invoke(arg1, arg2, arg3);

    public event Action<bool>? TournamentRegisterEvent;

    internal void RaiseTournamentRegister(bool value) => TournamentRegisterEvent?.Invoke(value);
}
