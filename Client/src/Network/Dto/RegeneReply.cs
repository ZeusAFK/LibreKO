namespace LibreKO.Network;

public readonly record struct RegeneReply
{

    public float X { get; init; }
    public float Z { get; init; }
    public RespawnType Type { get; init; }
    public int CasterId { get; init; }
    public int SkillId { get; init; }
    public RespawnRefusal Code { get; init; }
    public int ItemId { get; init; }
    public ushort Amount { get; init; }

    public static RegeneReply? Read(Packet p)
    {
        if (p.RemainingBytes < 4) return null;
        var reply = new RegeneReply { X = p.ReadUShort() / Coord.TenthsPerUnit, Z = p.ReadUShort() / Coord.TenthsPerUnit, Type = RespawnType.Normal };
        if (p.RemainingBytes >= 2) p.ReadUShort();
        if (p.RemainingBytes < 1) return reply;
        reply = reply with { Type = (RespawnType)p.ReadByte() };
        if (reply.Type == RespawnType.ResurrectOffer)
            return p.RemainingBytes >= 8 ? reply with { CasterId = p.ReadInt(), SkillId = p.ReadInt() } : null;
        if (reply.Type != RespawnType.Refused) return reply;
        if (p.RemainingBytes < 2) return null;
        reply = reply with { Code = (RespawnRefusal)p.ReadUShort() };
        return reply.Code == RespawnRefusal.MissingItem && p.RemainingBytes >= 6
            ? reply with { ItemId = p.ReadInt(), Amount = p.ReadUShort() }
            : reply;
    }
}
