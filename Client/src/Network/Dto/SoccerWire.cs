namespace LibreKO.Network;

public enum SoccerSubOpcode : byte
{
    Goal = 1,
    Clock = 2,
    Sync = 3,
    End = 4,
}

public static class SoccerWire
{
    public static SoccerGoal? ReadGoal(Packet p) =>
            p.RemainingBytes < 8 ? null : new SoccerGoal(p.ReadInt(), p.ReadByte(), p.ReadByte(), p.ReadByte(), p.ReadByte());
}
