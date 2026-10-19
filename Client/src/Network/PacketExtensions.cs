namespace LibreKO.Network;

internal static class PacketExtensions
{
    internal static void Skip(this Packet p, int count)
    {
        int take = Math.Min(count, p.RemainingBytes);
        if (take > 0) p.ReadBytes(take);
    }
}

internal static class WireNumbers
{
    internal static short ClampToShort(ushort value) => value > short.MaxValue ? short.MaxValue : (short)value;

    internal static short ClampToShort(int value) =>
        value > short.MaxValue ? short.MaxValue : value < short.MinValue ? short.MinValue : (short)value;
}
