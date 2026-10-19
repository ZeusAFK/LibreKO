namespace LibreKO.Network;

public readonly record struct ItemGetReply
{
    public const byte GoldPosition = byte.MaxValue;
    public const int NoBundleSlot = -1;

    public const int HeaderBytes = 1 + 4;
    public const int FailureBytes = 4;
    public const int PartyCoinBytes = 1 + 4 + 4;
    public const int TakenBytes = 1 + 4 + 2 + 4;
    public const int BundleSlotBytes = 2;
    public const int TemporaryBytes = 1 + 4;

    public ItemGetResult Result { get; init; }
    public int BundleId { get; init; }
    public int Failure { get; init; }
    public byte Position { get; init; }
    public int ItemId { get; init; }
    public short Count { get; init; }
    public int Money { get; init; }
    public int BundleSlot { get; init; }
    public string? Looter { get; init; }

    public bool IsGold => Result == ItemGetResult.PartyCoins || Position == GoldPosition;

    public static ItemGetReply? Read(Packet p)
    {
        if (p.RemainingBytes < HeaderBytes) return null;
        var reply = new ItemGetReply { Result = (ItemGetResult)p.ReadByte(), BundleId = p.ReadInt(), BundleSlot = NoBundleSlot };

        if (reply.Result == ItemGetResult.Failed)
            return reply with { Failure = p.RemainingBytes >= FailureBytes ? p.ReadInt() : 0 };

        if (reply.Result == ItemGetResult.PartyMember)
        {
            if (p.RemainingBytes < 4) return null;
            reply = reply with { ItemId = p.ReadInt(), Looter = p.ReadString() };
            return p.RemainingBytes >= BundleSlotBytes ? reply with { BundleSlot = p.ReadUShort() } : reply;
        }

        if (reply.Result is ItemGetResult.TemporaryKept or ItemGetResult.TemporaryLost)
        {
            if (p.RemainingBytes < TemporaryBytes) return null;
            p.ReadByte();
            return reply with { ItemId = p.ReadInt() };
        }

        if (reply.Result == ItemGetResult.PartyCoins)
            return p.RemainingBytes < PartyCoinBytes
                ? null
                : reply with { Position = p.ReadByte(), ItemId = p.ReadInt(), Money = p.ReadInt() };

        if (reply.Result is not (ItemGetResult.Taken or ItemGetResult.TakenToSlot) || p.RemainingBytes < TakenBytes) return null;
        reply = reply with
        {
            Position = p.ReadByte(),
            ItemId = p.ReadInt(),
            Count = (short)Math.Min((int)p.ReadUShort(), short.MaxValue),
            Money = p.ReadInt(),
        };
        return p.RemainingBytes >= BundleSlotBytes ? reply with { BundleSlot = p.ReadUShort() } : reply;
    }
}
