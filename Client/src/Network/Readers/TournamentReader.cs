namespace LibreKO.Network.Readers;

internal sealed class TournamentReader : IPacketReader
{
    private readonly TournamentSession tournament;

    public TournamentReader(TournamentSession tournament)
    {
        this.tournament = tournament;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_TOURNAMENT, HandleTournament);
    }

    private void HandleTournament(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (TournamentSubOpcode)p.ReadByte();
        if (sub == TournamentSubOpcode.Status)
        {
            bool registered = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) != 0;
            int participantCount = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
            int round = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
            tournament.RaiseTournamentStatus(registered, participantCount, round);
        }
        else if (sub == TournamentSubOpcode.Register || sub == TournamentSubOpcode.Unregister)
        {
            bool nowRegistered = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) != 0;
            tournament.RaiseTournamentRegister(nowRegistered);
        }
    }
}
