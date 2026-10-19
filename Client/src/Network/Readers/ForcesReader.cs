namespace LibreKO.Network.Readers;

internal sealed class ForcesReader : IPacketReader
{
    private readonly ForcesSession forces;

    public ForcesReader(ForcesSession forces)
    {
        this.forces = forces;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_FORCES, HandleForces);
    }

    private void HandleForces(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (ForcesSubOpcode)p.ReadByte();
        if (sub == ForcesSubOpcode.Status)
        {
            if (p.RemainingBytes < 7) return;
            bool joined = p.ReadByte() != 0;
            int points = p.ReadInt();
            byte rank = p.ReadByte();
            byte angerPct = p.ReadByte();
            forces.RaiseForcesStatus(new ForcesStatus
            {
                Joined = joined,
                Points = points,
                Rank = rank,
                AngerPct = angerPct,
            });
        }
        else if (sub == ForcesSubOpcode.Join)
        {
            bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
            forces.RaiseForcesJoin(ok);
        }
        else if (sub == ForcesSubOpcode.Leave)
        {
            bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
            forces.RaiseForcesLeave(ok);
        }
    }
}
