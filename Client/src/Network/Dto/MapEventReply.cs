namespace LibreKO.Network;

public enum MapEventKind
{
    Owners,
    Captured,
    Points,
    Provisions,
    Cont,
    Result,
    Clock,
}

public sealed record MapEventReply(MapEventKind Kind, byte[] Owners, int Index, int Nation, long A, long B)
{
    private const byte CapturedOne = 1;

    public static MapEventReply? Read(Packet p)
    {
        if (p.RemainingBytes < 1) return null;
        return (MapEventSubOpcode)p.ReadByte() switch
        {
            MapEventSubOpcode.Owners when p.RemainingBytes >= 1 => ReadOwners(p),
            MapEventSubOpcode.Captured when p.RemainingBytes >= 3 && p.ReadByte() == CapturedOne =>
                Of(MapEventKind.Captured, index: p.ReadByte(), nation: p.ReadByte()),
            MapEventSubOpcode.Points when p.RemainingBytes >= 2 * sizeof(ushort) => Of(MapEventKind.Points, a: p.ReadUShort(), b: p.ReadUShort()),
            MapEventSubOpcode.Provisions when p.RemainingBytes >= 1 + sizeof(ushort) =>
                Of(MapEventKind.Provisions, nation: p.ReadByte(), a: p.ReadUShort()),
            MapEventSubOpcode.Cont when p.RemainingBytes >= sizeof(uint) => Of(MapEventKind.Cont, a: p.ReadUInt()),
            MapEventSubOpcode.Result when p.RemainingBytes >= 1 => Of(MapEventKind.Result, nation: p.ReadByte()),
            MapEventSubOpcode.Clock when p.RemainingBytes >= sizeof(int) => Of(MapEventKind.Clock, a: p.ReadInt()),
            _ => null,
        };
    }

    private static MapEventReply ReadOwners(Packet p)
    {
        int count = p.ReadByte();
        var owners = new byte[Math.Min(count, p.RemainingBytes)];
        for (int i = 0; i < owners.Length; i++) owners[i] = p.ReadByte();
        return new MapEventReply(MapEventKind.Owners, owners, 0, 0, 0, 0);
    }

    private static MapEventReply Of(MapEventKind kind, int index = 0, int nation = 0, long a = 0, long b = 0) =>
        new(kind, Array.Empty<byte>(), index, nation, a, b);
}

public enum MapEventSubOpcode : byte
{
    Owners = 0,
    Captured = 1,
    Points = 2,
    Provisions = 3,
    Cont = 4,
    Result = 5,
    Clock = 6,
}
