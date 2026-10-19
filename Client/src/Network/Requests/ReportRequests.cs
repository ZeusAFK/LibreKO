namespace LibreKO.Network.Requests;

public interface IReportRequests
{
    void File(string targetName, string reason);
    void VoteYes(int reportId);
    void VoteNo(int reportId);
    void ListOpen(byte page = 0);
    void Inspector();
}

public sealed class ReportRequests : IReportRequests
{
    private readonly IPacketSink sink;

    public ReportRequests(IPacketSink sink) => this.sink = sink;

    public void File(string targetName, string reason)
    {
        var p = new Packet(GameOpcodes.GS_REPORT);
        p.WriteByte((byte)ReportSubOpcode.File);
        p.WriteSByteString(targetName ?? string.Empty);
        p.WriteString(reason ?? string.Empty);
        sink.Send(p);
    }

    public void VoteYes(int reportId) => SendReportVote(ReportSubOpcode.VoteYes, reportId);

    public void VoteNo(int reportId) => SendReportVote(ReportSubOpcode.VoteNo, reportId);

    public void ListOpen(byte page = 0)
    {
        var p = new Packet(GameOpcodes.GS_REPORT);
        p.WriteByte((byte)ReportSubOpcode.ListOpen);
        p.WriteByte(page);
        sink.Send(p);
    }

    public void Inspector()
    {
        var p = new Packet(GameOpcodes.GS_REPORT);
        p.WriteByte((byte)ReportSubOpcode.Inspector);
        sink.Send(p);
    }

    private void SendReportVote(ReportSubOpcode sub, int reportId)
    {
        var p = new Packet(GameOpcodes.GS_REPORT);
        p.WriteByte((byte)sub);
        p.WriteInt(reportId);
        sink.Send(p);
    }
}
