namespace LibreKO.Network;

public enum VanguardReplyKind
{
    Invite,
    Appointed,
    AppointFailed,
    List,
    Positions,
    Remove,
    Closed,
}

public readonly record struct VanguardMark(int X, int Z, string Name);

public sealed record VanguardReply(VanguardReplyKind Kind, IReadOnlyList<string> Names, IReadOnlyList<VanguardMark> Marks)
{
    private const int MarkBytes = 3 * sizeof(short);

    public static VanguardReply? Read(Packet p)
    {
        if (p.RemainingBytes < 1) return null;
        var sub = (VanguardSubOpcode)p.ReadByte();
        if (sub == VanguardSubOpcode.Closed) return Of(VanguardReplyKind.Closed);
        if (p.RemainingBytes < 1) return null;
        byte kind = p.ReadByte();
        return (sub, kind) switch
        {
            (VanguardSubOpcode.Appoint, (byte)VanguardAppointKind.Invite) => Of(VanguardReplyKind.Invite),
            (VanguardSubOpcode.Appoint, (byte)VanguardAppointKind.Result) when p.RemainingBytes >= 1 => (VanguardAppointResult)p.ReadByte() switch
            {
                VanguardAppointResult.Appointed => Of(VanguardReplyKind.Appointed),
                VanguardAppointResult.Failed => Of(VanguardReplyKind.AppointFailed),
                _ => null,
            },
            (VanguardSubOpcode.Wanted, (byte)VanguardWantedKind.List) => ReadList(p),
            (VanguardSubOpcode.Wanted, (byte)VanguardWantedKind.Positions) => ReadPositions(p),
            (VanguardSubOpcode.Wanted, (byte)VanguardWantedKind.Remove) when p.RemainingBytes >= sizeof(short) =>
                new VanguardReply(VanguardReplyKind.Remove, new[] { p.ReadString() }, Array.Empty<VanguardMark>()),
            _ => null,
        };
    }

    private static VanguardReply Of(VanguardReplyKind kind) =>
        new(kind, Array.Empty<string>(), Array.Empty<VanguardMark>());

    private static VanguardReply? ReadList(Packet p)
    {
        if (p.RemainingBytes < 1) return null;
        int count = p.ReadByte();
        var names = new List<string>(count);
        for (int i = 0; i < count && p.RemainingBytes >= sizeof(short); i++)
            names.Add(p.ReadString());
        return new VanguardReply(VanguardReplyKind.List, names, Array.Empty<VanguardMark>());
    }

    private static VanguardReply? ReadPositions(Packet p)
    {
        if (p.RemainingBytes < sizeof(short)) return null;
        int count = Math.Max(0, (int)p.ReadShort());
        var marks = new List<VanguardMark>(count);
        for (int i = 0; i < count && p.RemainingBytes >= MarkBytes; i++)
        {
            int x = p.ReadShort();
            int z = p.ReadShort();
            marks.Add(new VanguardMark(x, z, p.ReadString()));
        }
        var names = marks.ConvertAll(m => m.Name);
        return new VanguardReply(VanguardReplyKind.Positions, names, marks);
    }
}

public enum VanguardSubOpcode : byte
{
    Appoint = 1,
    Wanted = 2,
    Closed = 3,
}

public enum VanguardAppointKind : byte
{
    Accept = 0,
    Invite = 1,
    Result = 2,
}

public enum VanguardWantedKind : byte
{
    List = 1,
    Positions = 2,
    Remove = 3,
}

public enum VanguardAppointResult : byte
{
    Appointed = 1,
    Failed = 2,
}
