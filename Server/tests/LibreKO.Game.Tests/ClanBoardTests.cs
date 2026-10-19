using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class ClanBoardTests
{
    private const short Wolves = 10;

    private sealed record Player(UserSession Session, List<Packet> Sent);

    private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }

    private readonly SessionManager sessions = new();
    private readonly IKnightsMembershipPacketService membership = Substitute.For<IKnightsMembershipPacketService>();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
    private readonly ClanBoardService boards;

    public ClanBoardTests()
    {
        sessions.Knights.AddClan(Wolves, new KnightsEntity { Id = Wolves, Name = "Wolves", Chief = "Alpha", Nation = 1, Grade = 3, Flag = 2, MarkVersion = 4 });
        boards = new ClanBoardService(sessions, membership, clock);
        membership.JoinFromBoardAsync(Arg.Any<UserSession>(), Arg.Any<KnightsEntity>()).Returns(true);
    }

    private Player Join(int id, string name, short clan = 0, byte fame = 0)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(p => sent.Add(p)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = sessions.CreateSession(client, id, id);
        session.Name = name;
        session.KnightsId = clan;
        session.KnightsFame = fame;
        session.Nation = AccountNation.Karus;
        session.Level = 50;
        return new Player(session, sent);
    }

    private static Packet Request(KnightsSubOpcode sub, Action<Packet> body)
    {
        var packet = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        body(packet);
        packet.ResetOffset();
        return packet;
    }

    private static byte[] Head(Packet packet, int count)
    {
        packet.ResetOffset();
        var bytes = new byte[count];
        for (int i = 0; i < count; i++) bytes[i] = packet.ReadByte();
        return bytes;
    }

    private async Task Advertise(Player chief, string text) =>
        await boards.HandleAdvertAsync(chief.Session, Request(KnightsSubOpcode.ClanAdvanceRegister, p =>
        {
            p.WriteByte(ClanBoardPacketWriter.KindRegister);
            p.WriteString(text);
        }));

    [Fact]
    public async Task AChiefsAdvertIsListedForClanlessPlayersOfTheNation()
    {
        var chief = Join(1, "Alpha", Wolves, ClanRules.FameChief);
        var seeker = Join(2, "Rin");
        await Advertise(chief, "We hunt every night");
        Head(chief.Sent.Single(), 3).Should().Equal((byte)KnightsSubOpcode.ClanAdvanceRegister, ClanBoardPacketWriter.KindRegister, 0);

        await boards.HandleBoardAsync(seeker.Session, Request(KnightsSubOpcode.ClanAdvance, p => { p.WriteByte(1); p.WriteByte(1); }));

        seeker.Sent.Should().HaveCount(2);
        Head(seeker.Sent[0], 3).Should().Equal((byte)KnightsSubOpcode.ClanAdvance, ClanBoardPacketWriter.BoardStatus, ClanBoardPacketWriter.StateClanless);
        var rows = seeker.Sent[1];
        Head(rows, 2).Should().Equal((byte)KnightsSubOpcode.ClanAdvance, ClanBoardPacketWriter.BoardRows);
        rows.ReadShort().Should().Be(1);
        rows.ReadShort().Should().Be(Wolves);
        rows.ReadString().Should().Be("Wolves");
        rows.ReadByte().Should().Be(3);
        rows.ReadByte().Should().Be(2);
        rows.ReadString().Should().Be("Alpha");
        rows.ReadShort().Should().Be(4);
        rows.ReadString().Should().Be("We hunt every night");
    }

    [Fact]
    public async Task AnApplicationShowsInTheChiefsStatusAndCanBeCancelled()
    {
        var chief = Join(1, "Alpha", Wolves, ClanRules.FameChief);
        var seeker = Join(2, "Rin");
        await Advertise(chief, "Join us");
        var apply = Request(KnightsSubOpcode.ClanAdvanceApply, p => { p.WriteByte(0); p.WriteShort(Wolves); p.WriteString("Hello"); });

        await boards.HandleApplyAsync(seeker.Session, apply);
        await boards.HandleApplyAsync(seeker.Session, Request(KnightsSubOpcode.ClanAdvanceApply, p => { p.WriteByte(0); p.WriteShort(Wolves); p.WriteString("Again"); }));

        Head(seeker.Sent[0], 3).Should().Equal((byte)KnightsSubOpcode.ClanAdvanceApply, 0, 0);
        Head(seeker.Sent[1], 3).Should().Equal((byte)KnightsSubOpcode.ClanAdvanceApply, 0, 2);

        chief.Sent.Clear();
        await boards.HandleBoardAsync(chief.Session, Request(KnightsSubOpcode.ClanAdvance, p => { p.WriteByte(1); p.WriteByte(1); }));
        var status = chief.Sent[0];
        Head(status, 3).Should().Equal((byte)KnightsSubOpcode.ClanAdvance, ClanBoardPacketWriter.BoardStatus, ClanBoardPacketWriter.StateStaff);
        status.ReadShort().Should().Be(1);
        status.ReadByte().Should().Be(50);
        status.ReadByte();
        status.ReadString().Should().Be("Rin");
        status.ReadString().Should().Be("Hello");

        await boards.HandleApplyAsync(chief.Session, Request(KnightsSubOpcode.ClanAdvanceApply, p => { p.WriteByte(1); p.WriteString("Rin"); }));
        Head(chief.Sent.Last(), 3).Should().Equal((byte)KnightsSubOpcode.ClanAdvanceApply, 1, 0);
    }

    [Fact]
    public async Task OnlyStaffMayWriteAnAdvertAndPromotionWaitsFiveMinutes()
    {
        var member = Join(3, "Cub", Wolves, ClanRules.FameTrainee);
        await Advertise(member, "nope");
        Head(member.Sent.Single(), 3).Should().Equal((byte)KnightsSubOpcode.ClanAdvanceRegister, 0, 3);

        var chief = Join(1, "Alpha", Wolves, ClanRules.FameChief);
        await Advertise(chief, "Join us");
        chief.Sent.Clear();

        await boards.HandlePromoteAsync(chief.Session);
        Head(chief.Sent.Single(), 2).Should().Equal((byte)KnightsSubOpcode.ClanAdvancePromote, ClanBoardPacketWriter.PromoteBroadcast);
        member.Sent.Should().Contain(p => Head(p, 2)[1] == ClanBoardPacketWriter.PromoteBroadcast);

        chief.Sent.Clear();
        await boards.HandlePromoteAsync(chief.Session);
        Head(chief.Sent.Single(), 2).Should().Equal((byte)KnightsSubOpcode.ClanAdvancePromote, 4);

        clock.Advance(ClanBoardService.ClanPromoteCooldown);
        chief.Sent.Clear();
        await boards.HandlePromoteAsync(chief.Session);
        Head(chief.Sent.Single(), 2).Should().Equal((byte)KnightsSubOpcode.ClanAdvancePromote, ClanBoardPacketWriter.PromoteBroadcast);
    }

    [Fact]
    public async Task ASeekersIntroductionIsListedAndAChiefsRequestReachesThem()
    {
        var chief = Join(1, "Alpha", Wolves, ClanRules.FameChief);
        var seeker = Join(2, "Rin");
        await boards.HandleWishAsync(seeker.Session, Request(KnightsSubOpcode.WishBoard, p =>
        {
            p.WriteByte(2); p.WriteByte(0); p.WriteByte(25); p.WriteSByteString("Priest looking for a clan");
        }));
        Head(seeker.Sent.Single(), 4).Should().Equal((byte)KnightsSubOpcode.WishBoard, ClanBoardPacketWriter.WishIntroResult, 0, 1);

        await boards.HandleWishAsync(chief.Session, Request(KnightsSubOpcode.WishBoard, p => { p.WriteByte(0); p.WriteByte(1); p.WriteByte(0); }));
        var status = chief.Sent[0];
        Head(status, 4).Should().Equal((byte)KnightsSubOpcode.WishBoard, ClanBoardPacketWriter.WishPage, ClanBoardPacketWriter.WishPageStatus, ClanBoardPacketWriter.RoleRecruiter);
        var rows = chief.Sent[1];
        Head(rows, 3).Should().Equal((byte)KnightsSubOpcode.WishBoard, ClanBoardPacketWriter.WishPage, ClanBoardPacketWriter.WishPageRows);
        rows.ReadShort().Should().Be(1);
        rows.ReadString().Should().Be("Rin");
        rows.ReadString().Should().Be("Priest looking for a clan");
        rows.ReadByte().Should().Be(50);
        rows.ReadByte();
        rows.ReadByte().Should().Be(25);
        rows.ReadByte().Should().Be(1);
        rows.ReadInt().Should().Be(seeker.Session.CharacterId);
        rows.ReadString().Should().Be("2026-10-09");

        await boards.HandleWishAsync(chief.Session, Request(KnightsSubOpcode.WishBoard, p =>
        {
            p.WriteByte(3); p.WriteByte(0); p.WriteShort(Wolves); p.WriteSByteString("Rin"); p.WriteSByteString("Come with us");
        }));
        Head(chief.Sent.Last(), 4).Should().Equal((byte)KnightsSubOpcode.WishBoard, ClanBoardPacketWriter.WishRequestResult, 0, 1);

        seeker.Sent.Clear();
        await boards.HandleWishAsync(seeker.Session, Request(KnightsSubOpcode.WishBoard, p => p.WriteByte(1)));
        var list = seeker.Sent.Single();
        Head(list, 3).Should().Equal((byte)KnightsSubOpcode.WishBoard, ClanBoardPacketWriter.WishRequests, 1);
        list.ReadShort().Should().Be(Wolves);
        list.ReadString().Should().Be("Alpha");
        list.ReadString().Should().Be("Wolves");

        await boards.HandleWishAsync(seeker.Session, Request(KnightsSubOpcode.WishBoard, p => { p.WriteByte(5); p.WriteShort(Wolves); }));
        await membership.Received(1).JoinFromBoardAsync(seeker.Session, Arg.Is<KnightsEntity>(c => c.Id == Wolves));
    }

    [Fact]
    public async Task RefreshingTheWishBoardWaitsThirtySeconds()
    {
        var seeker = Join(2, "Rin");
        var refresh = () => Request(KnightsSubOpcode.WishBoard, p => { p.WriteByte(0); p.WriteByte(1); p.WriteByte(1); });

        await boards.HandleWishAsync(seeker.Session, refresh());
        seeker.Sent.Clear();
        await boards.HandleWishAsync(seeker.Session, refresh());

        Head(seeker.Sent.Single(), 3).Should().Equal((byte)KnightsSubOpcode.WishBoard, ClanBoardPacketWriter.WishPage, ClanBoardPacketWriter.WishPageTooSoon);
    }
}
