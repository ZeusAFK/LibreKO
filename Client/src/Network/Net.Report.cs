using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public const byte ReportFileSub      = 9;
    public const byte ReportVoteYesSub   = 12;
    public const byte ReportVoteNoSub    = 13;
    public const byte ReportListOpenSub  = 14;
    public const byte ReportInspectorSub = 18;

    public const int ReportPageSize  = 8;
    public const int ReportReasonMax = 512;

    public readonly struct ReportEntry
    {
        public readonly int Id;
        public readonly string TargetName;
        public readonly byte VoteYes;
        public readonly byte VoteNo;
        public readonly string Reason;
        public ReportEntry(int id, string target, byte yes, byte no, string reason)
        { Id = id; TargetName = target; VoteYes = yes; VoteNo = no; Reason = reason; }
    }

    public event Action<byte, bool>? ReportResultEvent;

    public event Action<byte, List<ReportEntry>>? ReportListEvent;

    public event Action<bool, int>? ReportInspectorEvent;

    private void HandleReport(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case ReportListOpenSub:
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
                ReportListEvent?.Invoke(totalPages, list);
                break;
            }
            case ReportInspectorSub:
            {
                bool open = p.RemainingBytes >= 1 && p.ReadByte() != 0;
                int openCount = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                ReportInspectorEvent?.Invoke(open, openCount);
                break;
            }
            default:
            {
                bool ok = p.RemainingBytes >= 1 && p.ReadByte() == 1;
                ReportResultEvent?.Invoke(sub, ok);
                break;
            }
        }
    }

    public void SendReportFile(string targetName, string reason)
    {
        var p = new Packet(GameOpcodes.GS_REPORT);
        p.WriteByte(ReportFileSub);
        p.WriteSByteString(targetName ?? string.Empty);
        p.WriteString(reason ?? string.Empty);
        _conn.Send(p);
    }

    public void SendReportVoteYes(int reportId) => SendReportVote(ReportVoteYesSub, reportId);

    public void SendReportVoteNo(int reportId) => SendReportVote(ReportVoteNoSub, reportId);

    private void SendReportVote(byte sub, int reportId)
    {
        var p = new Packet(GameOpcodes.GS_REPORT);
        p.WriteByte(sub);
        p.WriteInt(reportId);
        _conn.Send(p);
    }

    public void SendReportListOpen(byte page = 0)
    {
        var p = new Packet(GameOpcodes.GS_REPORT);
        p.WriteByte(ReportListOpenSub);
        p.WriteByte(page);
        _conn.Send(p);
    }

    public void SendReportInspector()
    {
        var p = new Packet(GameOpcodes.GS_REPORT);
        p.WriteByte(ReportInspectorSub);
        _conn.Send(p);
    }
}
