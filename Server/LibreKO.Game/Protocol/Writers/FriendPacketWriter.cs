using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public enum FriendState : byte
{
    Offline = 0,
    Online = 1,
    InParty = 3,
}

public sealed class FriendPacketWriter
{
    public const byte NoZone = 0;
    public const byte UnknownNation = 0;
    public const int OfflineCharacterId = -1;

    public readonly record struct Status(string Name, int CharacterId, FriendState State);

    public readonly record struct Detail(
        string Name, byte Level, short Class, byte Nation, byte ZoneId);

    public static Packet Details(IReadOnlyCollection<Detail> friends)
    {
        var packet = Sub(FriendSubOpcode.Details);
        packet.WriteUShort((ushort)friends.Count);

        foreach (var friend in friends)
        {
            packet.WriteString(friend.Name);
            packet.WriteByte(friend.Level);
            packet.WriteShort(friend.Class);
            packet.WriteByte(friend.Nation);
            packet.WriteByte(friend.ZoneId);
        }

        return packet;
    }

    public static Packet StatusList(IReadOnlyCollection<Status> friends, ushort declaredCount)
    {
        var packet = Sub(FriendSubOpcode.StatusList);
        packet.WriteUShort(declaredCount);

        foreach (var friend in friends)
            WriteStatus(packet, friend);

        return packet;
    }

    public static Packet ModifyResult(FriendSubOpcode sub, byte resultCode, string friendName, Status status)
    {
        var packet = Sub(sub);
        packet.WriteByte(resultCode);
        packet.WriteSByteString(friendName);
        WriteStatus(packet, status);
        return packet;
    }

    private static void WriteStatus(Packet packet, Status friend)
    {
        packet.WriteString(friend.Name);
        packet.WriteInt(friend.CharacterId);
        packet.WriteByte((byte)friend.State);
    }

    private static Packet Sub(FriendSubOpcode sub)
    {
        var packet = new Packet(GameOpcodes.GS_FRIEND_PROCESS);
        packet.WriteByte((byte)sub);
        return packet;
    }
}
