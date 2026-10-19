using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class NationChatTests
{
    private readonly SessionManager sessions = new();
    private readonly Dictionary<int, List<Packet>> sent = [];
    private readonly NationChatService service;

    public NationChatTests()
    {
        service = new NationChatService(sessions, Substitute.For<IChatRoomHistory>(), Substitute.For<ILogger<NationChatService>>());
    }

    private UserSession Player(int id, AccountNation nation)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var log = sent[id] = [];
        client.SendPacket(Arg.Do<Packet>(p => log.Add(p)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = sessions.CreateSession(client, characterId: id, accountId: id * 10);
        session.Name = $"Player{id}";
        session.Nation = nation;
        session.ZoneId = 21;
        return session;
    }

    private static Packet Request(byte nationSub)
    {
        var packet = new Packet(GameOpcodes.GS_NATION_CHAT);
        packet.WriteByte(NationChatPacketWriter.NationRooms);
        packet.WriteByte(nationSub);
        return packet;
    }

    private Task Send(UserSession session, Packet packet)
    {
        packet.ResetOffset();
        return service.HandleAsync(session.Client, packet);
    }

    private Task Create(UserSession owner, string name, string password = "", ushort size = 10)
    {
        var packet = Request(NationChatPacketWriter.Create);
        packet.WriteString(name);
        packet.WriteByte((byte)(password.Length > 0 ? 1 : 0));
        if (password.Length > 0) packet.WriteString(password);
        packet.WriteUShort(size);
        return Send(owner, packet);
    }

    private Task Enter(UserSession who, ushort roomId, string password = "")
    {
        var packet = Request(NationChatPacketWriter.PrivateEnter);
        packet.WriteUShort(roomId);
        packet.WriteByte((byte)(password.Length > 0 ? 1 : 0));
        if (password.Length > 0) packet.WriteString(password);
        return Send(who, packet);
    }

    private Packet Last(int id, byte nationSub)
    {
        var packet = sent[id].Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_NATION_CHAT && Sub(p) == nationSub);
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadByte();
        return packet;
    }

    private static byte Sub(Packet packet)
    {
        packet.ResetOffset();
        packet.ReadByte();
        return packet.ReadByte();
    }

    private List<Packet> RoomChat(int id) => sent[id].Where(p => p.GetOpcode() == (byte)GameOpcodes.GS_CHAT).ToList();

    [Fact]
    public async Task CreatingARoomPutsTheOwnerInItAndListsIt()
    {
        var owner = Player(1, AccountNation.Karus);
        await Create(owner, "Siege talk", "1234", 30);

        var created = Last(1, NationChatPacketWriter.Create);
        created.ReadByte().Should().Be(NationChatResults.Created);
        ushort roomId = created.ReadUShort();
        roomId.Should().BeGreaterThan(NationChatService.PrivateRoomIdBase - 1);
        created.ReadString().Should().Be("Siege talk");
        created.ReadByte().Should().Be(1);
        created.ReadString().Should().Be("1234");
        created.ReadUShort();
        created.ReadUShort().Should().Be(30);

        await Send(owner, Request(NationChatPacketWriter.PrivateList));
        var list = Last(1, NationChatPacketWriter.PrivateList);
        list.ReadUShort().Should().Be(1);
        list.ReadUShort().Should().Be(roomId);
        list.ReadString().Should().Be("Siege talk");
        list.ReadByte().Should().Be(1);
        list.ReadUShort().Should().Be((ushort)AccountNation.Karus);
        list.ReadUShort().Should().Be(1);
        list.ReadUShort().Should().Be(30);

        var other = Player(2, AccountNation.ElMorad);
        await Send(other, Request(NationChatPacketWriter.PrivateList));
        var seen = Last(2, NationChatPacketWriter.PrivateList);
        seen.ReadUShort();
        seen.ReadUShort();
        seen.ReadString();
        seen.ReadByte();
        seen.ReadUShort().Should().Be((ushort)AccountNation.ElMorad, "every room is open to the viewer's nation");
    }

    [Fact]
    public async Task EnteringChecksThePasswordLetsEitherNationInAndTellsTheRoom()
    {
        var owner = Player(1, AccountNation.Karus);
        var friend = Player(2, AccountNation.Karus);
        var other = Player(3, AccountNation.ElMorad);
        await Create(owner, "Locked", "pw");
        ushort roomId = NationChatService.PrivateRoomIdBase;

        await Enter(other, roomId, "nope");
        Last(3, NationChatPacketWriter.PrivateEnter).ReadByte().Should().Be(NationChatResults.PasswordWrong);

        await Enter(friend, roomId, "nope");
        Last(2, NationChatPacketWriter.PrivateEnter).ReadByte().Should().Be(NationChatResults.PasswordWrong);

        await Enter(friend, roomId, "pw");
        var ok = Last(2, NationChatPacketWriter.PrivateEnter);
        ok.ReadByte().Should().Be(NationChatResults.Ok);
        ok.ReadUShort().Should().Be(roomId);

        var notice = RoomChat(1).Last();
        notice.ResetOffset();
        notice.ReadByte().Should().Be((byte)ChatType.ChatRoom);
        notice.ReadByte().Should().Be(NationChatPacketWriter.SayEntered);
        notice.ReadUShort().Should().Be(2);
        notice.ReadString().Should().Be(friend.Name);

        await Enter(other, roomId, "pw");
        Last(3, NationChatPacketWriter.PrivateEnter).ReadByte().Should().Be(NationChatResults.Ok);
        service.MembersOf(RoomKeyOf(1)).Should().BeEquivalentTo([1, 2, 3]);
    }

    private string RoomKeyOf(int characterId) => service.TryGetSeat(characterId, out var seat) ? seat.Key : "";

    [Fact]
    public async Task RoomChatReachesOnlyTheMembers()
    {
        var owner = Player(1, AccountNation.Karus);
        var friend = Player(2, AccountNation.Karus);
        var outsider = Player(3, AccountNation.Karus);
        await Create(owner, "Party");
        ushort roomId = NationChatService.PrivateRoomIdBase;
        await Enter(friend, roomId);

        await service.SayAsync(owner, roomId, "hello");

        var line = RoomChat(2).Last();
        line.ResetOffset();
        line.ReadByte().Should().Be((byte)ChatType.ChatRoom);
        line.ReadByte().Should().Be(NationChatPacketWriter.SayMessage);
        line.ReadInt().Should().Be(owner.CharacterId);
        line.ReadString().Should().Be(owner.Name);
        line.ReadString().Should().Be("hello");
        line.ReadUShort().Should().Be(21);
        RoomChat(3).Should().BeEmpty();

        await service.SayAsync(outsider, roomId, "let me in");
        RoomChat(1).Should().NotContain(p => Said(p) == "let me in");
    }

    private static string Said(Packet packet)
    {
        packet.ResetOffset();
        packet.ReadByte();
        if (packet.ReadByte() != NationChatPacketWriter.SayMessage) return "";
        packet.ReadInt();
        packet.ReadString();
        return packet.ReadString();
    }

    [Fact]
    public async Task WhenTheOwnerLeavesTheNextMemberOwnsTheRoom()
    {
        var owner = Player(1, AccountNation.Karus);
        var friend = Player(2, AccountNation.Karus);
        await Create(owner, "Party");
        ushort roomId = NationChatService.PrivateRoomIdBase;
        await Enter(friend, roomId);

        var leave = Request(NationChatPacketWriter.PrivateLeave);
        leave.WriteUShort(roomId);
        await Send(owner, leave);

        Last(1, NationChatPacketWriter.PrivateLeave).ReadByte().Should().Be(NationChatResults.Ok);
        Last(2, NationChatPacketWriter.HandOver).ReadByte().Should().Be(NationChatResults.YouOwnIt);
    }

    [Fact]
    public async Task OnlyTheOwnerKicksAndNotHimself()
    {
        var owner = Player(1, AccountNation.Karus);
        var friend = Player(2, AccountNation.Karus);
        await Create(owner, "Party");
        ushort roomId = NationChatService.PrivateRoomIdBase;
        await Enter(friend, roomId);

        var byMember = Request(NationChatPacketWriter.Kick);
        byMember.WriteUShort(roomId);
        byMember.WriteInt(owner.CharacterId);
        await Send(friend, byMember);
        Last(2, NationChatPacketWriter.Kick).ReadByte().Should().Be(NationChatResults.NotOwner);

        var self = Request(NationChatPacketWriter.Kick);
        self.WriteUShort(roomId);
        self.WriteInt(owner.CharacterId);
        await Send(owner, self);
        Last(1, NationChatPacketWriter.Kick).ReadByte().Should().Be(NationChatResults.KickSelf);

        var kick = Request(NationChatPacketWriter.Kick);
        kick.WriteUShort(roomId);
        kick.WriteInt(friend.CharacterId);
        await Send(owner, kick);
        Last(1, NationChatPacketWriter.Kick).ReadByte().Should().Be(NationChatResults.Ok);
        Last(2, NationChatPacketWriter.Kick).ReadByte().Should().Be(NationChatResults.YouWereKicked);
    }

    [Fact]
    public async Task PublicChannelsAreSharedByBothNations()
    {
        var karus = Player(1, AccountNation.Karus);
        var elmo = Player(2, AccountNation.ElMorad);
        var enter = Request(NationChatPacketWriter.PublicEnter);
        enter.WriteUShort(1);
        await Send(karus, enter);
        Last(1, NationChatPacketWriter.PublicEnter).ReadByte().Should().Be(NationChatResults.Ok);

        await Send(elmo, Request(NationChatPacketWriter.PublicList));
        var list = Last(2, NationChatPacketWriter.PublicList);
        list.ReadUShort().Should().Be((ushort)NationChatService.PublicChannelNames.Length);
        list.ReadUShort().Should().Be(1);
        list.ReadString().Should().Be(NationChatService.PublicChannelNames[0]);
        list.ReadUShort().Should().Be(1);
        await Send(elmo, enter);
        service.MembersOf(RoomKeyOf(2)).Should().BeEquivalentTo([1, 2]);

        var again = Request(NationChatPacketWriter.Create);
        again.WriteString("Second");
        again.WriteByte(0);
        again.WriteUShort(10);
        await Send(karus, again);
        Last(1, NationChatPacketWriter.Create).ReadByte().Should().Be(NationChatResults.CreateRefused);
    }

    [Fact]
    public async Task LeavingOnLogoutEmptiesTheRoom()
    {
        var owner = Player(1, AccountNation.Karus);
        await Create(owner, "Solo");

        await service.LeaveAllAsync(owner);
        await Send(owner, Request(NationChatPacketWriter.PrivateList));

        Last(1, NationChatPacketWriter.PrivateList).ReadUShort().Should().Be(0);
    }
}
