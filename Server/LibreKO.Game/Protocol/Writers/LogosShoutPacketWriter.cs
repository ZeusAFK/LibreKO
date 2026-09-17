using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class LogosShoutPacketWriter
{
    public const byte Failed = 0;
    public const byte Succeeded = 1;

    public const sbyte RegisterAccepted = 1;
    public const sbyte RegisterNoItem = -2;
    public const sbyte RegisterRetryLater = -3;
    public const sbyte RegisterChatRestricted = -5;
    public const sbyte RegisterLevelTooLow = -6;
    public const byte NoPersonalRank = 0;
    public const byte AnnouncementUpgrade = 5;

    public static Packet Result(LogosShoutSubOpcode sub, byte result)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        return packet;
    }

    public static Packet RegisterRejected(sbyte result)
    {
        var packet = Sub(LogosShoutSubOpcode.RegisterResult);
        packet.WriteByte(unchecked((byte)result));
        return packet;
    }

    public static Packet RegisterAcceptedAs(byte messageNumber)
    {
        var packet = Sub(LogosShoutSubOpcode.RegisterResult);
        packet.WriteByte(unchecked((byte)RegisterAccepted));
        packet.WriteByte(messageNumber);
        return packet;
    }

    public static Packet RareItemAnnouncement(
        LogosShoutSubOpcode sub, byte announcementType, string playerName, int itemId, byte nation)
    {
        var packet = Sub(sub);
        packet.WriteByte(announcementType);
        packet.WriteSByteString(playerName);
        packet.WriteInt(itemId);
        packet.WriteByte(0);
        packet.WriteByte(nation);
        return packet;
    }

    public static Packet UpgradeAnnouncement(
        byte upgradeResult, string playerName, int itemId, byte nation)
    {
        var packet = Sub(LogosShoutSubOpcode.Broadcast);
        packet.WriteByte(AnnouncementUpgrade);
        packet.WriteByte(upgradeResult);
        packet.WriteSByteString(playerName);
        packet.WriteInt(itemId);
        packet.WriteByte(0);
        packet.WriteByte(nation);
        return packet;
    }

    public static Packet Broadcast(byte red, byte green, byte blue, byte colourIndex, string message)
    {
        var packet = Sub(LogosShoutSubOpcode.Broadcast);
        packet.WriteByte(Succeeded);
        packet.WriteByte(red);
        packet.WriteByte(green);
        packet.WriteByte(blue);
        packet.WriteByte(colourIndex);
        packet.WriteSByteString(message);
        packet.WriteByte(NoPersonalRank);
        return packet;
    }

    private static Packet Sub(LogosShoutSubOpcode sub)
    {
        var packet = new Packet(GameOpcodes.GS_LOGOSSHOUT);
        packet.WriteByte((byte)sub);
        return packet;
    }
}
