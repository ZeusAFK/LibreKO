using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class ReportPacketWriter
{
    public const byte Failed = 0;
    public const byte Succeeded = 1;
    public const byte InspectorOpen = 1;

    public readonly record struct OpenReport(
        int Id, string TargetName, byte VoteYes, byte VoteNo, string Reason);

    public static Packet Result(byte sub, bool success)
    {
        var packet = Sub(sub);
        packet.WriteByte(success ? Succeeded : Failed);
        return packet;
    }

    public static Packet OpenList(byte sub, byte totalPages, IReadOnlyCollection<OpenReport> reports)
    {
        var packet = Sub(sub);
        packet.WriteByte(totalPages);
        packet.WriteByte((byte)reports.Count);
        foreach (var report in reports)
        {
            packet.WriteInt(report.Id);
            packet.WriteSByteString(report.TargetName);
            packet.WriteByte(report.VoteYes);
            packet.WriteByte(report.VoteNo);
            packet.WriteString(report.Reason);
        }
        return packet;
    }

    public static Packet InspectorState(byte sub, ushort openCount)
    {
        var packet = Sub(sub);
        packet.WriteByte(InspectorOpen);
        packet.WriteUShort(openCount);
        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_REPORT);
        packet.WriteByte(sub);
        return packet;
    }
}
