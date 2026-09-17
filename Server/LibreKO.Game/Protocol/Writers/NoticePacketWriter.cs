using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class NoticePacketWriter
{
    public const byte ScreenNotice = 1;
    public const byte LoginNotice = 2;

    private const int LineMaxLength = 255;

    public static Packet Broadcast(string message) => Broadcast(ScreenNotice, message);

    public static Packet Broadcast(byte noticeType, string message)
        => noticeType == LoginNotice
            ? Login([(string.Empty, message)])
            : Screen(message);

    public static Packet Screen(params string[] lines)
    {
        var packet = new Packet(GameOpcodes.GS_NOTICE);
        packet.WriteByte(ScreenNotice);
        packet.WriteByte((byte)lines.Length);

        foreach (var line in lines)
            packet.WriteSByteString(Trim(line));

        return packet;
    }

    public static Packet Login(IReadOnlyList<(string Title, string Message)> entries)
    {
        var packet = new Packet(GameOpcodes.GS_NOTICE);
        packet.WriteByte(LoginNotice);
        packet.WriteByte((byte)entries.Count);

        foreach (var (title, message) in entries)
        {
            packet.WriteString(title);
            packet.WriteString(message);
        }

        return packet;
    }

    private static string Trim(string line)
        => line.Length <= LineMaxLength ? line : line[..LineMaxLength];
}
