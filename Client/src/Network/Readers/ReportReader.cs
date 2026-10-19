namespace LibreKO.Network.Readers;

internal sealed class ReportReader : IPacketReader
{
    private readonly ReportSession report;

    public ReportReader(ReportSession report)
    {
        this.report = report;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_REPORT, HandleReport);
    }

    private void HandleReport(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (ReportSubOpcode)p.ReadByte();
        switch (sub)
        {
            case ReportSubOpcode.ListOpen:
                {
                    byte totalPages = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                    int count = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
                    var list = new List<ReportEntry>(count);
                    for (int i = 0; i < count && p.RemainingBytes >= 4; i++)
                    {
                        int id = p.ReadInt();
                        string target = p.ReadSByteString();
                        byte yes = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                        byte no = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                        string reason = p.ReadString();
                        list.Add(new ReportEntry(id, target, yes, no, reason));
                    }
                    report.RaiseReportList(totalPages, list);
                    break;
                }
            case ReportSubOpcode.Inspector:
                {
                    bool open = p.RemainingBytes >= 1 && p.ReadByte() != 0;
                    int openCount = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                    report.RaiseReportInspector(open, openCount);
                    break;
                }
            default:
                {
                    bool ok = p.RemainingBytes >= 1 && p.ReadByte() == 1;
                    report.RaiseReportResult(sub, ok);
                    break;
                }
        }
    }
}
