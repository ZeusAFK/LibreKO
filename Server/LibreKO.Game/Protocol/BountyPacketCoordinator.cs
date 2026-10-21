using System.Collections.Concurrent;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IBountyPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
    void RecordKill(UserSession victim, UserSession killer);
}

public class BountyPacketCoordinator(
    SessionManager sessionManager,
    IUserNotificationService userNotification,
    IMailService mailService,
    ICharacterStatePersister statePersister,
    IServiceScopeFactory scopeFactory,
    ILogger<BountyPacketCoordinator> logger,
    TimeProvider? timeProvider = null) : IBountyPacketCoordinator, IHostedService
{
    public const byte BountySubList = 1;
    public const byte BountySubPost = 2;
    public const byte BountySubClaim = 3;
    public const byte BountySubWithdraw = 4;

    private const int BountyMinReward = 1;
    private const int BountyMaxReward = 100_000_000;
    private const int BountyLifetimeDays = 7;
    private const int MaxOpenBountiesPerPoster = 5;
    private const int MaxListedBounties = 200;
    private const string ReturnSubject = "Bounty returned";
    private const string RewardSubject = "Bounty reward";
    public const int RetrySeconds = 30;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(RetrySeconds);
    public const int WakeSeconds = 5;
    private static readonly TimeSpan WakeDelay = TimeSpan.FromSeconds(WakeSeconds);

    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly List<Bounty> board = [];
    private readonly Dictionary<int, HashSet<int>> huntersByBounty = [];
    private readonly ConcurrentQueue<Kill> kills = new();
    private readonly List<Kill> unsaved = [];
    private readonly ConcurrentQueue<int> unreadNotices = new();
    private readonly Dictionary<int, long> restingTargets = [];
    private readonly Dictionary<int, long> restingCloses = [];
    private bool loaded;
    private long loadRetryAtTicks;
    private int draining;

    private sealed record Kill(int VictimId, int KillerId, DateTime At);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SaveBeforeStopAsync().WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("The shutdown ran out of time before {Count} bounty kill(s) were saved", kills.Count + unsaved.Count);
        }
    }

    private async Task SaveBeforeStopAsync()
    {
        await gate.WaitAsync();
        try
        {
            Interlocked.Exchange(ref loadRetryAtTicks, 0);
            restingTargets.Clear();
            restingCloses.Clear();
            await RefreshBoardAsync();
            var lost = kills.Count + unsaved.Count;
            if (lost > 0)
                logger.LogWarning("{Count} bounty kill(s) could not be saved before the shutdown", lost);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case BountySubList:
                await SendBountyListAsync(session);
                break;
            case BountySubPost:
                await HandleBountyPostAsync(session, packet);
                break;
            case BountySubClaim:
                await HandleBountyClaimAsync(session, packet);
                break;
            case BountySubWithdraw:
                await HandleBountyWithdrawAsync(session, packet);
                break;
        }
    }

    // The death path must not wait on the board's lock or the database.
    public void RecordKill(UserSession victim, UserSession killer)
    {
        kills.Enqueue(new Kill(victim.CharacterId, killer.CharacterId, time.GetUtcNow().UtcDateTime));
        _ = SaveKillsAsync();
    }

    private async Task SaveKillsAsync()
    {
        while (!kills.IsEmpty && !LoadResting && Interlocked.CompareExchange(ref draining, 1, 0) == 0)
        {
            try
            {
                await gate.WaitAsync();
                try
                {
                    await RefreshBoardAsync();
                }
                finally
                {
                    gate.Release();
                }

                await SendUnreadNoticesAsync();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not save a bounty kill; the next use of the board tries again");
                return;
            }
            finally
            {
                Interlocked.Exchange(ref draining, 0);
            }
        }
    }

    private bool LoadResting => !Volatile.Read(ref loaded) && Now < Interlocked.Read(ref loadRetryAtTicks);

    private long Now => time.GetUtcNow().UtcDateTime.Ticks;

    private long RestEnd => (time.GetUtcNow().UtcDateTime + RetryDelay).Ticks;

    private bool Resting(Dictionary<int, long> rests, int id) => rests.TryGetValue(id, out var until) && Now < until;

    private void Wake(Bounty bounty)
    {
        if (restingTargets.TryGetValue(bounty.TargetCharacterId, out var until) && Now >= until - RetryDelay.Ticks + WakeDelay.Ticks)
            restingTargets.Remove(bounty.TargetCharacterId);
    }

    private async Task SendBountyListAsync(UserSession session)
    {
        List<BountyPacketWriter.Entry>? entries = null;
        await gate.WaitAsync();
        try
        {
            if (await RefreshBoardAsync())
                entries = board.OrderByDescending(b => b.PosterCharacterId == session.CharacterId)
                    .ThenByDescending(b => HasEarned(b, session.CharacterId))
                    .ThenByDescending(b => b.Id)
                    .Take(MaxListedBounties)
                    .Select(b => new BountyPacketWriter.Entry(b.Id, b.TargetName, b.PosterName, b.Reward, StandingOf(b, session)))
                    .ToList();
        }
        finally
        {
            gate.Release();
        }

        await SendUnreadNoticesAsync();

        if (entries == null)
            return;

        await session.Client.SendPacket(BountyPacketWriter.BountyList(BountySubList, entries));
    }

    private async Task HandleBountyPostAsync(UserSession session, Packet packet)
    {
        var target = (packet.RemainingBytes >= 1 ? packet.ReadSByteString() : "").Trim();
        var reward = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;

        Bounty? posted = null;
        await gate.WaitAsync();
        try
        {
            if (await RefreshBoardAsync()
                && target.Length > 0 && reward is >= BountyMinReward and <= BountyMaxReward
                && board.Count(b => b.PosterCharacterId == session.CharacterId) < MaxOpenBountiesPerPoster
                && !string.Equals(target, session.Name, StringComparison.OrdinalIgnoreCase)
                && await FindCharacterAsync(target) is { } wanted
                && wanted.Id != session.CharacterId)
            {
                var bounty = new Bounty
                {
                    PosterCharacterId = session.CharacterId,
                    PosterName = session.Name,
                    TargetCharacterId = wanted.Id,
                    TargetName = wanted.Name,
                    Reward = reward,
                    PostedAt = time.GetUtcNow().UtcDateTime,
                };
                var outcome = await statePersister.CommitAsync(session, db =>
                {
                    if (!session.WithLock(s => TryDebit(s, reward)))
                        return Task.FromResult(false);
                    db.Bounties.Add(bounty);
                    return Task.FromResult(true);
                }, () => session.WithLock(s => GiveBack(s, reward)));
                if (outcome == StateCommit.Failed)
                    await RemoveStrayPostAsync(bounty, session);
                if (outcome == StateCommit.Saved)
                {
                    posted = bounty;
                    if (bounty.Id > 0)
                        board.Add(bounty);
                    else
                        ForgetBoard();
                }
            }
        }
        finally
        {
            gate.Release();
        }

        await SendUnreadNoticesAsync();

        if (posted == null)
        {
            await session.Client.SendPacket(BountyPacketWriter.Result(BountySubPost, BountyPacketWriter.Failed));
            return;
        }

        await userNotification.SendGoldLossAsync(session, reward);
        logger.LogDebug("{Name} posted bounty on {Target} for {Reward}", session.Name, posted.TargetName, reward);
        await session.Client.SendPacket(BountyPacketWriter.Result(BountySubPost, BountyPacketWriter.Succeeded));
        await SendBountyListAsync(session);
    }

    private async Task HandleBountyClaimAsync(UserSession session, Packet packet)
    {
        var bountyId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;

        Bounty? claimed = null;
        await gate.WaitAsync();
        try
        {
            if (board.FirstOrDefault(b => b.Id == bountyId) is { } wanted
                && unsaved.Any(kill => kill.VictimId == wanted.TargetCharacterId && kill.KillerId == session.CharacterId))
                Wake(wanted);
            var bounty = await RefreshBoardAsync() ? board.FirstOrDefault(b => b.Id == bountyId) : null;
            if (bounty != null && Hunters(bounty.Id).Contains(session.CharacterId)
                && await PayAndCloseAsync(session, bounty))
                claimed = bounty;
        }
        finally
        {
            gate.Release();
        }

        await SendUnreadNoticesAsync();

        if (claimed == null)
        {
            await session.Client.SendPacket(BountyPacketWriter.BountyResult(
                BountySubClaim, BountyPacketWriter.Failed, bountyId));
            return;
        }

        await userNotification.SendGoldGainAsync(session, claimed.Reward);
        logger.LogDebug("{Name} claimed bounty {Id} on {Target} for {Reward}",
            session.Name, claimed.Id, claimed.TargetName, claimed.Reward);
        await session.Client.SendPacket(BountyPacketWriter.BountyResult(
            BountySubClaim, BountyPacketWriter.Succeeded, bountyId));
        await SendBountyListAsync(session);
    }

    private async Task HandleBountyWithdrawAsync(UserSession session, Packet packet)
    {
        var bountyId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;

        Bounty? withdrawn = null;
        await gate.WaitAsync();
        try
        {
            if (board.FirstOrDefault(b => b.Id == bountyId && b.PosterCharacterId == session.CharacterId) is { } own)
                Wake(own);
            var bounty = await RefreshBoardAsync()
                ? board.FirstOrDefault(b => b.Id == bountyId && b.PosterCharacterId == session.CharacterId)
                : null;
            if (bounty != null && Hunters(bounty.Id).Count == 0 && !KillWaitsOn(bounty)
                && await PayAndCloseAsync(session, bounty))
                withdrawn = bounty;
        }
        finally
        {
            gate.Release();
        }

        await SendUnreadNoticesAsync();

        await session.Client.SendPacket(BountyPacketWriter.BountyResult(
            BountySubWithdraw, withdrawn != null ? BountyPacketWriter.Succeeded : BountyPacketWriter.Failed, bountyId));
        if (withdrawn == null)
            return;

        await userNotification.SendGoldGainAsync(session, withdrawn.Reward);
        await SendBountyListAsync(session);
    }

    private async Task<bool> PayAndCloseAsync(UserSession payee, Bounty bounty)
    {
        var outcome = await statePersister.CommitAsync(payee, db =>
        {
            if (!payee.WithLock(s => TryCredit(s, bounty.Reward)))
                return Task.FromResult(false);
            db.Bounties.Remove(new Bounty { Id = bounty.Id });
            db.BountyHunters.RemoveRange(HunterRows(bounty));
            return Task.FromResult(true);
        }, () => payee.WithLock(s => s.Money = Math.Max(0, s.Money - bounty.Reward)));
        if (outcome == StateCommit.Failed && await ForgetIfClosedAsync(bounty))
            logger.LogError("Bounty {Id} was closed for character {CharacterId} but the save reported a failure; unless a bounty mail already "
                + "paid them, {Reward} coins must be paid by hand: check the character's mails first", bounty.Id, payee.CharacterId, bounty.Reward);
        if (outcome != StateCommit.Saved)
            return false;

        Forget(bounty);
        return true;
    }

    // Queued kills are recorded before any bounty runs out; a failed load, kill or expiry rests before the board tries it again.
    private async Task<bool> RefreshBoardAsync()
    {
        if (!loaded)
        {
            if (LoadResting)
                return false;
            if (!await TryLoadAsync())
            {
                Interlocked.Exchange(ref loadRetryAtTicks, RestEnd);
                return false;
            }
        }

        await RecordQueuedKillsAsync();
        var expiredBefore = time.GetUtcNow().UtcDateTime.AddDays(-BountyLifetimeDays);
        foreach (var bounty in board.Where(b => b.PostedAt <= expiredBefore && !KillWaitsOn(b) && !Resting(restingCloses, b.Id)).ToList())
        {
            var hunter = Hunters(bounty.Id).Select(id => (int?)id).FirstOrDefault();
            var recipient = hunter ?? bounty.PosterCharacterId;
            if (!await TryCloseByMailAsync(bounty, recipient, hunter != null) && !await ForgetIfClosedAsync(bounty))
            {
                restingCloses[bounty.Id] = RestEnd;
                continue;
            }

            Forget(bounty);
            unreadNotices.Enqueue(recipient);
        }

        return true;
    }

    // Loaded whole or not at all.
    private async Task<bool> TryLoadAsync()
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var bounties = await db.Bounties.AsNoTracking().OrderBy(b => b.Id).ToListAsync();
            var hunters = await db.BountyHunters.AsNoTracking().ToListAsync();
            board.AddRange(bounties);
            foreach (var hunter in hunters)
                Hunters(hunter.BountyId).Add(hunter.CharacterId);
            loaded = true;
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not load the bounty board");
            return false;
        }
    }

    private async Task<bool> TryCloseByMailAsync(Bounty bounty, int recipientId, bool toHunter)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Bounties.Remove(new Bounty { Id = bounty.Id });
            db.BountyHunters.RemoveRange(HunterRows(bounty));
            mailService.StageSystemMail(db, recipientId, toHunter ? RewardSubject : ReturnSubject,
                toHunter
                    ? $"You hunted down {bounty.TargetName} but did not claim the bounty in time. Its {bounty.Reward:N0} coins are yours."
                    : $"Your bounty on {bounty.TargetName} was not claimed. Its {bounty.Reward:N0} coins are returned.",
                [new MailAttachmentDraft(MailAttachmentKind.Gold, InventoryConstants.ItemGold, bounty.Reward)]);
            await db.SaveChangesAsync();
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not close expired bounty {Id}", bounty.Id);
            return false;
        }
    }

    // Kills on one target are recorded in the order they happened.
    private async Task RecordQueuedKillsAsync()
    {
        while (kills.TryDequeue(out var queued))
            unsaved.Add(queued);

        var heldTargets = new HashSet<int>();
        foreach (var kill in unsaved.ToList())
        {
            if (heldTargets.Contains(kill.VictimId) || Resting(restingTargets, kill.VictimId))
            {
                heldTargets.Add(kill.VictimId);
                continue;
            }

            if (!await TryRecordKillAsync(kill))
            {
                heldTargets.Add(kill.VictimId);
                restingTargets[kill.VictimId] = RestEnd;
                continue;
            }

            restingTargets.Remove(kill.VictimId);
            unsaved.Remove(kill);
        }
    }

    private async Task<bool> TryRecordKillAsync(Kill kill)
    {
        foreach (var bounty in board.Where(b => b.TargetCharacterId == kill.VictimId
                     && b.PosterCharacterId != kill.KillerId
                     && b.PostedAt <= kill.At && kill.At < b.PostedAt.AddDays(BountyLifetimeDays)
                     && Hunters(b.Id).Count == 0).ToList())
        {
            if (!await TrySaveHunterAsync(bounty.Id, kill.KillerId))
                return false;
            Hunters(bounty.Id).Add(kill.KillerId);
        }

        return true;
    }

    private bool KillWaitsOn(Bounty bounty) =>
        unsaved.Any(kill => kill.VictimId == bounty.TargetCharacterId) || kills.Any(kill => kill.VictimId == bounty.TargetCharacterId);

    private IEnumerable<BountyHunter> HunterRows(Bounty bounty) =>
        Hunters(bounty.Id).Select(hunter => new BountyHunter { BountyId = bounty.Id, CharacterId = hunter }).ToList();

    // A save that failed after it was written must not fail again on its retry.
    private async Task<bool> TrySaveHunterAsync(int bountyId, int characterId)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.BountyHunters.AnyAsync(hunter => hunter.BountyId == bountyId && hunter.CharacterId == characterId))
            {
                db.BountyHunters.Add(new BountyHunter { BountyId = bountyId, CharacterId = characterId });
                await db.SaveChangesAsync();
            }

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not save the kill on bounty {Id}; it is tried again on the board's next use", bountyId);
            return false;
        }
    }

    private async Task<bool> ForgetIfClosedAsync(Bounty bounty)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db.Bounties.AnyAsync(b => b.Id == bounty.Id))
                return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not check whether bounty {Id} is closed", bounty.Id);
            return false;
        }

        Forget(bounty);
        return true;
    }

    private async Task SendUnreadNoticesAsync()
    {
        while (unreadNotices.TryDequeue(out var characterId))
        {
            if (sessionManager.GetByCharacterId(characterId) is not { } online)
                continue;

            try
            {
                await mailService.SendUnreadAsync(online);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Could not tell {Name} of a bounty mail", online.Name);
            }
        }
    }

    private void ForgetBoard()
    {
        board.Clear();
        huntersByBounty.Clear();
        loaded = false;
    }

    private void Forget(Bounty bounty)
    {
        board.Remove(bounty);
        huntersByBounty.Remove(bounty.Id);
        restingCloses.Remove(bounty.Id);
    }

    private async Task<(int Id, string Name)?> FindCharacterAsync(string name)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var found = await db.Characters.AsNoTracking()
                .Where(c => c.Name == name && c.DeletionTime == null && !c.Sealed)
                .Select(c => new { c.Id, c.Name })
                .FirstOrDefaultAsync();
            return found == null ? null : (found.Id, found.Name);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not look up bounty target {Name}", name);
            return null;
        }
    }

    // A post reported as failed may still have been written; its refunded poster must not also have a live bounty.
    private async Task RemoveStrayPostAsync(Bounty bounty, UserSession poster)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var strays = await db.Bounties.Where(b => b.PosterCharacterId == bounty.PosterCharacterId
                && b.TargetCharacterId == bounty.TargetCharacterId && b.PostedAt == bounty.PostedAt).ToListAsync();
            if (strays.Count == 0)
                return;

            db.Bounties.RemoveRange(strays);
            await db.SaveChangesAsync();
            logger.LogWarning("A bounty posted by {Name} was written although its save reported a failure; it was taken down", poster.Name);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "A bounty posted by {Name} on {Target} for {Reward} at {PostedAt} may be stored although the poster kept "
                + "the coins; delete it by hand if it is", poster.Name, bounty.TargetName, bounty.Reward, bounty.PostedAt);
        }
    }

    private byte StandingOf(Bounty bounty, UserSession viewer) =>
        bounty.PosterCharacterId == viewer.CharacterId ? BountyPacketWriter.StandingOwn
        : HasEarned(bounty, viewer.CharacterId) ? BountyPacketWriter.StandingEarned
        : BountyPacketWriter.StandingOpen;

    private bool HasEarned(Bounty bounty, int characterId) =>
        huntersByBounty.TryGetValue(bounty.Id, out var hunters) && hunters.Contains(characterId);

    private HashSet<int> Hunters(int bountyId)
    {
        if (!huntersByBounty.TryGetValue(bountyId, out var hunters))
        {
            hunters = [];
            huntersByBounty[bountyId] = hunters;
        }

        return hunters;
    }

    private static bool TryDebit(UserSession session, int amount)
    {
        if (session.Money < amount)
            return false;

        session.Money -= amount;
        return true;
    }

    private static void GiveBack(UserSession session, int amount) =>
        session.Money = (int)Math.Min((long)session.Money + amount, ExchangePacketConstants.CoinMax);

    private static bool TryCredit(UserSession session, int amount)
    {
        if ((long)session.Money + amount > ExchangePacketConstants.CoinMax)
            return false;

        session.Money += amount;
        return true;
    }
}
