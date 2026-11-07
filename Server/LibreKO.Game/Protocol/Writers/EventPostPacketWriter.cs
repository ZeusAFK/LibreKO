using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol.Writers;

public static class EventPostPacketWriter
{
    public const uint Failed = 0;
    public const uint Accepted = 1;
    public const uint TryLater = 5;
    public const uint WrongText = 101;
    public const uint AlreadyPosted = 102;
    private const uint Shown = 1;
    private static readonly TimeSpan LastMoment = TimeSpan.FromSeconds(1);

    public static Packet Board(uint eventId, EventPostBoard? board, EventPostCalendar calendar)
    {
        var packet = Sub(EventBoardSubOpcode.PostBoard);
        packet.WriteUInt(eventId);
        packet.WriteUInt(board != null ? Shown : Failed);
        if (board != null)
            WriteBoard(packet, board, calendar);
        return packet;
    }

    public static Packet Registered(uint eventId, uint result, EventPostBoard? board = null, EventPostCalendar calendar = default)
    {
        var packet = Sub(EventBoardSubOpcode.PostRegister);
        packet.WriteUInt(eventId);
        packet.WriteUInt(result);
        if (result == Accepted && board != null)
            WriteBoard(packet, board, calendar);
        return packet;
    }

    public static Packet History(IReadOnlyList<EventPost> posts)
    {
        var packet = Sub(EventBoardSubOpcode.PostHistory);
        packet.WriteUInt(Shown);
        var count = Math.Min(posts.Count, EventPostRules.HistoryLimit);
        packet.WriteShort((short)count);
        for (var index = 0; index < count; index++)
        {
            packet.WriteByte(posts[index].EventId);
            packet.WriteUInt(UnixTime(posts[index].PostedAt));
        }
        return packet;
    }

    private static void WriteBoard(Packet packet, EventPostBoard board, EventPostCalendar calendar)
    {
        packet.WriteUInt((uint)board.Today);
        packet.WriteUInt((uint)board.InPeriod);
        packet.WriteUInt(UnixTime(calendar.PeriodStart));
        packet.WriteUInt(UnixTime(calendar.PeriodEnd - LastMoment));
        var count = Math.Min(board.Recent.Count, EventPostRules.RecentLimit);
        packet.WriteShort((short)count);
        for (var index = 0; index < count; index++)
        {
            var post = board.Recent[index];
            packet.WriteSByteString(post.ServerName);
            packet.WriteSByteString(post.CharacterName);
            packet.WriteSByteString(post.Text);
            packet.WriteUInt(UnixTime(post.PostedAt));
        }
    }

    private static uint UnixTime(DateTime utc) =>
        (uint)new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

    private static Packet Sub(EventBoardSubOpcode sub)
    {
        var packet = new Packet(GameOpcodes.GS_EVENT_BOARD);
        packet.WriteByte((byte)sub);
        return packet;
    }
}
