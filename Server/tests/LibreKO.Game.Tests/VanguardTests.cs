using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class VanguardTests
{
    private const byte RonarkLand = (byte)ZoneId.RonarkLand;

    private readonly SessionManager sessions = new();
    private readonly IGameDataService gameData = Substitute.For<IGameDataService>();
    private readonly IMagicStatusEffectService magic = Substitute.For<IMagicStatusEffectService>();
    private readonly ILoyaltyService loyalty = Substitute.For<ILoyaltyService>();
    private readonly IItemGrantService items = Substitute.For<IItemGrantService>();
    private readonly VanguardService vanguards;

    public VanguardTests()
    {
        var services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(IMagicStatusEffectService)).Returns(magic);
        services.GetService(typeof(ILoyaltyService)).Returns(loyalty);
        services.GetService(typeof(IItemGrantService)).Returns(items);
        services.GetService(typeof(IWorldPacketCoordinator)).Returns(Substitute.For<IWorldPacketCoordinator>());
        gameData.GetMagic(VanguardRules.RewardMarkSkill).Returns(new MagicData { Id = VanguardRules.RewardMarkSkill });
        gameData.GetItem(Arg.Any<int>()).Returns(call => new ItemData { Num = call.Arg<int>() });
        vanguards = new VanguardService(sessions, gameData, Substitute.For<IZoneTransitionService>(), services,
            NullLogger<VanguardService>.Instance);
    }

    private UserSession Player(int id, AccountNation nation)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = sessions.CreateSession(client, id, id + 1000);
        session.Name = $"Player{id}";
        session.Nation = nation;
        session.ZoneId = RonarkLand;
        session.X = 1000f;
        session.Z = 1000f;
        session.MaxHp = 100;
        session.Hp = 100;
        return session;
    }

    private static List<Packet> Sent(UserSession session) =>
        session.Client.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IClient.SendPacket))
            .Select(call =>
            {
                var packet = (Packet)call.GetArguments()[0]!;
                packet.ResetOffset();
                return packet;
            })
            .ToList();

    private static bool Is(Packet packet, params byte[] head) => head.All(b => packet.ReadByte() == b);

    private static Packet Accept()
    {
        var packet = new Packet(GameOpcodes.GS_VANGUARD);
        packet.WriteByte(VanguardPacketWriter.SubAppoint);
        packet.WriteByte(VanguardPacketWriter.AcceptRequest);
        packet.ResetOffset();
        return packet;
    }

    private async Task Ticks(int count)
    {
        for (int i = 0; i < count; i++)
            await vanguards.TickAsync();
    }

    [Fact]
    public void TheListAndPositionPacketsFollowThe2619Layout()
    {
        var list = VanguardPacketWriter.List(["Velkan", "Rikka"]);
        list.ResetOffset();
        list.ReadByte().Should().Be(VanguardPacketWriter.SubWanted);
        list.ReadByte().Should().Be(VanguardPacketWriter.WantedList);
        list.ReadByte().Should().Be(2);
        list.ReadString().Should().Be("Velkan");
        list.ReadString().Should().Be("Rikka");

        var positions = VanguardPacketWriter.Positions([(1008, 1010, "Velkan")]);
        positions.ResetOffset();
        positions.ReadByte().Should().Be(VanguardPacketWriter.SubWanted);
        positions.ReadByte().Should().Be(VanguardPacketWriter.WantedPositions);
        positions.ReadShort().Should().Be(1);
        positions.ReadShort().Should().Be(1008);
        positions.ReadShort().Should().Be(1010);
        positions.ReadString().Should().Be("Velkan");
    }

    [Fact]
    public async Task StartingInvitesUpToFivePlayersOfEachNation()
    {
        var karus = Enumerable.Range(1, 7).Select(i => Player(i, AccountNation.Karus)).ToList();
        var elmo = Player(20, AccountNation.ElMorad);

        await vanguards.StartAsync();
        await Ticks(1);

        karus.Count(p => Sent(p).Any(s => Is(s, VanguardPacketWriter.SubAppoint, VanguardPacketWriter.AppointInvite)))
            .Should().Be(VanguardRules.MaxPerNation);
        Sent(elmo).Should().ContainSingle(s => Is(s, VanguardPacketWriter.SubAppoint, VanguardPacketWriter.AppointInvite));
    }

    [Fact]
    public async Task AnInviteeIsAppointedAndMarkedAndAnyoneElseIsRefused()
    {
        var invitee = Player(1, AccountNation.Karus);
        await vanguards.StartAsync();
        await Ticks(1);
        var late = Player(2, AccountNation.Karus);

        await vanguards.HandleAsync(invitee.Client, Accept());
        await vanguards.HandleAsync(late.Client, Accept());

        Sent(invitee).Last().Should().Match<Packet>(p =>
            Is(p, VanguardPacketWriter.SubAppoint, VanguardPacketWriter.AppointResult, VanguardPacketWriter.Appointed));
        Sent(late).Last().Should().Match<Packet>(p =>
            Is(p, VanguardPacketWriter.SubAppoint, VanguardPacketWriter.AppointResult, VanguardPacketWriter.AppointFailed));
        vanguards.IsWanted(invitee).Should().BeTrue();
        await magic.Received(1).ExecuteAsync(invitee, Arg.Any<MagicData>(), MagicSkillType.Buff,
            VanguardRules.RewardMarkSkill, invitee.CharacterId, Arg.Any<int[]>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task TheEnemyNationGetsTheWantedListAfterThirtySeconds()
    {
        var vanguard = Player(1, AccountNation.Karus);
        var hunter = Player(2, AccountNation.ElMorad);
        await vanguards.StartAsync();
        await Ticks(1);
        await vanguards.HandleAsync(vanguard.Client, Accept());

        await Ticks(VanguardRules.ListDelaySeconds);

        var list = Sent(hunter).Single(p => Is(p, VanguardPacketWriter.SubWanted, VanguardPacketWriter.WantedList));
        list.ReadByte().Should().Be(1);
        list.ReadString().Should().Be(vanguard.Name);
        Sent(vanguard).Should().NotContain(p => Is(p, VanguardPacketWriter.SubWanted, VanguardPacketWriter.WantedList));
    }

    [Fact]
    public async Task KillingTheLastVanguardRewardsTheKillerAndClosesTheRound()
    {
        var vanguard = Player(1, AccountNation.Karus);
        var hunter = Player(2, AccountNation.ElMorad);
        await vanguards.StartAsync();
        await Ticks(1);
        await vanguards.HandleAsync(vanguard.Client, Accept());
        await Ticks(VanguardRules.ListDelaySeconds);

        await vanguards.OnPlayerDiedAsync(vanguard, hunter);

        vanguards.IsWanted(vanguard).Should().BeFalse();
        Sent(hunter).Should().Contain(p => Is(p, VanguardPacketWriter.SubWanted, VanguardPacketWriter.WantedRemove));
        Sent(hunter).Last().Should().Match<Packet>(p => Is(p, VanguardPacketWriter.SubClosed));
        await items.Received(1).GrantAsync(hunter, Arg.Any<ItemData>(), 1);
        await loyalty.Received(1).ChangeAsync(hunter, VanguardRules.KillerPartyLoyalty);
    }

    [Fact]
    public async Task SurvivorsAreRewardedWhenTheTimeRunsOut()
    {
        var vanguard = Player(1, AccountNation.Karus);
        await vanguards.StartAsync();
        await Ticks(1);
        await vanguards.HandleAsync(vanguard.Client, Accept());

        await Ticks(VanguardRules.ActiveSeconds);

        await items.Received(1).GrantAsync(vanguard, Arg.Is<ItemData>(i => i.Num == VanguardRules.OffensiveEmblem), 1);
        await loyalty.Received(1).ChangeAsync(vanguard, VanguardRules.SurvivorLoyalty);
        Sent(vanguard).Last().Should().Match<Packet>(p => Is(p, VanguardPacketWriter.SubClosed));
    }

    [Fact]
    public void OnlyRonarkLandSendsAVanguardHomeFromItsOwnBase()
    {
        VanguardRules.InOwnBase(RonarkLand, AccountNation.ElMorad, 600f, 900f).Should().BeTrue();
        VanguardRules.InOwnBase(RonarkLand, AccountNation.Karus, 600f, 900f).Should().BeFalse();
        VanguardRules.InOwnBase(RonarkLand, AccountNation.Karus, 1400f, 1100f).Should().BeTrue();
        VanguardRules.InOwnBase((byte)ZoneId.RonarkLandBase, AccountNation.Karus, 1400f, 1100f).Should().BeFalse();
    }
}
