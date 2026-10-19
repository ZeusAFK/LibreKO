using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class BountyTests : GameTestBase
{
    private const byte ListSub = 1;
    private const byte PostSub = 2;
    private const byte ClaimSub = 3;
    private const byte Moradon = 21;
    private const int Reward = 400;
    private const int StartingGold = 1000;

    [Fact]
    public async Task PostingABountyTakesTheRewardFromThePoster()
    {
        using var provider = CreateProvider(_ => { });
        var poster = CreatePlayer(provider, 1200, AccountNation.Karus);

        await PostAsync(provider, poster, "BountyTargetA", Reward);

        poster.Session.Money.Should().Be(StartingGold - Reward);
        LastResult(poster, PostSub).Should().Be(BountyPacketWriter.Succeeded);
        (await BountyIdOnAsync(provider, poster, "BountyTargetA")).Should().NotBeNull();
    }

    [Fact]
    public async Task ABountyCannotBePostedWithoutTheGoldForIt()
    {
        using var provider = CreateProvider(_ => { });
        var poster = CreatePlayer(provider, 1201, AccountNation.Karus);
        poster.Session.Money = Reward - 1;

        await PostAsync(provider, poster, "BountyTargetB", Reward);

        poster.Session.Money.Should().Be(Reward - 1);
        LastResult(poster, PostSub).Should().Be(BountyPacketWriter.Failed);
        (await BountyIdOnAsync(provider, poster, "BountyTargetB")).Should().BeNull();
    }

    [Fact]
    public async Task NobodyCanPostABountyOnThemselves()
    {
        using var provider = CreateProvider(_ => { });
        var poster = CreatePlayer(provider, 1202, AccountNation.Karus);

        await PostAsync(provider, poster, poster.Session.Name, Reward);

        poster.Session.Money.Should().Be(StartingGold);
        LastResult(poster, PostSub).Should().Be(BountyPacketWriter.Failed);
    }

    [Fact]
    public async Task OnlyThePlayerWhoKilledTheTargetCollectsTheBountyAndOnlyOnce()
    {
        using var provider = CreateProvider(_ => { });
        var poster = CreatePlayer(provider, 1203, AccountNation.Karus);
        var target = CreatePlayer(provider, 1204, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1205, AccountNation.Karus);
        var bystander = CreatePlayer(provider, 1206, AccountNation.Karus);
        await PostAsync(provider, poster, target.Session.Name, Reward);
        var bountyId = (await BountyIdOnAsync(provider, poster, target.Session.Name))!.Value;

        await ClaimAsync(provider, hunter, bountyId);
        hunter.Session.Money.Should().Be(StartingGold, "the hunter has not killed the target yet");

        target.Session.Hp = 0;
        await provider.GetRequiredService<ICombatLifecycleService>().HandlePlayerDeathAsync(target.Session, hunter.Session);

        await ClaimAsync(provider, bystander, bountyId);
        bystander.Session.Money.Should().Be(StartingGold);

        await ClaimAsync(provider, hunter, bountyId);
        hunter.Session.Money.Should().Be(StartingGold + Reward);
        LastResult(hunter, ClaimSub).Should().Be(BountyPacketWriter.Succeeded);

        await ClaimAsync(provider, hunter, bountyId);
        hunter.Session.Money.Should().Be(StartingGold + Reward);
        LastResult(hunter, ClaimSub).Should().Be(BountyPacketWriter.Failed);
    }

    [Fact]
    public async Task ThePosterCannotCollectTheirOwnBountyEvenAfterTheKill()
    {
        using var provider = CreateProvider(_ => { });
        var poster = CreatePlayer(provider, 1207, AccountNation.Karus);
        var target = CreatePlayer(provider, 1208, AccountNation.ElMorad);
        await PostAsync(provider, poster, target.Session.Name, Reward);
        var bountyId = (await BountyIdOnAsync(provider, poster, target.Session.Name))!.Value;

        target.Session.Hp = 0;
        await provider.GetRequiredService<ICombatLifecycleService>().HandlePlayerDeathAsync(target.Session, poster.Session);
        await ClaimAsync(provider, poster, bountyId);

        poster.Session.Money.Should().Be(StartingGold - Reward);
        LastResult(poster, ClaimSub).Should().Be(BountyPacketWriter.Failed);
    }

    [Fact]
    public async Task AnArenaKillDoesNotEarnTheBounty()
    {
        using var provider = CreateProvider(_ => { });
        var poster = CreatePlayer(provider, 1209, AccountNation.Karus);
        var target = CreatePlayer(provider, 1210, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1211, AccountNation.Karus);
        await PostAsync(provider, poster, target.Session.Name, Reward);
        var bountyId = (await BountyIdOnAsync(provider, poster, target.Session.Name))!.Value;

        target.Session.ArenaId = (byte)(ArenaZones.NoArena + 1);
        target.Session.Hp = 0;
        await provider.GetRequiredService<ICombatLifecycleService>().HandlePlayerDeathAsync(target.Session, hunter.Session);
        await ClaimAsync(provider, hunter, bountyId);

        hunter.Session.Money.Should().Be(StartingGold);
    }

    private sealed record Player(UserSession Session, IClient Client, List<Packet> Sent);

    private static async Task PostAsync(IServiceProvider provider, Player poster, string target, int reward)
    {
        var packet = new Packet(GameOpcodes.GS_BOUNTY);
        packet.WriteByte(PostSub);
        packet.WriteSByteString(target);
        packet.WriteInt(reward);
        await provider.GetRequiredService<IBountyPacketCoordinator>().HandleAsync(poster.Client, packet);
    }

    private static async Task ClaimAsync(IServiceProvider provider, Player claimer, int bountyId)
    {
        var packet = new Packet(GameOpcodes.GS_BOUNTY);
        packet.WriteByte(ClaimSub);
        packet.WriteInt(bountyId);
        await provider.GetRequiredService<IBountyPacketCoordinator>().HandleAsync(claimer.Client, packet);
    }

    private static async Task<int?> BountyIdOnAsync(IServiceProvider provider, Player viewer, string target)
    {
        var packet = new Packet(GameOpcodes.GS_BOUNTY);
        packet.WriteByte(ListSub);
        await provider.GetRequiredService<IBountyPacketCoordinator>().HandleAsync(viewer.Client, packet);

        var list = viewer.Sent.Last(sent => sent.GetOpcode() == (byte)GameOpcodes.GS_BOUNTY);
        list.ResetOffset();
        list.ReadByte().Should().Be(ListSub);
        var count = list.ReadUShort();
        for (var entry = 0; entry < count; entry++)
        {
            var id = list.ReadInt();
            var name = list.ReadSByteString();
            list.ReadSByteString();
            list.ReadInt();
            if (name == target)
                return id;
        }

        return null;
    }

    private static byte LastResult(Player player, byte sub)
    {
        var result = player.Sent.Last(sent =>
        {
            if (sent.GetOpcode() != (byte)GameOpcodes.GS_BOUNTY)
                return false;
            sent.ResetOffset();
            return sent.ReadByte() == sub;
        });
        result.ResetOffset();
        result.ReadByte();
        return result.ReadByte();
    }

    private static Player CreatePlayer(IServiceProvider provider, int characterId, AccountNation nation)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId, accountId: characterId + 1000);
        session.Name = $"Bounty{characterId}";
        session.Nation = nation;
        session.ZoneId = Moradon;
        session.Hp = 100;
        session.MaxHp = 100;
        session.Money = StartingGold;
        session.X = 100;
        session.Z = 100;
        sessionManager.Regions.AddToRegion(session);
        return new Player(session, client, sent);
    }
}
