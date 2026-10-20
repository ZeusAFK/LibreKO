using System.Linq.Expressions;
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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
    private const int SmallReward = 100;
    private const int OpenBountyLimit = 5;
    private const int ListedLimit = 200;
    private const int SeededOthers = 250;
    private static readonly TimeSpan DeathWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PastTheLoadRest = TimeSpan.FromSeconds(BountyPacketCoordinator.LoadRetrySeconds + 1);
    private const int KillsWhileDown = 10;

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
    public async Task ABountyOnADeletedCharacterIsRefused()
    {
        using var provider = CreateProvider(_ => { });
        var poster = CreatePlayer(provider, 1238, AccountNation.Karus);
        var target = CreatePlayer(provider, 1239, AccountNation.ElMorad);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Characters.SingleAsync(c => c.Id == target.Session.CharacterId)).DeletionTime = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await PostAsync(provider, poster, target.Session.Name, Reward);

        poster.Session.Money.Should().Be(StartingGold, "a deleted character can never be hunted");
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
        await provider.GetRequiredService<IBountyPacketCoordinator>().HandleAsync(poster.Client, BountyPacket(ListSub));

        var restarted = ActivatorUtilities.CreateInstance<BountyPacketCoordinator>(provider);
        await restarted.HandleAsync(poster.Client, BountyPacket(WithdrawSub, bountyId));
        await restarted.HandleAsync(hunter.Client, BountyPacket(ClaimSub, bountyId));

        poster.Session.Money.Should().Be(StartingGold - Reward, "the poster cannot take back a bounty a hunter has earned");
        hunter.Session.Money.Should().Be(StartingGold + Reward, "the kill still counts after a restart");
    }

    [Fact]
    public async Task AnEarnedBountyThatRunsOutIsMailedToItsHunter()
    {
        var clock = new Clock();
        using var provider = CreateProvider(_ => { });
        var board = ActivatorUtilities.CreateInstance<BountyPacketCoordinator>(provider, (TimeProvider)clock);
        var poster = CreatePlayer(provider, 1235, AccountNation.Karus);
        var target = CreatePlayer(provider, 1236, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1237, AccountNation.Karus);
        await board.HandleAsync(poster.Client, PostPacket(target.Session.Name, Reward));
        board.RecordKill(target.Session, hunter.Session);

        clock.Now = clock.Now.AddDays(ExpiryDays);
        await board.HandleAsync(poster.Client, BountyPacket(ListSub));

        (await GoldMailedToAsync(provider, hunter)).Should().Be(Reward, "the hunter earned it, claimed or not");
        (await GoldMailedToAsync(provider, poster)).Should().Be(0, "the hunter's reward is not handed back to the poster");
    }

    [Fact]
    public async Task ADeathDoesNotWaitForTheBoardToSaveTheKill()
    {
        var saves = new HeldSaves(context => context.ChangeTracker.Entries<BountyHunter>().Any());
        using var provider = CreateProvider(_ => { },
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(saves)));
        var poster = CreatePlayer(provider, 1240, AccountNation.Karus);
        var target = CreatePlayer(provider, 1241, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1242, AccountNation.Karus);
        await PostAsync(provider, poster, target.Session.Name, Reward);
        var bountyId = (await BountyIdOnAsync(provider, poster, target.Session.Name))!.Value;
        saves.Holding = true;
        target.Session.Hp = 0;

        var death = Task.Run(() => provider.GetRequiredService<ICombatLifecycleService>()
            .HandlePlayerDeathAsync(target.Session, hunter.Session));

        (await Task.WhenAny(death, Task.Delay(DeathWait))).Should().BeSameAs(death, "a slow database must not hold up a death");
        saves.Release();
        await ClaimAsync(provider, hunter, bountyId);
        hunter.Session.Money.Should().Be(StartingGold + Reward, "the kill is saved once the database answers");
    }

    [Fact]
    public async Task APosterKeepsNoMoreThanTheLimitOpen()
    {
        using var provider = CreateProvider(_ => { });
        var poster = CreatePlayer(provider, 1243, AccountNation.Karus);
        var target = CreatePlayer(provider, 1244, AccountNation.ElMorad);

        for (var post = 0; post <= OpenBountyLimit; post++)
            await PostAsync(provider, poster, target.Session.Name, SmallReward);

        poster.Session.Money.Should().Be(StartingGold - OpenBountyLimit * SmallReward);
        LastResult(poster, PostSub).Should().Be(BountyPacketWriter.Failed);
    }

    [Fact]
    public async Task AKillAfterTheBountyRanOutEarnsNothing()
    {
        var clock = new Clock();
        using var provider = CreateProvider(_ => { });
        var board = ActivatorUtilities.CreateInstance<BountyPacketCoordinator>(provider, (TimeProvider)clock);
        var poster = CreatePlayer(provider, 1245, AccountNation.Karus);
        var target = CreatePlayer(provider, 1246, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1247, AccountNation.Karus);
        await board.HandleAsync(poster.Client, PostPacket(target.Session.Name, Reward));
        var bountyId = await StoredBountyIdAsync(provider);

        clock.Now = clock.Now.AddDays(ExpiryDays);
        board.RecordKill(target.Session, hunter.Session);
        await board.HandleAsync(hunter.Client, BountyPacket(ClaimSub, bountyId));

        hunter.Session.Money.Should().Be(StartingGold, "the bounty had run out before the kill");
    }

    [Fact]
    public async Task AKillFromBeforeTheBountyWasPostedEarnsNothing()
    {
        var clock = new Clock();
        var saves = new HeldSaves(context => context.ChangeTracker.Entries<Bounty>().Any(entry => entry.State == EntityState.Added));
        using var provider = CreateProvider(_ => { },
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(saves)));
        var board = ActivatorUtilities.CreateInstance<BountyPacketCoordinator>(provider, (TimeProvider)clock);
        var poster = CreatePlayer(provider, 1248, AccountNation.Karus);
        var target = CreatePlayer(provider, 1249, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1250, AccountNation.Karus);
        var killedAt = clock.Now;
        clock.Now = killedAt.AddMinutes(1);
        saves.Holding = true;

        var post = board.HandleAsync(poster.Client, PostPacket(target.Session.Name, Reward));
        await saves.Reached;
        clock.Now = killedAt;
        board.RecordKill(target.Session, hunter.Session);
        saves.Release();
        await post;
        await board.HandleAsync(hunter.Client, BountyPacket(ClaimSub, await StoredBountyIdAsync(provider)));

        hunter.Session.Money.Should().Be(StartingGold, "the kill came before the bounty, though it was recorded during the post");
    }

    [Fact]
    public async Task AKillWhoseSaveFailedStillEarnsTheBounty()
    {
        var clock = new Clock();
        var faults = new SaveFaults { Applies = context => context.ChangeTracker.Entries<BountyHunter>().Any() };
        using var provider = CreateProvider(_ => { },
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var board = ActivatorUtilities.CreateInstance<BountyPacketCoordinator>(provider, (TimeProvider)clock);
        var poster = CreatePlayer(provider, 1251, AccountNation.Karus);
        var target = CreatePlayer(provider, 1252, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1253, AccountNation.Karus);
        await board.HandleAsync(poster.Client, PostPacket(target.Session.Name, Reward));
        var bountyId = await StoredBountyIdAsync(provider);
        faults.Failing = true;
        board.RecordKill(target.Session, hunter.Session);
        await faults.Thrown;
        clock.Now = clock.Now.AddDays(ExpiryDays);
        await board.HandleAsync(poster.Client, BountyPacket(ListSub));
        (await GoldMailedToAsync(provider, poster)).Should().Be(0, "a bounty does not run out while a kill on its target waits for its save");
        faults.Failing = false;

        await board.HandleAsync(hunter.Client, BountyPacket(ClaimSub, bountyId));

        (await GoldMailedToAsync(provider, hunter)).Should().Be(Reward, "the kill waited for the database, and the bounty that ran out meanwhile went to its hunter");
        (await GoldMailedToAsync(provider, poster)).Should().Be(0);
    }

    [Fact]
    public async Task APosterCannotWithdrawWhileAKillOnTheTargetWaitsForItsSave()
    {
        var faults = new SaveFaults { Applies = context => context.ChangeTracker.Entries<BountyHunter>().Any() };
        using var provider = CreateProvider(_ => { },
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var poster = CreatePlayer(provider, 1257, AccountNation.Karus);
        var target = CreatePlayer(provider, 1258, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1259, AccountNation.Karus);
        await PostAsync(provider, poster, target.Session.Name, Reward);
        var bountyId = await StoredBountyIdAsync(provider);
        faults.Failing = true;
        provider.GetRequiredService<IBountyPacketCoordinator>().RecordKill(target.Session, hunter.Session);
        await faults.Thrown;

        await WithdrawAsync(provider, poster, bountyId);

        poster.Session.Money.Should().Be(StartingGold - Reward, "the hunter may have earned it");
        LastResult(poster, WithdrawSub).Should().Be(BountyPacketWriter.Failed);
    }

    [Fact]
    public async Task AKillAlreadyStoredIsNotStoredTwice()
    {
        using var provider = CreateProvider(_ => { });
        var poster = CreatePlayer(provider, 1254, AccountNation.Karus);
        var target = CreatePlayer(provider, 1255, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1256, AccountNation.Karus);
        await PostAsync(provider, poster, target.Session.Name, Reward);
        var bountyId = await StoredBountyIdAsync(provider);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.BountyHunters.Add(new BountyHunter { BountyId = bountyId, CharacterId = hunter.Session.CharacterId });
            await db.SaveChangesAsync();
        }

        provider.GetRequiredService<IBountyPacketCoordinator>().RecordKill(target.Session, hunter.Session);
        await ClaimAsync(provider, hunter, bountyId);

        hunter.Session.Money.Should().Be(StartingGold + Reward, "a kill whose save was written but not confirmed is retried without a duplicate");
    }

    [Fact]
    public async Task OnlyTheFirstKillEarnsABounty()
    {
        using var provider = CreateProvider(_ => { });
        var poster = CreatePlayer(provider, 1260, AccountNation.Karus);
        var target = CreatePlayer(provider, 1261, AccountNation.ElMorad);
        var first = CreatePlayer(provider, 1262, AccountNation.Karus);
        var second = CreatePlayer(provider, 1263, AccountNation.Karus);
        await PostAsync(provider, poster, target.Session.Name, Reward);
        var bountyId = await StoredBountyIdAsync(provider);
        var board = provider.GetRequiredService<IBountyPacketCoordinator>();

        board.RecordKill(target.Session, first.Session);
        board.RecordKill(target.Session, second.Session);
        await ClaimAsync(provider, second, bountyId);
        await ClaimAsync(provider, first, bountyId);

        second.Session.Money.Should().Be(StartingGold, "the bounty was already earned when the second kill came");
        first.Session.Money.Should().Be(StartingGold + Reward);
    }

    [Fact]
    public async Task AnEarnedBountyCountsTowardThePostersLimitUntilItIsPaid()
    {
        using var provider = CreateProvider(_ => { });
        var poster = CreatePlayer(provider, 1264, AccountNation.Karus);
        var target = CreatePlayer(provider, 1265, AccountNation.ElMorad);
        var other = CreatePlayer(provider, 1266, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1267, AccountNation.Karus);
        for (var post = 0; post < OpenBountyLimit; post++)
            await PostAsync(provider, poster, target.Session.Name, SmallReward);
        provider.GetRequiredService<IBountyPacketCoordinator>().RecordKill(target.Session, hunter.Session);

        await PostAsync(provider, poster, other.Session.Name, SmallReward);
        LastResult(poster, PostSub).Should().Be(BountyPacketWriter.Failed, "a kill must not free the limit, or alts could fill the board");

        await ClaimAsync(provider, hunter, (await BountyIdOnAsync(provider, poster, target.Session.Name))!.Value);
        await PostAsync(provider, poster, other.Session.Name, SmallReward);
        LastResult(poster, PostSub).Should().Be(BountyPacketWriter.Succeeded, "a paid bounty leaves the board");
    }

    [Fact]
    public async Task AStuckKillHoldsUpOnlyItsOwnTarget()
    {
        var clock = new Clock();
        var stuckBountyId = 0;
        var faults = new SaveFaults
        {
            Applies = context => context.ChangeTracker.Entries<BountyHunter>().Any(entry => entry.Entity.BountyId == stuckBountyId),
        };
        using var provider = CreateProvider(_ => { },
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var board = ActivatorUtilities.CreateInstance<BountyPacketCoordinator>(provider, (TimeProvider)clock);
        var poster = CreatePlayer(provider, 1268, AccountNation.Karus);
        var stuck = CreatePlayer(provider, 1269, AccountNation.ElMorad);
        var unhunted = CreatePlayer(provider, 1270, AccountNation.ElMorad);
        var later = CreatePlayer(provider, 1271, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1272, AccountNation.Karus);
        await board.HandleAsync(poster.Client, PostPacket(stuck.Session.Name, Reward));
        await board.HandleAsync(poster.Client, PostPacket(unhunted.Session.Name, SmallReward));
        stuckBountyId = (await BountyIdOnAsync(board, poster, stuck.Session.Name))!.Value;
        faults.Failing = true;
        board.RecordKill(stuck.Session, hunter.Session);
        await faults.Thrown;

        clock.Now = clock.Now.AddDays(ExpiryDays);
        await board.HandleAsync(poster.Client, PostPacket(later.Session.Name, SmallReward));
        var laterId = (await BountyIdOnAsync(board, poster, later.Session.Name))!.Value;
        board.RecordKill(later.Session, hunter.Session);
        await board.HandleAsync(hunter.Client, BountyPacket(ClaimSub, laterId));

        hunter.Session.Money.Should().Be(StartingGold + SmallReward, "a later kill on another target is not queued behind the stuck one");
        (await GoldMailedToAsync(provider, poster)).Should().Be(SmallReward, "the unhunted bounty still runs out and goes back to its poster");
        (await BountyIdOnAsync(board, poster, stuck.Session.Name)).Should().Be(stuckBountyId, "only the stuck kill's own bounty waits");
    }

    [Fact]
    public async Task ABountyOnASealedCharacterIsRefused()
    {
        using var provider = CreateProvider(_ => { });
        var poster = CreatePlayer(provider, 1273, AccountNation.Karus);
        var target = CreatePlayer(provider, 1274, AccountNation.ElMorad);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Characters.SingleAsync(c => c.Id == target.Session.CharacterId)).Sealed = true;
            await db.SaveChangesAsync();
        }

        await PostAsync(provider, poster, target.Session.Name, Reward);

        poster.Session.Money.Should().Be(StartingGold, "a sealed character cannot be hunted");
        LastResult(poster, PostSub).Should().Be(BountyPacketWriter.Failed);
    }

    [Fact]
    public async Task AnUnexpectedDatabaseErrorOnAKillDoesNotEndTheSession()
    {
        var faults = new SaveFaults
        {
            Applies = context => context.ChangeTracker.Entries<BountyHunter>().Any(),
            Error = () => new InvalidOperationException("the connection dropped"),
        };
        using var provider = CreateProvider(_ => { },
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var poster = CreatePlayer(provider, 1275, AccountNation.Karus);
        var target = CreatePlayer(provider, 1276, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1277, AccountNation.Karus);
        await PostAsync(provider, poster, target.Session.Name, Reward);
        faults.Failing = true;
        var board = provider.GetRequiredService<IBountyPacketCoordinator>();
        board.RecordKill(target.Session, hunter.Session);
        await faults.Thrown;

        var list = () => board.HandleAsync(poster.Client, BountyPacket(ListSub));

        await list.Should().NotThrowAsync("the server drops a client whose handler throws");
    }

    [Fact]
    public async Task ABoardWhoseLoadFailsHalfwayLoadsEachBountyOnceLater()
    {
        var clock = new Clock();
        var faults = new QueryFaults { Matches = query => query.Type.GenericTypeArguments.FirstOrDefault() == typeof(BountyHunter) };
        using var provider = CreateProvider(
            db => db.Bounties.Add(new Bounty
            {
                PosterCharacterId = 1278, PosterName = "Bounty1278", TargetCharacterId = 1279, TargetName = "Bounty1279",
                Reward = Reward, PostedAt = clock.Now.UtcDateTime,
            }),
            configureServices: services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults));
            });
        var poster = CreatePlayer(provider, 1278, AccountNation.Karus);
        var board = provider.GetRequiredService<IBountyPacketCoordinator>();
        faults.Failing = true;
        var failedList = () => board.HandleAsync(poster.Client, BountyPacket(ListSub));
        await failedList.Should().NotThrowAsync("a board that cannot load answers nothing rather than dropping the player");
        faults.Failing = false;
        clock.Now += PastTheLoadRest;

        await board.HandleAsync(poster.Client, BountyPacket(ListSub));

        var list = poster.Sent.Last(sent => sent.GetOpcode() == (byte)GameOpcodes.GS_BOUNTY);
        list.ResetOffset();
        list.ReadByte();
        list.ReadUShort().Should().Be(1, "the half-done first load must not leave a copy behind");
    }

    [Fact]
    public async Task ATargetLookupThatFailsRefusesThePostAndKeepsTheSession()
    {
        var faults = new QueryFaults
        {
            Matches = query => query.ToString() is var text && text.Contains(nameof(Character.Sealed)) && text.Contains(nameof(Character.DeletionTime)),
        };
        using var provider = CreateProvider(_ => { },
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var poster = CreatePlayer(provider, 1280, AccountNation.Karus);
        var target = CreatePlayer(provider, 1281, AccountNation.ElMorad);
        faults.Failing = true;

        var post = () => PostAsync(provider, poster, target.Session.Name, Reward);

        await post.Should().NotThrowAsync("the server drops a client whose handler throws");
        poster.Session.Money.Should().Be(StartingGold);
        LastResult(poster, PostSub).Should().Be(BountyPacketWriter.Failed);
    }

    [Fact]
    public async Task AnExpiryWhoseMailCannotBeSavedKeepsTheBountyAndTheSession()
    {
        var clock = new Clock();
        var faults = new SaveFaults
        {
            Applies = context => context.ChangeTracker.Entries<Mail>().Any(),
            Error = () => new InvalidOperationException("the connection dropped"),
        };
        using var provider = CreateProvider(_ => { },
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var board = ActivatorUtilities.CreateInstance<BountyPacketCoordinator>(provider, (TimeProvider)clock);
        var poster = CreatePlayer(provider, 1282, AccountNation.Karus);
        var target = CreatePlayer(provider, 1283, AccountNation.ElMorad);
        await board.HandleAsync(poster.Client, PostPacket(target.Session.Name, Reward));
        clock.Now = clock.Now.AddDays(ExpiryDays);
        faults.Failing = true;

        var list = () => board.HandleAsync(poster.Client, BountyPacket(ListSub));

        await list.Should().NotThrowAsync("the server drops a client whose handler throws");
        IdOn(poster, target.Session.Name).Should().NotBeNull("the bounty waits for a save that can return its gold");
    }

    [Fact]
    public async Task TheListShowsTheViewersOwnBountiesAndTheNewestWithinOnePacket()
    {
        using var provider = CreateProvider(db =>
        {
            db.Bounties.Add(new Bounty
            {
                PosterCharacterId = 1284, PosterName = "Bounty1284", TargetCharacterId = 1285, TargetName = "Bounty1285",
                Reward = Reward, PostedAt = DateTime.UtcNow,
            });
            db.SaveChanges();
            for (var other = 0; other < SeededOthers; other++)
            {
                db.Bounties.Add(new Bounty
                {
                    PosterCharacterId = 5000 + other, PosterName = $"Poster{other}", TargetCharacterId = 1285, TargetName = "Bounty1285",
                    Reward = SmallReward, PostedAt = DateTime.UtcNow,
                });
                db.SaveChanges();
            }
        });
        var viewer = CreatePlayer(provider, 1284, AccountNation.Karus);

        await provider.GetRequiredService<IBountyPacketCoordinator>().HandleAsync(viewer.Client, BountyPacket(ListSub));

        var list = viewer.Sent.Last(sent => sent.GetOpcode() == (byte)GameOpcodes.GS_BOUNTY);
        list.ResetOffset();
        list.ReadByte();
        list.ReadUShort().Should().Be(ListedLimit, "a list too long for one packet would break the board for everyone");
        list.ReadInt();
        list.ReadSByteString();
        list.ReadSByteString().Should().Be("Bounty1284", "the viewer's own bounties come first, however old");
        list.ReadInt();
        list.ReadByte().Should().Be(BountyPacketWriter.StandingOwn);
        list.ReadInt();
        list.ReadSByteString();
        list.ReadSByteString().Should().Be($"Poster{SeededOthers - 1}", "then the newest");
    }

    [Fact]
    public async Task TheListShowsAHuntersEarnedBountyHoweverOld()
    {
        using var provider = CreateProvider(db =>
        {
            db.Bounties.Add(new Bounty
            {
                PosterCharacterId = 1301, PosterName = "Bounty1301", TargetCharacterId = 1302, TargetName = "Bounty1302",
                Reward = Reward, PostedAt = DateTime.UtcNow,
            });
            db.SaveChanges();
            for (var other = 0; other < SeededOthers; other++)
                db.Bounties.Add(new Bounty
                {
                    PosterCharacterId = 5000 + other, PosterName = $"Poster{other}", TargetCharacterId = 1304, TargetName = "Bounty1304",
                    Reward = SmallReward, PostedAt = DateTime.UtcNow,
                });
        });
        var target = CreatePlayer(provider, 1302, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1303, AccountNation.Karus);
        var board = provider.GetRequiredService<IBountyPacketCoordinator>();
        board.RecordKill(target.Session, hunter.Session);

        await board.HandleAsync(hunter.Client, BountyPacket(ListSub));

        IdOn(hunter, "Bounty1302").Should().NotBeNull("a hunter claims from the list, so an earned bounty must be on it");
    }

    [Fact]
    public async Task AnUnreadNoticeWaitsOutsideTheBoardsLock()
    {
        var clock = new Clock();
        using var provider = CreateProvider(_ => { });
        var release = new TaskCompletionSource();
        var mail = Substitute.For<IMailService>();
        mail.SendUnreadAsync(Arg.Any<UserSession>()).Returns(release.Task);
        var board = ActivatorUtilities.CreateInstance<BountyPacketCoordinator>(provider, (TimeProvider)clock, mail);
        var poster = CreatePlayer(provider, 1305, AccountNation.Karus);
        var target = CreatePlayer(provider, 1306, AccountNation.ElMorad);
        var viewer = CreatePlayer(provider, 1307, AccountNation.Karus);
        await board.HandleAsync(poster.Client, PostPacket(target.Session.Name, Reward));
        clock.Now = clock.Now.AddDays(ExpiryDays);

        var expiring = board.HandleAsync(poster.Client, BountyPacket(ListSub));
        var other = board.HandleAsync(viewer.Client, BountyPacket(ListSub));

        (await Task.WhenAny(other, Task.Delay(DeathWait))).Should().BeSameAs(other, "a slow notice must not hold the board");
        release.SetResult();
        await expiring;
    }

    [Fact]
    public async Task AClaimWrittenButReportedAsFailedStillPays()
    {
        var faults = new FailsAfterWriting(context => context.ChangeTracker.Entries<Bounty>().Any(entry => entry.State == EntityState.Deleted));
        using var provider = CreateProvider(_ => { },
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var poster = CreatePlayer(provider, 1311, AccountNation.Karus);
        var target = CreatePlayer(provider, 1312, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1313, AccountNation.Karus);
        await PostAsync(provider, poster, target.Session.Name, Reward);
        var bountyId = await StoredBountyIdAsync(provider);
        provider.GetRequiredService<IBountyPacketCoordinator>().RecordKill(target.Session, hunter.Session);
        faults.Armed = true;

        await ClaimAsync(provider, hunter, bountyId);

        hunter.Session.Money.Should().Be(StartingGold + Reward, "the claim was written, so undoing it in memory would lose the reward at the next save");
        LastResult(hunter, ClaimSub).Should().Be(BountyPacketWriter.Succeeded);
    }

    [Fact]
    public async Task APostWrittenButReportedAsFailedStands()
    {
        var faults = new FailsAfterWriting(context => context.ChangeTracker.Entries<Bounty>().Any(entry => entry.State == EntityState.Added));
        using var provider = CreateProvider(_ => { },
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var poster = CreatePlayer(provider, 1314, AccountNation.Karus);
        var target = CreatePlayer(provider, 1315, AccountNation.ElMorad);
        faults.Armed = true;

        await PostAsync(provider, poster, target.Session.Name, Reward);

        poster.Session.Money.Should().Be(StartingGold - Reward, "a refund of a written post would hand the gold out twice");
        (await BountyIdOnAsync(provider, poster, target.Session.Name)).Should().Be(await StoredBountyIdAsync(provider));
    }

    [Fact]
    public async Task APostWhoseNewIdWasLostReadsTheBoardAgain()
    {
        var faults = new FailsAfterWriting(context => context.ChangeTracker.Entries<Bounty>().Any(entry => entry.State == EntityState.Added))
        {
            Meanwhile = context =>
            {
                foreach (var entry in context.ChangeTracker.Entries<Bounty>())
                    entry.Entity.Id = 0;
            },
        };
        using var provider = CreateProvider(_ => { },
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var poster = CreatePlayer(provider, 1316, AccountNation.Karus);
        var target = CreatePlayer(provider, 1317, AccountNation.ElMorad);
        faults.Armed = true;

        await PostAsync(provider, poster, target.Session.Name, Reward);
        var listedId = (await BountyIdOnAsync(provider, poster, target.Session.Name))!.Value;
        listedId.Should().Be(await StoredBountyIdAsync(provider), "the board read the stored row again");

        await WithdrawAsync(provider, poster, listedId);

        poster.Session.Money.Should().Be(StartingGold, "the listed bounty is the stored one, so it can be withdrawn");
    }

    [Fact]
    public async Task AClaimWhoseWriteCannotBeConfirmedIsLoggedForAHandPayout()
    {
        var faults = new FailsAfterWriting(context => context.ChangeTracker.Entries<Bounty>().Any(entry => entry.State == EntityState.Deleted));
        var check = new QueryFaults { Matches = query => query.ToString() is var text && text.Contains("c.Money") && text.Contains("c.Items") };
        using var provider = CreateProvider(_ => { },
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults, check)));
        var logger = new ListLogger();
        var board = ActivatorUtilities.CreateInstance<BountyPacketCoordinator>(provider, (ILogger<BountyPacketCoordinator>)logger);
        var poster = CreatePlayer(provider, 1308, AccountNation.Karus);
        var target = CreatePlayer(provider, 1309, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1310, AccountNation.Karus);
        await board.HandleAsync(poster.Client, PostPacket(target.Session.Name, Reward));
        var bountyId = await StoredBountyIdAsync(provider);
        board.RecordKill(target.Session, hunter.Session);
        faults.Armed = true;
        check.Failing = true;

        await board.HandleAsync(hunter.Client, BountyPacket(ClaimSub, bountyId));

        logger.Errors.Should().Contain(message => message.Contains(hunter.Session.CharacterId.ToString()) && message.Contains(Reward.ToString()),
            "a payout the database kept but memory undid can only be fixed by hand");
    }

    private sealed class ListLogger : ILogger<BountyPacketCoordinator>
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task AnUnreadNoticeThatFailsAfterAnExpiryDoesNotEndTheSession()
    {
        var clock = new Clock();
        var faults = new QueryFaults { Matches = query => query.ToString().Contains(nameof(Mail.ReadAt)) };
        using var provider = CreateProvider(_ => { },
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var board = ActivatorUtilities.CreateInstance<BountyPacketCoordinator>(provider, (TimeProvider)clock);
        var poster = CreatePlayer(provider, 1290, AccountNation.Karus);
        var target = CreatePlayer(provider, 1291, AccountNation.ElMorad);
        await board.HandleAsync(poster.Client, PostPacket(target.Session.Name, Reward));
        clock.Now = clock.Now.AddDays(ExpiryDays);
        faults.Failing = true;

        var list = () => board.HandleAsync(poster.Client, BountyPacket(ListSub));

        await list.Should().NotThrowAsync("the server drops a client whose handler throws");
        (await GoldMailedToAsync(provider, poster)).Should().Be(Reward);
    }

    [Fact]
    public async Task ALaterKillOnTheSameTargetWaitsBehindAFailedFirstKill()
    {
        const int firstId = 1294;
        var faults = new SaveFaults
        {
            Applies = context => context.ChangeTracker.Entries<BountyHunter>().Any(entry => entry.Entity.CharacterId == firstId),
        };
        using var provider = CreateProvider(_ => { },
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var poster = CreatePlayer(provider, 1292, AccountNation.Karus);
        var target = CreatePlayer(provider, 1293, AccountNation.ElMorad);
        var first = CreatePlayer(provider, firstId, AccountNation.Karus);
        var second = CreatePlayer(provider, 1295, AccountNation.Karus);
        await PostAsync(provider, poster, target.Session.Name, Reward);
        var bountyId = await StoredBountyIdAsync(provider);
        var board = provider.GetRequiredService<IBountyPacketCoordinator>();
        faults.Failing = true;
        board.RecordKill(target.Session, first.Session);
        await faults.Thrown;
        board.RecordKill(target.Session, second.Session);
        await BountyIdOnAsync(provider, poster, target.Session.Name);
        faults.Failing = false;

        await ClaimAsync(provider, second, bountyId);
        await ClaimAsync(provider, first, bountyId);

        second.Session.Money.Should().Be(StartingGold, "the first kill came first, though its save failed");
        first.Session.Money.Should().Be(StartingGold + Reward);
    }

    [Fact]
    public async Task AnExpiryWrittenButReportedAsFailedLeavesNoGhost()
    {
        var clock = new Clock();
        var faults = new FailsAfterWriting(context => context.ChangeTracker.Entries<Mail>().Any(entry => entry.State == EntityState.Added));
        using var provider = CreateProvider(_ => { },
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var board = ActivatorUtilities.CreateInstance<BountyPacketCoordinator>(provider, (TimeProvider)clock);
        var poster = CreatePlayer(provider, 1296, AccountNation.Karus);
        var target = CreatePlayer(provider, 1297, AccountNation.ElMorad);
        await board.HandleAsync(poster.Client, PostPacket(target.Session.Name, Reward));
        clock.Now = clock.Now.AddDays(ExpiryDays);
        faults.Armed = true;

        await board.HandleAsync(poster.Client, BountyPacket(ListSub));
        await board.HandleAsync(poster.Client, BountyPacket(ListSub));

        IdOn(poster, target.Session.Name).Should().BeNull("the close was written, so the bounty is gone");
        (await GoldMailedToAsync(provider, poster)).Should().Be(Reward, "the gold is mailed back once");
    }

    [Fact]
    public async Task AClaimWrittenButReportedAsFailedLeavesNoGhost()
    {
        var faults = new FailsAfterWriting(context => context.ChangeTracker.Entries<Bounty>().Any(entry => entry.State == EntityState.Deleted));
        using var provider = CreateProvider(_ => { },
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var poster = CreatePlayer(provider, 1298, AccountNation.Karus);
        var target = CreatePlayer(provider, 1299, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1300, AccountNation.Karus);
        await PostAsync(provider, poster, target.Session.Name, Reward);
        var bountyId = await StoredBountyIdAsync(provider);
        provider.GetRequiredService<IBountyPacketCoordinator>().RecordKill(target.Session, hunter.Session);
        faults.Armed = true;

        await ClaimAsync(provider, hunter, bountyId);

        (await BountyIdOnAsync(provider, poster, target.Session.Name)).Should().BeNull("the close was written, so the bounty is gone");
    }

    private sealed class QueryFaults : IQueryExpressionInterceptor
    {
        private int attempts;
        private readonly TaskCompletionSource tried = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Failing { get; set; }
        public Func<Expression, bool> Matches { get; init; } = _ => true;
        public int Attempts => Volatile.Read(ref attempts);
        public Task Tried => tried.Task;

        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        {
            if (!Failing || !Matches(queryExpression))
                return queryExpression;

            Interlocked.Increment(ref attempts);
            tried.TrySetResult();
            throw new InvalidOperationException("the connection dropped");
        }
    }

    [Fact]
    public async Task EachRowSaysWhetherItIsTheViewersOwnEarnedOrOpen()
    {
        using var provider = CreateProvider(_ => { });
        var poster = CreatePlayer(provider, 1320, AccountNation.Karus);
        var target = CreatePlayer(provider, 1321, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1322, AccountNation.Karus);
        var bystander = CreatePlayer(provider, 1323, AccountNation.Karus);
        await PostAsync(provider, poster, target.Session.Name, Reward);
        var board = provider.GetRequiredService<IBountyPacketCoordinator>();
        board.RecordKill(target.Session, hunter.Session);

        foreach (var (viewer, standing) in new[]
                 {
                     (poster, BountyPacketWriter.StandingOwn), (hunter, BountyPacketWriter.StandingEarned), (bystander, BountyPacketWriter.StandingOpen),
                 })
        {
            await board.HandleAsync(viewer.Client, BountyPacket(ListSub));
            StandingOn(viewer, target.Session.Name).Should().Be(standing, "the client picks Withdraw or Claim from this, not from a name");
        }
    }

    [Fact]
    public async Task KillsWhileTheBoardCannotLoadTryTheDatabaseOnceAndRequestsAreRefusedAtOnce()
    {
        var faults = new QueryFaults { Matches = query => query.Type.GenericTypeArguments.FirstOrDefault() == typeof(Bounty), Failing = true };
        using var provider = CreateProvider(_ => { },
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var target = CreatePlayer(provider, 1324, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1325, AccountNation.Karus);
        var board = provider.GetRequiredService<IBountyPacketCoordinator>();

        for (var kill = 0; kill < KillsWhileDown; kill++)
            board.RecordKill(target.Session, hunter.Session);
        await faults.Tried;
        await board.HandleAsync(hunter.Client, PostPacket(target.Session.Name, Reward));

        faults.Attempts.Should().Be(1, "after a failed load the board rests instead of every kill trying the database again");
        LastResult(hunter, PostSub).Should().Be(BountyPacketWriter.Failed);
        hunter.Session.Money.Should().Be(StartingGold);
    }

    [Fact]
    public async Task KillsQueuedAtShutdownAreSavedBeforeTheServerStops()
    {
        var faults = new QueryFaults { Matches = query => query.Type.GenericTypeArguments.FirstOrDefault() == typeof(Bounty), Failing = true };
        using var provider = CreateProvider(
            db => db.Bounties.Add(new Bounty
            {
                PosterCharacterId = 1326, PosterName = "Bounty1326", TargetCharacterId = 1327, TargetName = "Bounty1327",
                Reward = Reward, PostedAt = DateTime.UtcNow.AddMinutes(-1),
            }),
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var target = CreatePlayer(provider, 1327, AccountNation.ElMorad);
        var hunter = CreatePlayer(provider, 1328, AccountNation.Karus);
        var board = provider.GetRequiredService<BountyPacketCoordinator>();
        board.RecordKill(target.Session, hunter.Session);
        await faults.Tried;
        faults.Failing = false;

        await ((IHostedService)board).StopAsync(CancellationToken.None);

        using var scope = provider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().BountyHunters.CountAsync(h => h.CharacterId == hunter.Session.CharacterId))
            .Should().Be(1, "a kill still queued when the server stops is saved, not lost");
    }

    [Fact]
    public async Task APostWrittenButNotConfirmedIsTakenDownWithItsRefund()
    {
        var faults = new FailsAfterWriting(context => context.ChangeTracker.Entries<Bounty>().Any(entry => entry.State == EntityState.Added));
        var check = new QueryFaults { Matches = query => query.ToString() is var text && text.Contains("c.Money") && text.Contains("c.Items") };
        using var provider = CreateProvider(_ => { },
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults, check)));
        var poster = CreatePlayer(provider, 1329, AccountNation.Karus);
        var target = CreatePlayer(provider, 1330, AccountNation.ElMorad);
        faults.Armed = true;
        check.Failing = true;

        await PostAsync(provider, poster, target.Session.Name, Reward);

        poster.Session.Money.Should().Be(StartingGold, "the post was reported as failed and its gold came back");
        using var scope = provider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bounties.CountAsync())
            .Should().Be(0, "a bounty its poster was refunded for would pay out coins no one paid in");
    }

    private static byte? StandingOn(Player viewer, string target)
    {
        var list = viewer.Sent.Last(sent => sent.GetOpcode() == (byte)GameOpcodes.GS_BOUNTY);
        list.ResetOffset();
        list.ReadByte();
        var count = list.ReadUShort();
        for (var entry = 0; entry < count; entry++)
        {
            list.ReadInt();
            var name = list.ReadSByteString();
            list.ReadSByteString();
            list.ReadInt();
            var standing = list.ReadByte();
            if (name == target)
                return standing;
        }

        return null;
    }

    private static async Task<int> GoldMailedToAsync(IServiceProvider provider, Player player)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Mails
            .Where(mail => mail.RecipientCharacterId == player.Session.CharacterId)
            .SelectMany(mail => mail.Attachments)
            .Where(attachment => attachment.Kind == MailAttachmentKind.Gold)
            .SumAsync(attachment => attachment.Count);
    }

    private static async Task<int?> BountyIdOnAsync(BountyPacketCoordinator board, Player viewer, string target)
    {
        await board.HandleAsync(viewer.Client, BountyPacket(ListSub));
        return IdOn(viewer, target);
    }

    private static Packet PostPacket(string target, int reward)
    {
        var packet = new Packet(GameOpcodes.GS_BOUNTY);
        packet.WriteByte(PostSub);
        packet.WriteSByteString(target);
        packet.WriteInt(reward);
        return packet;
    }

    private static async Task<int> StoredBountyIdAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bounties.SingleAsync()).Id;
    }

    private sealed class HeldSaves(Func<DbContext, bool> holds) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Holding { get; set; }
        public Task Reached => reached.Task;

        public void Release() => release.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Holding && eventData.Context is { } context && holds(context))
            {
                reached.TrySetResult();
                await release.Task;
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
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
        private readonly TaskCompletionSource thrown = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Failing { get; set; }
        public Func<DbContext, bool> Applies { get; init; } = _ => true;
        public Func<Exception> Error { get; init; } = () => new DbUpdateException("the database went away");
        public Task Thrown => thrown.Task;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Failing || eventData.Context is not { } context || !Applies(context))
                return base.SavingChangesAsync(eventData, result, cancellationToken);

            thrown.TrySetResult();
            throw Error();
        }
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
        return IdOn(viewer, target);
    }

    private static int? IdOn(Player viewer, string target)
    {
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
            list.ReadByte();
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
