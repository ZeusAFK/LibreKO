namespace LibreKO.Session;

public sealed class ReportSession
{
    public event Action<ReportSubOpcode, bool>? ReportResultEvent;

    internal void RaiseReportResult(ReportSubOpcode arg1, bool arg2) => ReportResultEvent?.Invoke(arg1, arg2);

    public event Action<byte, List<ReportEntry>>? ReportListEvent;

    internal void RaiseReportList(byte arg1, List<ReportEntry> list) => ReportListEvent?.Invoke(arg1, list);

    public event Action<bool, int>? ReportInspectorEvent;

    internal void RaiseReportInspector(bool arg1, int arg2) => ReportInspectorEvent?.Invoke(arg1, arg2);
}
