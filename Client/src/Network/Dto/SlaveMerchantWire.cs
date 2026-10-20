namespace LibreKO.Network;

public enum SlaveMerchantSub : byte
{
    Status = 1,
    Collect = 2,
    Redeem = 3,
    Time = 4,
    Deploy = 5,
    Sold = 6,
    Closed = 7,
}

public enum SlaveMerchantDeployResult : byte
{
    Deployed = 1,
    NoTime = 2,
    AlreadyOpen = 3,
    ItemsMoved = 4,
    Failed = 5,
}

public enum SlaveMerchantCollectResult : byte
{
    Failed = 0,
    Collected = 1,
}

public enum SlaveMerchantRedeemResult : byte
{
    Failed = 0,
    Added = 1,
}

public enum SlaveMerchantClosedReason : byte
{
    SoldOut = 1,
    OutOfTime = 2,
}

public readonly record struct SlaveMerchantLine(int ItemId, int Count, int Price)
{
    public bool IsEmpty => ItemId == 0 || Count == 0;
}

public sealed record SlaveMerchantStatus(bool Open, int Hours, long Coins, SlaveMerchantLine[] Lines);

public readonly record struct SlaveMerchantCollected(bool Collected, long CoinsPaid, int ItemsReturned, int ItemsKept, long CoinsKept);

public readonly record struct SlaveMerchantSale(int ItemId, int Count, long Coins);

public readonly record struct SlaveMerchantRedeem(bool Added, int Hours);

public static class SlaveMerchantWire
{
    private const int StatusHeaderBytes = 1 + 2 + 4 + 1;
    private const int LineBytes = 4 + 2 + 4;
    private const int CollectedBytes = 1 + 4 + 1 + 1 + 4;
    private const int RedeemBytes = 1 + 2;
    private const int SaleBytes = 4 + 2 + 4;

    public static SlaveMerchantStatus? ReadStatus(Packet p)
    {
        if (p.RemainingBytes < StatusHeaderBytes) return null;
        bool open = p.ReadByte() != 0;
        int hours = p.ReadUShort();
        long coins = p.ReadUInt();
        int count = p.ReadByte();
        var lines = new SlaveMerchantLine[count];
        for (int i = 0; i < count && p.RemainingBytes >= LineBytes; i++)
            lines[i] = new SlaveMerchantLine(p.ReadInt(), p.ReadUShort(), p.ReadInt());
        return new SlaveMerchantStatus(open, hours, coins, lines);
    }

    public static SlaveMerchantCollected? ReadCollected(Packet p) =>
        p.RemainingBytes < CollectedBytes
            ? null
            : new SlaveMerchantCollected((SlaveMerchantCollectResult)p.ReadByte() == SlaveMerchantCollectResult.Collected,
                p.ReadUInt(), p.ReadByte(), p.ReadByte(), p.ReadUInt());

    public static SlaveMerchantRedeem? ReadRedeem(Packet p) =>
        p.RemainingBytes < RedeemBytes
            ? null
            : new SlaveMerchantRedeem((SlaveMerchantRedeemResult)p.ReadByte() == SlaveMerchantRedeemResult.Added, p.ReadUShort());

    public static int? ReadTime(Packet p) => p.RemainingBytes < sizeof(ushort) ? null : p.ReadUShort();

    public static SlaveMerchantDeployResult? ReadDeploy(Packet p) =>
        p.RemainingBytes < 1 ? null : (SlaveMerchantDeployResult)p.ReadByte();

    public static SlaveMerchantSale? ReadSale(Packet p) =>
        p.RemainingBytes < SaleBytes ? null : new SlaveMerchantSale(p.ReadInt(), p.ReadUShort(), p.ReadUInt());

    public static SlaveMerchantClosedReason? ReadClosed(Packet p) =>
        p.RemainingBytes < 1 ? null : (SlaveMerchantClosedReason)p.ReadByte();
}
