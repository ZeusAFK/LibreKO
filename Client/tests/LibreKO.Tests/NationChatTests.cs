using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class NationChatTests
{
    private static Packet Reply(NationChatSubOpcode sub)
    {
        var p = new Packet(GameOpcodes.GS_NATION_CHAT);
        p.WriteByte(NationChatWire.NationRooms);
        p.WriteByte((byte)sub);
        return p;
    }

    private static Packet Ready(Packet p)
    {
        p.ResetOffset();
        p.ReadByte();
        p.ReadByte();
        return p;
    }

    [Fact]
    public void ThePrivateListCarriesLockNationAndSeats()
    {
        var p = Reply(NationChatSubOpcode.PrivateList);
        p.WriteUShort(2);
        p.WriteUShort(1001);
        p.WriteString("Siege at nine");
        p.WriteByte(1);
        p.WriteUShort(1);
        p.WriteUShort(4);
        p.WriteUShort(30);
        p.WriteUShort(1002);
        p.WriteString("Traders");
        p.WriteByte(0);
        p.WriteUShort(2);
        p.WriteUShort(12);
        p.WriteUShort(50);

        var rooms = NationChatReplies.ReadRooms(Ready(p));

        Assert.Equal(2, rooms.Count);
        Assert.Equal(new NationRoom(1001, "Siege at nine", true, 1, 4, 30), rooms[0]);
        Assert.Equal(new NationRoom(1002, "Traders", false, 2, 12, 50), rooms[1]);
    }

    [Fact]
    public void ThePublicListStopsAtSixteenRows()
    {
        var p = Reply(NationChatSubOpcode.PublicList);
        p.WriteUShort(20);
        for (int i = 1; i <= 20; i++)
        {
            p.WriteUShort((ushort)i);
            p.WriteString($"Channel {i}");
            p.WriteUShort((ushort)(i * 3));
        }

        var channels = NationChatReplies.ReadChannels(Ready(p));

        Assert.Equal(NationChatWire.PublicListMax, channels.Count);
        Assert.Equal(new NationChannel(2, "Channel 2", 6), channels[1]);
    }

    [Fact]
    public void ACreatedRoomReadsItsPasswordOnlyWhenPrivate()
    {
        var p = new Packet(GameOpcodes.GS_NATION_CHAT);
        p.WriteUShort(1003);
        p.WriteString("Locked");
        p.WriteByte(1);
        p.WriteString("1234");
        p.WriteUShort(0);
        p.WriteUShort(30);
        p.ResetOffset();

        var info = NationChatReplies.ReadRoomInfo(p);

        Assert.Equal(new NationRoomInfo(1003, "Locked", true, "1234", 30), info);
    }

    [Fact]
    public void TheMemberListKeepsTheOwnerAndTheNames()
    {
        var p = new Packet(GameOpcodes.GS_NATION_CHAT);
        p.WriteUShort(1003);
        p.WriteString("Locked");
        p.WriteInt(7);
        p.WriteUShort(30);
        p.WriteUShort(2);
        p.WriteInt(7);
        p.WriteString("Rikka");
        p.WriteInt(9);
        p.WriteString("Zeus");
        p.ResetOffset();

        var members = NationChatReplies.ReadMembers(p);

        Assert.NotNull(members);
        Assert.Equal(7, members.OwnerId);
        Assert.Equal(2, members.Members.Count);
        Assert.Equal("Zeus", members.Members[1].Name);
    }

    [Fact]
    public void LeavingClearsTheRoom()
    {
        var state = new NationChatState();
        state.OwnRoom(1003, "Locked", true, "1234", 30);
        Assert.True(state.InPrivate);
        Assert.True(state.Owner);
        state.LostOwnership();
        Assert.False(state.Owner);
        state.Left();
        Assert.False(state.InRoom);
        Assert.Equal(NationChatRules.NoRoom, state.RoomId);
    }

    [Theory]
    [InlineData(1, NationChatTexts.AlreadyInRoom)]
    [InlineData(4, NationChatTexts.PasswordWrong)]
    [InlineData(5, NationChatTexts.NationMismatch)]
    [InlineData(0, 0)]
    public void PrivateEnterResultsMapToThe2619Texts(byte result, int text)
    {
        Assert.Equal(text, NationChatRules.PrivateEnterText(result));
    }

    [Fact]
    public void ARoomNeedsATitleAndAPrivateOneAPassword()
    {
        static string Text(int id, string fallback) => $"{id}";
        Assert.Equal($"{NationChatTexts.NoTitle}", NationChatRules.Validate("  ", false, "", Text));
        Assert.Equal($"{NationChatTexts.NoPassword}", NationChatRules.Validate("Room", true, "", Text));
        Assert.Equal("", NationChatRules.Validate("Room", false, "", Text));
    }
}
