namespace LibreKO.Network.Readers;

internal sealed class SoccerReader : IPacketReader
{
    private readonly SoccerSession soccer;

    public SoccerReader(SoccerSession soccer)
    {
        this.soccer = soccer;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_MINING, MiningSubOpcode.Soccer, HandleSoccer);
    }

    private void HandleSoccer(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        switch ((SoccerSubOpcode)p.ReadByte())
        {
            case SoccerSubOpcode.Goal when SoccerWire.ReadGoal(p) is { } goal:
                soccer.RaiseSoccerGoal(goal);
                break;
            case SoccerSubOpcode.Clock when p.RemainingBytes >= 2:
                soccer.RaiseSoccerClock(Math.Max(0, (int)p.ReadShort()));
                break;
            case SoccerSubOpcode.Sync when p.RemainingBytes >= 5:
                p.ReadByte();
                int seconds = Math.Max(0, (int)p.ReadShort());
                soccer.RaiseSoccerSync(seconds, p.ReadByte(), p.ReadByte());
                break;
            case SoccerSubOpcode.End when p.RemainingBytes >= 3:
                soccer.RaiseSoccerEnd(new SoccerEnd(p.ReadByte(), p.ReadByte(), p.ReadByte()));
                break;
        }
    }
}
