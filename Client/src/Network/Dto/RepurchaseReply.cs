namespace LibreKO.Network;

public readonly record struct RepurchaseRow(byte Row, int ItemId, int Price, uint DeletesAt);

public sealed class RepurchaseReply
{
    public RepurchaseSubOpcode Op { get; private init; }
    public bool Ok { get; private init; }
    public List<RepurchaseRow> Rows { get; } = new();
    public byte BagPosition { get; private init; }
    public int ItemId { get; private init; }
    public byte Flag { get; private init; }
    public RepurchaseError Error { get; private init; }

    public static RepurchaseReply Read(Packet p)
    {
        var op = p.RemainingBytes >= 1 ? (RepurchaseSubOpcode)p.ReadByte() : default;
        if (op is RepurchaseSubOpcode.Open or RepurchaseSubOpcode.Refresh)
        {
            bool ok = p.RemainingBytes >= 1 && (RepurchaseResult)p.ReadByte() == RepurchaseResult.Succeeded;
            var list = new RepurchaseReply { Op = op, Ok = ok };
            if (!ok || p.RemainingBytes < 1) return list;
            int count = p.ReadByte();
            for (int i = 0; i < count && p.RemainingBytes >= 13; i++)
                list.Rows.Add(new RepurchaseRow(p.ReadByte(), p.ReadInt(), p.ReadInt(), p.ReadUInt()));
            return list;
        }
        if (op != RepurchaseSubOpcode.Buy || p.RemainingBytes < 2) return new RepurchaseReply { Op = op };
        bool bought = (RepurchaseResult)p.ReadByte() == RepurchaseResult.Succeeded;
        p.ReadByte();
        if (bought)
        {
            if (p.RemainingBytes < 6) return new RepurchaseReply { Op = op };
            return new RepurchaseReply { Op = op, Ok = true, BagPosition = p.ReadByte(), ItemId = p.ReadInt(), Flag = p.ReadByte() };
        }
        if (p.RemainingBytes < 6) return new RepurchaseReply { Op = op };
        return new RepurchaseReply { Op = op, ItemId = p.ReadInt(), Error = (RepurchaseError)p.ReadShort() };
    }
}
