namespace LibreKO.Network;

public readonly record struct SiegeTowerReply(SiegeTowerSubOpcode Sub, SiegeTowerResult Result, int UserId, int NpcId, float X, float Z)
{
    private const int UseBytes = 2 * sizeof(int) + 3 * sizeof(ushort);
    private const int ExitBytes = 2 * sizeof(int);

    public static SiegeTowerReply? Read(Packet p)
    {
        if (p.RemainingBytes < 2) return null;
        var sub = (SiegeTowerSubOpcode)p.ReadByte();
        var result = (SiegeTowerResult)p.ReadByte();
        int user = 0, npc = 0;
        float x = 0f, z = 0f;
        if (result == SiegeTowerResult.Succeeded && sub == SiegeTowerSubOpcode.Use && p.RemainingBytes >= UseBytes)
        {
            user = p.ReadInt();
            npc = p.ReadInt();
            x = (float)p.ReadUShort() / SiegeTower.WireScale;
            z = (float)p.ReadUShort() / SiegeTower.WireScale;
        }
        else if (result == SiegeTowerResult.Succeeded && sub == SiegeTowerSubOpcode.Exit && p.RemainingBytes >= ExitBytes)
        {
            user = p.ReadInt();
            npc = p.ReadInt();
        }
        return new SiegeTowerReply(sub, result, user, npc, x, z);
    }
}

public enum SiegeTowerSubOpcode : byte
{
    Board = 1,
    Leave = 2,
    Ended = 3,
    Use = 0x10,
    Exit = 0x11,
}

public enum SiegeTowerResult : byte
{
    Failed = 0,
    Succeeded = 1,
}
