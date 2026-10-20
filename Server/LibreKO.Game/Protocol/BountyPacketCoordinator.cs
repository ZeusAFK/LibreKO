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
    TimeProvider? timeProvider = null) : IBountyPacketCoordinator
{
    public const byte BountySubList = 1;
    public const byte BountySubPost = 2;
    public const byte BountySubClaim = 3;
    public const byte BountySubWithdraw = 4;

    private const int BountyMinReward = 1;
    private const int BountyMaxReward = 100_000_000;
    private const int BountyLifetimeDays = 7;
    private const int MaxOpenBountiesPerPoster = 5;
    private const string ReturnSubject = "Bounty returned";

    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly List<Bounty> board = [];
    private readonly Dictionary<int, HashSet<int>> huntersByBounty = [];
    private readonly ConcurrentQueue<Kill> kills = new();
    private bool loaded;

    private sealed record Kill(int VictimId, int KillerId, DateTime At);

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
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not save a bounty kill; the next use of the board tries again");
        }
    }

    private async Task SendBountyListAsync(UserSession session)
    {
        List<BountyPacketWriter.Entry> entries;
        await gate.WaitAsync();
        try
        {
            await RefreshBoardAsync();
            entries = board
                .Select(b => new BountyPacketWriter.Entry(b.Id, b.TargetName, b.PosterName, b.Reward))
                .ToList();
        }
        finally
        {
            gate.Release();
        }

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
            await RefreshBoardAsync();
            if (target.Length > 0 && reward is >= BountyMinReward and <= BountyMaxReward
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
                if (outcome == StateCommit.Saved)
                {
                    board.Add(bounty);
                    posted = bounty;
                }
            }
        }
        finally
        {
            gate.Release();
        }

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
            await RefreshBoardAsync();
            var bounty = board.FirstOrDefault(b => b.Id == bountyId);
            if (bounty != null && Hunters(bounty.Id).Contains(session.CharacterId)
                && await PayAndCloseAsync(session, bounty))
                claimed = bounty;
        }
        finally
        {
            gate.Release();
        }

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
            await RefreshBoardAsync();
            var bounty = board.FirstOrDefault(b => b.Id == bountyId && b.PosterCharacterId == session.CharacterId);
            // A kill still waiting for its save may have earned the bounty.
            if (bounty != null && Hunters(bounty.Id).Count == 0 && !kills.Any(kill => kill.VictimId == bounty.TargetCharacterId)
                && await PayAndCloseAsync(session, bounty))
                withdrawn = bounty;
        }
        finally
        {
            gate.Release();
        }

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
        if (outcome != StateCommit.Saved)
            return false;

        Forget(bounty);
        return true;
    }

    // Kills are recorded before expiry, so a bounty earned in time never goes back to its poster.
    private async Task RefreshBoardAsync()
    {
        if (!loaded)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            board.AddRange(await db.Bounties.AsNoTracking().OrderBy(b => b.Id).ToListAsync());
            foreach (var hunter in await db.BountyHunters.AsNoTracking().ToListAsync())
                Hunters(hunter.BountyId).Add(hunter.CharacterId);
            loaded = true;
        }

        if (!await RecordQueuedKillsAsync())
            return;

        var expiredBefore = time.GetUtcNow().UtcDateTime.AddDays(-BountyLifetimeDays);
        foreach (var bounty in board.Where(b => b.PostedAt <= expiredBefore && Hunters(b.Id).Count == 0).ToList())
        {
            if (!await TryReturnByMailAsync(bounty))
                continue;

            Forget(bounty);
            if (sessionManager.GetByCharacterId(bounty.PosterCharacterId) is { } poster)
                await mailService.SendUnreadAsync(poster);
        }
    }

    private async Task<bool> TryReturnByMailAsync(Bounty bounty)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Bounties.Remove(new Bounty { Id = bounty.Id });
            mailService.StageSystemMail(db, bounty.PosterCharacterId, ReturnSubject,
                $"Your bounty on {bounty.TargetName} was not claimed. Its {bounty.Reward:N0} coins are returned.",
                [new MailAttachmentDraft(MailAttachmentKind.Gold, InventoryConstants.ItemGold, bounty.Reward)]);
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Could not return expired bounty {Id}", bounty.Id);
            return false;
        }
    }

    private async Task<bool> RecordQueuedKillsAsync()
    {
        while (kills.TryPeek(out var kill))
        {
            foreach (var bounty in board.Where(b => b.TargetCharacterId == kill.VictimId
                         && b.PosterCharacterId != kill.KillerId
                         && b.PostedAt <= kill.At && kill.At < b.PostedAt.AddDays(BountyLifetimeDays)
                         && !Hunters(b.Id).Contains(kill.KillerId)).ToList())
            {
                if (!await TrySaveHunterAsync(bounty.Id, kill.KillerId))
                    return false;
                Hunters(bounty.Id).Add(kill.KillerId);
            }

            kills.TryDequeue(out _);
        }

        return true;
    }

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
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Could not save the kill on bounty {Id}; it is tried again on the board's next use", bountyId);
            return false;
        }
    }

    private void Forget(Bounty bounty)
    {
        board.Remove(bounty);
        huntersByBounty.Remove(bounty.Id);
    }

    private async Task<(int Id, string Name)?> FindCharacterAsync(string name)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var found = await db.Characters.AsNoTracking()
            .Where(c => c.Name == name && c.DeletionTime == null)
            .Select(c => new { c.Id, c.Name })
            .FirstOrDefaultAsync();
        return found == null ? null : (found.Id, found.Name);
    }

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
