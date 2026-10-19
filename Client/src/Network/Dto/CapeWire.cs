namespace LibreKO.Network;

public readonly record struct CapeReply(CapeResult Result, int ClanId, int CapeId, int R, int G, int B)
{
    public bool Changed => Result == CapeResult.Changed;
}

public static class CapeWire
{
    public const int NoCape = -1;
    public const CapeResult RefusedFirst = CapeResult.NotAllowed, RefusedLast = CapeResult.CastellanCannotUse;
    public const int ChangedTailBytes = 10;
    public const int ColourMask = 0xFFFFFF;
    private const int ResultBytes = 2;
    private const int ChannelMask = 0xFF, GreenShift = 8, BlueShift = 16;

    public static bool IsRequest(CapeSubOpcode op, int capeId) =>
        op is CapeSubOpcode.NormalPurchase or CapeSubOpcode.TicketPurchase && capeId >= NoCape && capeId <= short.MaxValue;

    public static bool TryRead(Packet p, out CapeReply reply)
    {
        reply = default;
        if (p.RemainingBytes < ResultBytes) return false;
        var result = (CapeResult)p.ReadShort();
        if (result != CapeResult.Changed)
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

public enum CapeSubOpcode : byte
{
    NormalPurchase = 0,
    TicketPurchase = 1,
}

public enum CapeResult : short
{
    CastellanCannotUse = -10,
    NotEnoughClanPoints = -9,
    MissingPurchaseItem = -8,
    NotEnoughCoins = -7,
    RankTooLow = -6,
    NotSoldHere = -5,
    ClanNotFound = -2,
    NotAllowed = -1,
    Changed = 1,
}

public readonly record struct CapeChangeReply(bool Succeeded, int ClanOrResult, int CapeId, int R, int G, int B);
