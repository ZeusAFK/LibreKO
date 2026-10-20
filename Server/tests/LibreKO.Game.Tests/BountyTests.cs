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
    private const int SmallReward = 100;
    private const int OpenBountyLimit = 5;
    private static readonly TimeSpan DeathWait = TimeSpan.FromSeconds(5);

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
        // The board saves every recorded kill before its next use.
        await provider.GetRequiredService<IBountyPacketCoordinator>().HandleAsync(poster.Client, BountyPacket(ListSub));

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
        board.RecordKill(target.Session, hunter.Session);

        clock.Now = clock.Now.AddDays(ExpiryDays);
        await board.HandleAsync(poster.Client, BountyPacket(ListSub));
        var list = poster.Sent.Last(sent => sent.GetOpcode() == (byte)GameOpcodes.GS_BOUNTY);
        list.ResetOffset();
        list.ReadByte();

        list.ReadUShort().Should().Be(1, "the hunter's reward is not handed back to the poster");
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
    public async Task AKillWhoseSaveFailedStillEarnsTheBountyAndKeepsItFromExpiring()
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
        faults.Failing = false;

        await board.HandleAsync(hunter.Client, BountyPacket(ClaimSub, bountyId));

        hunter.Session.Money.Should().Be(StartingGold + Reward, "the kill waited for the database and the bounty waited for the kill");
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
        public Task Thrown => thrown.Task;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Failing || eventData.Context is not { } context || !Applies(context))
                return base.SavingChangesAsync(eventData, result, cancellationToken);

            thrown.TrySetResult();
            throw new DbUpdateException("the database went away");
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
