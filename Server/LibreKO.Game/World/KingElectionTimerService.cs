using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.World;

public class KingElectionTimerService(
    IGameDataService gameData,
    SessionManager sessionManager,
    IServiceProvider serviceProvider,
    ILogger<KingElectionTimerService> logger) : BackgroundService
{
    // Election phase constants (match GamePacketHandler)
    private const byte ELECTION_TYPE_NO_TERM = 0;
    private const byte ELECTION_TYPE_NOMINATION = 1;
    private const byte ELECTION_TYPE_PRE_ELECTION = 2;
    private const byte ELECTION_TYPE_ELECTION = 3;
    private const byte ELECTION_TYPE_TERM_STARTED = 6;
    private const byte ELECTION_TYPE_TERM_ENDED = 7;

    private const byte ELECTION_LIST_SENATOR = 3;
    private const byte ELECTION_LIST_CANDIDATE = 4;

    private DateTime _lastBroadcast = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("King election timer service started");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                // Check both nations (1 = Karus, 2 = El Morad)
                foreach (byte nation in new byte[] { 1, 2 })
                {
                    if (!gameData.KingSystemTable.TryGetValue(nation, out var kingData))
                        continue;

                    await CheckElectionPhase(nation, kingData);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error in king election timer tick");
            }
        }
    }

    private async Task CheckElectionPhase(byte nation, KingSystemData kingData)
    {
        var now = DateTime.Now;
        var electionTime = GetElectionDateTime(kingData);

        // If no valid election time is set, skip
        if (electionTime == DateTime.MinValue) return;

        switch (kingData.Type)
        {
            case ELECTION_TYPE_NO_TERM:
            case ELECTION_TYPE_TERM_ENDED:
            {
                // Transition to NOMINATION 1 day before election
                var nominationStart = electionTime.AddDays(-1);
                if (now >= nominationStart)
                {
                    logger.LogInformation("Nation {Nation}: Election nomination phase started", nation);
                    await UpdateElectionStatus(kingData, ELECTION_TYPE_NOMINATION);
                    await LoadSenatorList(nation);
                    await BroadcastElectionMessage(nation, "The king election nomination period has begun!");
                }
                break;
            }

            case ELECTION_TYPE_NOMINATION:
            {
                // Transition to PRE_ELECTION 1 hour before election
                var preElectionStart = electionTime.AddHours(-1);
                if (now >= preElectionStart)
                {
                    logger.LogInformation("Nation {Nation}: Election pre-election phase started", nation);
                    await UpdateElectionStatus(kingData, ELECTION_TYPE_PRE_ELECTION);
                    await BroadcastElectionMessage(nation, "The king election will begin in 1 hour!");
                }
                else if (ShouldBroadcast(now))
                {
                    await BroadcastElectionMessage(nation, "The king election nomination period is in progress.");
                }
                break;
            }

            case ELECTION_TYPE_PRE_ELECTION:
            {
                // Transition to ELECTION at scheduled time
                if (now >= electionTime)
                {
                    logger.LogInformation("Nation {Nation}: Election voting phase started", nation);
                    await UpdateElectionStatus(kingData, ELECTION_TYPE_ELECTION);
                    await BroadcastElectionMessage(nation, "The king election has begun! Cast your vote!");
                }
                break;
            }

            case ELECTION_TYPE_ELECTION:
            {
                // Transition to TERM_STARTED 1 hour after election
                var electionEnd = electionTime.AddHours(1);
                if (now >= electionEnd)
                {
                    logger.LogInformation("Nation {Nation}: Election voting ended, tallying results", nation);
                    await TallyElectionResults(nation, kingData);
                    await UpdateElectionStatus(kingData, ELECTION_TYPE_TERM_STARTED);
                }
                else if (ShouldBroadcast(now))
                {
                    await BroadcastElectionMessage(nation, "The king election is in progress. Cast your vote!");
                }
                break;
            }
        }
    }

    private static DateTime GetElectionDateTime(KingSystemData kingData)
    {
        try
        {
            if (kingData.Year <= 0 || kingData.Month == 0 || kingData.Day == 0)
                return DateTime.MinValue;

            return new DateTime(kingData.Year, kingData.Month, kingData.Day, kingData.Hour, kingData.Minute, 0);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private bool ShouldBroadcast(DateTime now)
    {
        // Broadcast reminder every 30 minutes
        if ((now - _lastBroadcast).TotalMinutes >= 30)
        {
            _lastBroadcast = now;
            return true;
        }
        return false;
    }

    private async Task UpdateElectionStatus(KingSystemData kingData, byte newType)
    {
        kingData.Type = newType;

        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Attach(kingData);
        db.Entry(kingData).Property(p => p.Type).IsModified = true;
        await db.SaveChangesAsync();
    }

    private async Task LoadSenatorList(byte nation)
    {
        using var scope = serviceProvider.CreateScope();
        var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Clear existing election data for this nation
        var oldElections = await appDb.KingElectionList.Where(e => e.Nation == nation).ToListAsync();
        appDb.KingElectionList.RemoveRange(oldElections);
        var oldBallots = await appDb.KingBallotBox.Where(b => b.Nation == nation).ToListAsync();
        appDb.KingBallotBox.RemoveRange(oldBallots);
        var oldNotices = await appDb.KingCandidacyNoticeBoard.Where(n => n.Nation == nation).ToListAsync();
        appDb.KingCandidacyNoticeBoard.RemoveRange(oldNotices);
        await appDb.SaveChangesAsync();

        // Get top 10 clans by points for this nation
        var topClans = await appDb.Knights
            .Where(k => k.Nation == nation && k.Flag > 0)
            .OrderByDescending(k => k.Points)
            .Take(10)
            .ToListAsync();

        foreach (var clan in topClans)
        {
            if (string.IsNullOrEmpty(clan.Chief)) continue;

            appDb.KingElectionList.Add(new KingElectionList
            {
                Nation = nation,
                Type = ELECTION_LIST_SENATOR,
                Name = clan.Chief,
                Knights = clan.Id,
                Money = 0
            });
        }
        await appDb.SaveChangesAsync();

        logger.LogInformation("Nation {Nation}: Loaded {Count} senators for election", nation, topClans.Count);
    }

    private async Task TallyElectionResults(byte nation, KingSystemData kingData)
    {
        using var scope = serviceProvider.CreateScope();
        var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Count votes per candidate
        var voteResults = await appDb.KingBallotBox
            .Where(b => b.Nation == nation)
            .GroupBy(b => b.CandidacyId)
            .Select(g => new { CandidateName = g.Key, VoteCount = g.Count() })
            .OrderByDescending(r => r.VoteCount)
            .ToListAsync();

        if (voteResults.Count == 0)
        {
            logger.LogInformation("Nation {Nation}: No votes cast, no king elected", nation);
            kingData.KingName = string.Empty;
            await UpdateKingNameInDb(kingData);
            await BroadcastElectionMessage(nation, "The king election has ended with no votes cast. No king was elected.");
            return;
        }

        var winner = voteResults[0];
        logger.LogInformation("Nation {Nation}: Election winner is {Winner} with {Votes} votes",
            nation, winner.CandidateName, winner.VoteCount);

        // Update king name in memory and DB
        kingData.KingName = winner.CandidateName;
        await UpdateKingNameInDb(kingData);

        // Schedule next election (30 days from now)
        var nextElection = DateTime.Now.AddDays(30);
        kingData.Year = (short)nextElection.Year;
        kingData.Month = (byte)nextElection.Month;
        kingData.Day = (byte)nextElection.Day;
        kingData.Hour = (byte)nextElection.Hour;
        kingData.Minute = (byte)nextElection.Minute;
        await UpdateElectionScheduleInDb(kingData);

        // Broadcast result
        var totalVotes = voteResults.Sum(r => r.VoteCount);
        await BroadcastElectionMessage(nation,
            $"{winner.CandidateName} has been elected as the new King with {winner.VoteCount} out of {totalVotes} votes!");
    }

    private async Task UpdateKingNameInDb(KingSystemData kingData)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Attach(kingData);
        db.Entry(kingData).Property(p => p.KingName).IsModified = true;
        await db.SaveChangesAsync();
    }

    private async Task UpdateElectionScheduleInDb(KingSystemData kingData)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Attach(kingData);
        db.Entry(kingData).Property(p => p.Year).IsModified = true;
        db.Entry(kingData).Property(p => p.Month).IsModified = true;
        db.Entry(kingData).Property(p => p.Day).IsModified = true;
        db.Entry(kingData).Property(p => p.Hour).IsModified = true;
        db.Entry(kingData).Property(p => p.Minute).IsModified = true;
        await db.SaveChangesAsync();
    }

    private async Task BroadcastElectionMessage(byte nation, string message)
    {
        var pkt = NoticePacketWriter.Screen(message);

        foreach (var session in sessionManager.GetAll())
        {
            if ((byte)session.Nation == nation)
            {
                try { await session.Client.SendPacket(pkt); }
                catch { /* ignore send errors */ }
            }
        }
    }
}
