namespace LibreKO.Network;

public readonly record struct EffectReply(int EntityId, int Value, string Name)
{
    public static EffectReply? Read(Packet p)
    {
        if (p.RemainingBytes < 8) return null;
        int entity = p.ReadInt();
        int value = p.ReadInt();
        string name = entity == EffectWire.AnnouncementEntity && p.RemainingBytes >= 1 ? p.ReadSByteString() : "";
        return new EffectReply(entity, value, name);
    }
}
