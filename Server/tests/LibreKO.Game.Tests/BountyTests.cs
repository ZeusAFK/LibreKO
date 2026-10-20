using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class BountyTests : GameTestBase
{
    private const byte ListSub = 1;
    private const byte PostSub = 2;
    private const byte ClaimSub = 3;
    private const byte WithdrawSub = 4;
    private const int ExpiryDays = 8;
    private const byte Moradon = 21;
    private const int Reward = 400;
    private const int StartingGold = 1000;

    [Fact]
    public async Task PostingABountyTakesTheRewardFromThePoster()
    {
        using var provider = CreateProvider(_ => { });
        var poster = CreatePlayer(provider, 1200, AccountNation.Karus);
        var target = CreatePlayer(provider, 1212, AccountNation.ElMorad);

        await PostAsync(provider, poster, target.Session.Name, Reward);

        poster.Session.Money.Should().Be(StartingGold - Reward);
        LastResult(poster, PostSub).Should().Be(BountyPacketWriter.Succeeded);
        (await BountyIdOnAsync(provider, poster, target.Session.Name)).Should().NotBeNull();
    }

    [Fact]
    public async Task ABountyCannotBePostedWithoutTheGoldForIt()
    {
        using var provider = CreateProvider(_ => { });
        var poster = CreatePlayer(provider, 1201, AccountNation.Karus);
        var target = CreatePlayer(provider, 1213, AccountNation.ElMorad);
        poster.Session.Money = Reward - 1;

        await PostAsync(provider, poster, target.Session.Name, Reward);

        poster.Session.Money.Should().Be(Reward - 1);
        LastResult(poster, PostSub).Should().Be(BountyPacketWriter.Failed);
        (await BountyIdOnAsync(provider, poster, target.Session.Name)).Should().BeNull();
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

    [Fact]
    public async Task ABountyOnSomeoneWhoDoesNotExistIsRefused()
    {
        using var provider = CreateProvider(_ => { });
        var poster = CreatePlayer(provider, 1214, AccountNation.Karus);

        await PostAsync(provider, poster, "NoSuchCharacter", Reward);

        poster.Session.Money.Should().Be(StartingGold, "a typo must not lock the gold away");
        LastResult(poster, PostSub).Should().Be(BountyPacketWriter.Failed);
    }

    [Fact]
    public async Task OnlyThePosterWithdrawsABountyAndGetsTheGoldBack()
    {
        using var provider = CreateProvider(_ => { });
        var poster = CreatePlayer(provider, 1215, AccountNation.Karus);
        var target = CreatePlayer(provider, 1216, AccountNation.ElMorad);
        var other = CreatePlayer(provider, 1217, AccountNation.Karus);
        await PostAsync(provider, poster, target.Session.Name, Reward);
        var bountyId = (await BountyIdOnAsync(provider, poster, target.Session.Name))!.Value;

        await WithdrawAsync(provider, other, bountyId);
        other.Session.Money.Should().Be(StartingGold);
        LastResult(other, WithdrawSub).Should().Be(BountyPacketWriter.Failed);

        await WithdrawAsync(provider, poster, bountyId);
        poster.Session.Money.Should().Be(StartingGold);
        LastResult(poster, WithdrawSub).Should().Be(BountyPacketWriter.Succeeded);
        (await BountyIdOnAsync(provider, poster, target.Session.Name)).Should().BeNull();
    }

    [Fact]
    public async Task AnOpenBountySurvivesARestart()
    {
        using var provider = CreateProvider(_ => { });
        var poster = CreatePlayer(provider, 1218, AccountNation.Karus);
        var target = CreatePlayer(provider, 1219, AccountNation.ElMorad);
        await PostAsync(provider, poster, target.Session.Name, Reward);

        var restarted = ActivatorUtilities.CreateInstance<BountyPacketCoordinator>(provider);
        var packet = new Packet(GameOpcodes.GS_BOUNTY);
        packet.WriteByte(ListSub);
        await restarted.HandleAsync(poster.Client, packet);

        var list = poster.Sent.Last(sent => sent.GetOpcode() == (byte)GameOpcodes.GS_BOUNTY);
        list.ResetOffset();
        list.ReadByte();
        list.ReadUShort().Should().Be(1, "the reward taken from the poster is still on the board after a restart");
    }

    [Fact]
    public async Task AnUnclaimedBountyExpiresAndItsGoldIsMailedBack()
    {
        var clock = new Clock();
        using var provider = CreateProvider(_ => { });
        var board = ActivatorUtilities.CreateInstance<BountyPacketCoordinator>(provider, (TimeProvider)clock);
        var poster = CreatePlayer(provider, 1220, AccountNation.Karus);
        var target = CreatePlayer(provider, 1221, AccountNation.ElMorad);
        var post = new Packet(GameOpcodes.GS_BOUNTY);
        post.WriteByte(PostSub);
        post.WriteSByteString(target.Session.Name);
        post.WriteInt(Reward);
        await board.HandleAsync(poster.Client, post);

        clock.Now = clock.Now.AddDays(ExpiryDays);
        var list = new Packet(GameOpcodes.GS_BOUNTY);
        list.WriteByte(ListSub);
        await board.HandleAsync(poster.Client, list);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Bounties.CountAsync()).Should().Be(0);
        var mail = await db.Mails.Include(m => m.Attachments).SingleAsync(m => m.RecipientCharacterId == poster.Session.CharacterId);
        mail.Attachments.Single().Count.Should().Be(Reward);
    }

    [Fact]
    public async Task ThePostersGoldIsSavedTogetherWithTheBounty()
    {
        using var provider = CreateProvider(_ => { });
        var poster = CreatePlayer(provider, 1222, AccountNation.Karus);
        var target = CreatePlayer(provider, 1223, AccountNation.ElMorad);

        await PostAsync(provider, poster, target.Session.Name, Reward);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Characters.SingleAsync(c => c.Id == poster.Session.CharacterId)).Money
            .Should().Be(StartingGold - Reward, "a crash after the post must not give the poster the gold back as well");
    }

    [Fact]
    public async Task APostWhoseSaveFailsTakesNoGoldAndPostsNothing()
    {
        var faults = new SaveFaults();
        using var provider = CreateProvider(_ => { },
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var poster = CreatePlayer(provider, 1224, AccountNation.Karus);
        var target = CreatePlayer(provider, 1225, AccountNation.ElMorad);
        faults.Failing = true;

        await PostAsync(provider, poster, target.Session.Name, Reward);

        faults.Failing = false;
        poster.Session.Money.Should().Be(StartingGold);
        LastResult(poster, PostSub).Should().Be(BountyPacketWriter.Failed);
        (await BountyIdOnAsync(provider, poster, target.Session.Name)).Should().BeNull();
    }

    [Fact]
    public async Task AClaimWhoseSaveFailsPaysNothingAndKeepsTheBounty()
    {
        var faults = new SaveFaults();
        using var provider = CreateProvider(_ => { },
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var poster = CreatePlayer(provider, 1229, AccountNation.Karus);
        var target = CreatePlayer(provider, 1230, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1231, AccountNation.Karus);
        await PostAsync(provider, poster, target.Session.Name, Reward);
        var bountyId = (await BountyIdOnAsync(provider, poster, target.Session.Name))!.Value;
        target.Session.Hp = 0;
        await provider.GetRequiredService<ICombatLifecycleService>().HandlePlayerDeathAsync(target.Session, hunter.Session);
        faults.Failing = true;

        await ClaimAsync(provider, hunter, bountyId);

        faults.Failing = false;
        hunter.Session.Money.Should().Be(StartingGold);
        LastResult(hunter, ClaimSub).Should().Be(BountyPacketWriter.Failed);
        (await BountyIdOnAsync(provider, poster, target.Session.Name)).Should().Be(bountyId);
    }

    [Fact]
    public async Task ABountyAlreadyEarnedCannotBeWithdrawn()
    {
        using var provider = CreateProvider(_ => { });
        var poster = CreatePlayer(provider, 1226, AccountNation.Karus);
        var target = CreatePlayer(provider, 1227, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1228, AccountNation.Karus);
        await PostAsync(provider, poster, target.Session.Name, Reward);
        var bountyId = (await BountyIdOnAsync(provider, poster, target.Session.Name))!.Value;
        target.Session.Hp = 0;
        await provider.GetRequiredService<ICombatLifecycleService>().HandlePlayerDeathAsync(target.Session, hunter.Session);

        await WithdrawAsync(provider, poster, bountyId);
        await ClaimAsync(provider, hunter, bountyId);

        poster.Session.Money.Should().Be(StartingGold - Reward, "the hunter has earned it");
        hunter.Session.Money.Should().Be(StartingGold + Reward);
    }

    [Fact]
    public async Task AHuntersClaimSurvivesARestartAndBlocksAWithdrawal()
    {
        using var provider = CreateProvider(_ => { });
        var poster = CreatePlayer(provider, 1232, AccountNation.Karus);
        var target = CreatePlayer(provider, 1233, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1234, AccountNation.Karus);
        await PostAsync(provider, poster, target.Session.Name, Reward);
        var bountyId = (await BountyIdOnAsync(provider, poster, target.Session.Name))!.Value;
        target.Session.Hp = 0;
        await provider.GetRequiredService<ICombatLifecycleService>().HandlePlayerDeathAsync(target.Session, hunter.Session);

        var restarted = ActivatorUtilities.CreateInstance<BountyPacketCoordinator>(provider);
        await restarted.HandleAsync(poster.Client, BountyPacket(WithdrawSub, bountyId));
        await restarted.HandleAsync(hunter.Client, BountyPacket(ClaimSub, bountyId));

        poster.Session.Money.Should().Be(StartingGold - Reward, "the poster cannot take back a bounty a hunter has earned");
        hunter.Session.Money.Should().Be(StartingGold + Reward, "the kill still counts after a restart");
    }

    [Fact]
    public async Task AnEarnedBountyDoesNotExpire()
    {
        var clock = new Clock();
        using var provider = CreateProvider(_ => { });
        var board = ActivatorUtilities.CreateInstance<BountyPacketCoordinator>(provider, (TimeProvider)clock);
        var poster = CreatePlayer(provider, 1235, AccountNation.Karus);
        var target = CreatePlayer(provider, 1236, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1237, AccountNation.Karus);
        var post = new Packet(GameOpcodes.GS_BOUNTY);
        post.WriteByte(PostSub);
        post.WriteSByteString(target.Session.Name);
        post.WriteInt(Reward);
        await board.HandleAsync(poster.Client, post);
        await board.OnPlayerKilledAsync(target.Session, hunter.Session);

        clock.Now = clock.Now.AddDays(ExpiryDays);
        await board.HandleAsync(poster.Client, BountyPacket(ListSub));
        var list = poster.Sent.Last(sent => sent.GetOpcode() == (byte)GameOpcodes.GS_BOUNTY);
        list.ResetOffset();
        list.ReadByte();

        list.ReadUShort().Should().Be(1, "the hunter's reward is not handed back to the poster");
    }

    private static Packet BountyPacket(byte sub, int? bountyId = null)
    {
        var packet = new Packet(GameOpcodes.GS_BOUNTY);
        packet.WriteByte(sub);
        if (bountyId is { } id)
            packet.WriteInt(id);
        return packet;
    }

    private sealed class SaveFaults : SaveChangesInterceptor
    {
        public bool Failing { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Failing ? throw new DbUpdateException("the database went away") : base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
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

    private static async Task WithdrawAsync(IServiceProvider provider, Player poster, int bountyId)
    {
        var packet = new Packet(GameOpcodes.GS_BOUNTY);
        packet.WriteByte(WithdrawSub);
        packet.WriteInt(bountyId);
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
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Characters.Add(new Character { Id = characterId, AccountId = characterId + 1000, Name = session.Name });
        db.SaveChanges();
        return new Player(session, client, sent);
    }
}
