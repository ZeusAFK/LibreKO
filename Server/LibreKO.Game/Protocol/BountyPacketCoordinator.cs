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
    Task OnPlayerKilledAsync(UserSession victim, UserSession killer);
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
    private const string ReturnSubject = "Bounty returned";

    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly List<Bounty> board = [];
    private readonly Dictionary<int, HashSet<int>> huntersByBounty = [];
    private bool loaded;

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

    public async Task OnPlayerKilledAsync(UserSession victim, UserSession killer)
    {
        await gate.WaitAsync();
        try
        {
            await LoadAndExpireAsync();
            foreach (var bounty in board.Where(b => b.TargetCharacterId == victim.CharacterId
                         && b.PosterCharacterId != killer.CharacterId
                         && !Hunters(b.Id).Contains(killer.CharacterId)).ToList())
            {
                if (await TrySaveAsync(db => db.BountyHunters.Add(new BountyHunter { BountyId = bounty.Id, CharacterId = killer.CharacterId })))
                    Hunters(bounty.Id).Add(killer.CharacterId);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task SendBountyListAsync(UserSession session)
    {
        List<BountyPacketWriter.Entry> entries;
        await gate.WaitAsync();
        try
        {
            await LoadAndExpireAsync();
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
            await LoadAndExpireAsync();
            if (target.Length > 0 && reward is >= BountyMinReward and <= BountyMaxReward
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
            await LoadAndExpireAsync();
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
            await LoadAndExpireAsync();
            var bounty = board.FirstOrDefault(b => b.Id == bountyId && b.PosterCharacterId == session.CharacterId);
            if (bounty != null && Hunters(bounty.Id).Count == 0 && await PayAndCloseAsync(session, bounty))
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

    private async Task LoadAndExpireAsync()
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

    private IEnumerable<BountyHunter> HunterRows(Bounty bounty) =>
        Hunters(bounty.Id).Select(hunter => new BountyHunter { BountyId = bounty.Id, CharacterId = hunter }).ToList();

    private async Task<bool> TrySaveAsync(Action<AppDbContext> change)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            change(db);
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Bounty board save failed");
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
            .Where(c => c.Name == name)
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
