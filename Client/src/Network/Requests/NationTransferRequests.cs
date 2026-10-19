namespace LibreKO.Network.Requests;

public interface INationTransferRequests
{
    bool Submit(IReadOnlyList<NationTransferPick> picks);
    void Cancel();
}

public sealed class NationTransferRequests : INationTransferRequests
{
    private const byte SubmitCancelled = 0;

    private readonly IPacketSink sink;
    private readonly NationTransferSession transfer;

    public NationTransferRequests(IPacketSink sink, NationTransferSession transfer)
    {
        this.sink = sink;
        this.transfer = transfer;
    }

    public bool Submit(IReadOnlyList<NationTransferPick> picks)
    {
        if (transfer.Pending || transfer.Target == 0
            || !NationTransferWire.PicksMatch(transfer.Candidates, picks)) return false;
        var p = new Packet(GameOpcodes.GS_NATION_TRANSFER);
        p.WriteByte((byte)NationTransferSubOpcode.Submit);
        p.WriteByte((byte)NationTransferResult.Accepted);
        p.WriteByte((byte)picks.Count);
        foreach (var pick in picks)
        {
            p.WriteShort((short)pick.Slot);
            p.WriteString(pick.Name);
            p.WriteByte((byte)pick.Race);
            p.WriteByte((byte)pick.Face);
            p.WriteInt(pick.Hair);
        }
        transfer.Pending = true;
        sink.Send(p);
        return true;
    }

    public void Cancel()
    {
        if (transfer.Pending) return;
        transfer.Clear();
        var p = new Packet(GameOpcodes.GS_NATION_TRANSFER);
        p.WriteByte((byte)NationTransferSubOpcode.Submit);
        p.WriteByte(SubmitCancelled);
        sink.Send(p);
    }
}
