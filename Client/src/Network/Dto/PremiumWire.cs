namespace LibreKO.Network;

public enum PremiumSubOpcode : byte
{
    List = 1,
    Switch = 4,
}

public readonly record struct HeldPremium(int Type, int Hours);

public sealed record PremiumList(IReadOnlyList<HeldPremium> Held, int InUse)
{
    public int InUseHours => Held.FirstOrDefault(held => held.Type == InUse).Hours;
}

public readonly record struct PremiumSwitchReply(int Type, int Result, int Seconds);

public static class PremiumWire
{
    public const int SwitchDone = 1;
    public const int SwitchTooSoon = -1;
    private const int EntryBytes = 3;
    private const int BonusBytes = 3;
    private const int SwitchBytes = 3;

    public static PremiumList ReadList(Packet p)
    {
        int count = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
        var held = new List<HeldPremium>(count);
        for (int i = 0; i < count && p.RemainingBytes >= EntryBytes; i++)
            held.Add(new HeldPremium(p.ReadByte(), p.ReadUShort()));
        int inUse = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
        return new PremiumList(held, inUse);
    }

    public static PremiumSwitchReply? ReadSwitch(Packet p)
    {
        if (p.RemainingBytes < SwitchBytes) return null;
        int type = p.ReadByte();
        int result = p.ReadShort();
        int seconds = result == SwitchTooSoon && p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
        return new PremiumSwitchReply(type, result, seconds);
    }

    public static IReadOnlyList<PremiumBonus> ReadBonuses(Packet p)
    {
        if (p.RemainingBytes < 1) return Array.Empty<PremiumBonus>();
        int count = p.ReadByte();
        var bonuses = new List<PremiumBonus>(count);
        for (int i = 0; i < count && p.RemainingBytes >= BonusBytes; i++)
            bonuses.Add(new PremiumBonus((PremiumBonusKind)p.ReadByte(), p.ReadShort()));
        return bonuses;
    }
}
