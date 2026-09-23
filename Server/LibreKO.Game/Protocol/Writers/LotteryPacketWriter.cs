using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class LotteryPacketWriter
{
    public const byte SubState = 1;
    public const byte SubJoinAck = 2;
    public const byte SubProgress = 3;
    public const byte SubEnded = 4;
    public const byte SubClose = 5;

    public readonly record struct RewardInfo(int ItemId, int ItemCount, string Name);
    public readonly record struct WinnerInfo(byte Place, string CharacterName, int ItemId, int ItemCount, string ItemName);

    public static Packet InactiveState()
    {
        var packet = new Packet(GameOpcodes.GS_LOTTERY);
        packet.WriteByte(SubState);
        packet.WriteByte(0); // active = false
        return packet;
    }

    public static Packet State(
        int lotteryId,
        string name,
        int remainingSeconds,
        int userLimit,
        int totalTickets,
        int myTickets,
        int reqItemId,
        int reqItemCount,
        string reqItemName,
        IReadOnlyList<RewardInfo> rewards)
    {
        var packet = new Packet(GameOpcodes.GS_LOTTERY);
        packet.WriteByte(SubState);
        packet.WriteByte(1); // active = true
        packet.WriteInt(lotteryId);
        packet.WriteSByteString(name);
        packet.WriteInt(remainingSeconds);
        packet.WriteInt(userLimit);
        packet.WriteInt(totalTickets);
        packet.WriteInt(myTickets);
        packet.WriteInt(reqItemId);
        packet.WriteInt(reqItemCount);
        packet.WriteSByteString(reqItemName);

        packet.WriteByte((byte)rewards.Count);
        foreach (var r in rewards)
        {
            packet.WriteInt(r.ItemId);
            packet.WriteInt(r.ItemCount);
            packet.WriteSByteString(r.Name);
        }

        return packet;
    }

    public static Packet JoinAck(bool success, string message, int myTickets, int totalTickets)
    {
        var packet = new Packet(GameOpcodes.GS_LOTTERY);
        packet.WriteByte(SubJoinAck);
        packet.WriteByte((byte)(success ? 1 : 0));
        packet.WriteSByteString(message);
        packet.WriteInt(myTickets);
        packet.WriteInt(totalTickets);
        return packet;
    }

    public static Packet Progress(int totalTickets)
    {
        var packet = new Packet(GameOpcodes.GS_LOTTERY);
        packet.WriteByte(SubProgress);
        packet.WriteInt(totalTickets);
        return packet;
    }

    public static Packet Ended(string message, IReadOnlyList<WinnerInfo> winners)
    {
        var packet = new Packet(GameOpcodes.GS_LOTTERY);
        packet.WriteByte(SubEnded);
        packet.WriteSByteString(message);
        packet.WriteByte((byte)winners.Count);
        foreach (var w in winners)
        {
            packet.WriteByte(w.Place);
            packet.WriteSByteString(w.CharacterName);
            packet.WriteInt(w.ItemId);
            packet.WriteInt(w.ItemCount);
            packet.WriteSByteString(w.ItemName);
        }
        return packet;
    }

    public static Packet Close()
    {
        var packet = new Packet(GameOpcodes.GS_LOTTERY);
        packet.WriteByte(SubClose);
        return packet;
    }
}
