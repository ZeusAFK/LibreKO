using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class MailPacketWriter
{
    public const byte Failed = 0;
    public const byte Succeeded = 1;

    public readonly record struct InboxEntry(
        int MailId, string Sender, string Subject, bool Read, int Gold, int ItemId);

    public static Packet Inbox(byte sub, IReadOnlyCollection<InboxEntry> entries)
    {
        var packet = Sub(sub);
        packet.WriteUShort((ushort)entries.Count);

        foreach (var entry in entries)
        {
            packet.WriteInt(entry.MailId);
            packet.WriteSByteString(entry.Sender);
            packet.WriteSByteString(entry.Subject);
            packet.WriteByte(entry.Read ? Succeeded : Failed);
            packet.WriteInt(entry.Gold);
            packet.WriteInt(entry.ItemId);
        }

        return packet;
    }

    public static Packet Result(byte sub, byte result)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        return packet;
    }

    public static Packet MailResult(byte sub, byte result, int mailId)
    {
        var packet = Result(sub, result);
        packet.WriteInt(mailId);
        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_MAIL);
        packet.WriteByte(sub);
        return packet;
    }
}
