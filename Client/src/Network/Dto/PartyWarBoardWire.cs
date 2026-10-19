namespace LibreKO.Network;

public static class PartyWarBoardWire
{
    private const int ShareBytes = 6;

    public static WarBoardReply? ReadWarBoard(Packet p)
    {
        if (p.RemainingBytes < 3) return null;
        var target = (PartyWarBoardTarget)p.ReadByte();
        var mode = (PartyBoardMeterMode)p.ReadByte();
        int count = p.ReadByte();
        var shares = new List<WarBoardShare>(count);
        for (int i = 0; i < count && p.RemainingBytes >= ShareBytes; i++)
            shares.Add(new WarBoardShare(p.ReadInt(), p.ReadUShort()));
        return new WarBoardReply(target, mode, shares);
    }
}

public enum PartyWarBoardTarget : byte
{
    Rows = 1,
    Board = 2,
}

public enum PartyBoardMeterMode : byte
{
    Off = 0,
    Damage = 1,
    Healing = 2,
}
