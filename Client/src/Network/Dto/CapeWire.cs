namespace LibreKO.Network;

public readonly record struct CapeReply(short Result, int ClanId, int CapeId, int R, int G, int B)
{
    public bool Changed => Result == Net.CapeChanged;
}

public static class CapeWire
{
    public const int NoCape = -1;
    public const short RefusedFirst = -1, RefusedLast = -10;
    public const int ChangedTailBytes = 10;
    public const int ColourMask = 0xFFFFFF;
    private const int ResultBytes = 2;
    private const int ChannelMask = 0xFF, GreenShift = 8, BlueShift = 16;

    public static bool IsRequest(byte op, int capeId) =>
        op is Net.CapeOpBuy or Net.CapeOpTicket && capeId >= NoCape && capeId <= short.MaxValue;

    public static bool TryRead(Packet p, out CapeReply reply)
    {
        reply = default;
        if (p.RemainingBytes < ResultBytes) return false;
        short result = p.ReadShort();
        if (result != Net.CapeChanged)
        {
            if (p.RemainingBytes != 0 || result is > RefusedFirst or < RefusedLast) return false;
            reply = new CapeReply(result, 0, NoCape, 0, 0, 0);
            return true;
        }

        if (p.RemainingBytes != ChangedTailBytes) return false;
        int clanId = p.ReadShort();
        p.ReadShort();
        int capeId = p.ReadShort();
        int colour = p.ReadInt();
        if (capeId < NoCape || (colour & ~ColourMask) != 0) return false;
        reply = new CapeReply(result, clanId, capeId,
            colour & ChannelMask, (colour >> GreenShift) & ChannelMask, (colour >> BlueShift) & ChannelMask);
        return true;
    }
}
