using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IJobChangeService
{
    Task HandlePromoteNoviceAsync(UserSession session, byte changeType, byte newJob);
}

public class JobChangeService(
    IGameDataService gameDataService,
    ICharacterStatePersister statePersister,
    IUserNotificationService userNotifications,
    ICombatNotificationService combatNotificationService,
    ILogger<JobChangeService> logger) : IJobChangeService
{
    private const int FreeStats = 0;

    private sealed record Job(short Class, byte Race, int[] Stats, int[] Skills);

    public async Task HandlePromoteNoviceAsync(UserSession session, byte changeType, byte newJob)
    {
        if (!Enum.IsDefined((JobChangeTarget)newJob) || changeType is > 1
            || session.IsBusy || session.IsHeld || session.IsInCombat)
        {
            await SendFailAsync(session, JobChangeOutcome.JobError);
            return;
        }

        if (ClassIdHelper.IsSameJobGroup(session.Class, newJob))
        {
            await SendFailAsync(session, JobChangeOutcome.NoTokenOrSameClass);
            return;
        }

        var tokenId = JobChangeRules.FindChangeToken(session, changeType);
        if (tokenId == 0)
        {
            await SendFailAsync(session, JobChangeOutcome.NoTokenOrSameClass);
            return;
        }

        if (JobChangeRules.HasEquippedItems(session))
        {
            await session.Client.SendPacket(ClassChangePacketWriter.Failure(
                ClassChangeSubOpcode.SkillReset, ResetResult.InventoryNotEmpty, 0));
            await SendFailAsync(session, JobChangeOutcome.EquipmentWorn);
            return;
        }

        var resolved = JobChangeRules.Resolve(session.Class, session.Race, (byte)session.Nation, newJob, changeType);
        if (resolved is null)
        {
            await SendFailAsync(session, JobChangeOutcome.JobError);
            return;
        }
        var (newClass, newRace) = resolved.Value;

        var oldClass = session.Class;
        var oldRace = session.Race;
        var journal = new InventoryJournal(session, logger);
        Job? before = null;
        Job? after = null;
        var tiedUp = false;
        // The token and the new job are saved together, and the job's race with them.
        var outcome = await statePersister.CommitAsync(session, async db =>
        {
            var character = await db.Characters.FindAsync(session.CharacterId);
            if (character == null || !journal.Apply(s =>
                {
                    tiedUp = s.IsBusy || s.IsHeld || s.IsInCombat;
                    before = JobOf(s);
                    if (tiedUp || !TakeToken(s, journal, tokenId, newClass, newRace))
                        return false;
                    after = JobOf(s);
                    return true;
                }))
                return false;

            character.Race = newRace;
            return true;
        }, () => session.WithLock(s =>
        {
            journal.Restore();
            Restore(s, before!, after!);
        }));
        if (outcome != StateCommit.Saved)
        {
            await SendFailAsync(session, outcome == StateCommit.Failed || tiedUp ? JobChangeOutcome.JobError : JobChangeOutcome.NoTokenOrSameClass);
            return;
        }

        var stack = journal.TouchedSlots.Single();
        var token = session.Inventory[stack];
        await userNotifications.SendStackChangeAsync(session, (byte)stack, token.ItemId, token.Count, token.Durability);
        var coefficient = gameDataService.GetCoefficient(session.Class);
        if (coefficient != null)
            session.RecalculateStats(coefficient, gameDataService);
        session.WithLock(s =>
        {
            if (s.Hp <= 0)
                return;
            s.Hp = s.MaxHp;
            s.Mp = s.MaxMp;
        });

        await session.Client.SendPacket(ClassChangePacketWriter.JobChangeResult(JobChangeOutcome.Succeeded));

        await combatNotificationService.SendPartyClassUpdateAsync(session);

        logger.LogInformation(
            "Job change for {Name}: class {OldClass}→{NewClass} race {OldRace}→{NewRace} (newJob={NewJob}, type={ChangeType})",
            session.Name, oldClass, newClass, oldRace, newRace, newJob, changeType);
    }

    private static async Task SendFailAsync(UserSession session, JobChangeOutcome outcome)
    {
        await session.Client.SendPacket(ClassChangePacketWriter.JobChangeResult(outcome));
    }

    private static bool TakeToken(UserSession session, InventoryJournal journal, int tokenId, short newClass, byte newRace)
    {
        if (!journal.TakeOne(tokenId))
            return false;

        session.Class = newClass;
        session.Race = newRace;
        session.ApplyBaseStats();
        session.StatPoints = ProgressionTable.StatPointsForLevel(session.Level);
        session.ResetMasteryPoints();
        return true;
    }

    private static Job JobOf(UserSession session) => new(session.Class, session.Race, StatsOf(session), SkillsOf(session));

    // Free points come first, at FreeStats.
    private static int[] StatsOf(UserSession session) =>
        [session.StatPoints, session.Strength, session.Stamina, session.Dexterity, session.Intelligence, session.Magic];

    private static int[] SkillsOf(UserSession session) => [.. session.SkillPoints.Select(points => (int)points)];

    // The class goes back whole, and stats and points by what the change moved, so points earned meanwhile stay.
    private static void Restore(UserSession session, Job before, Job after)
    {
        (session.Class, session.Race) = (before.Class, before.Race);
        var stats = Back(StatsOf(session), before.Stats, after.Stats, FreeStats);
        if (stats is [var points, var strength, var stamina, var dexterity, var intelligence, var magic])
        {
            session.StatPoints = (short)Math.Min(points, short.MaxValue);
            (session.Strength, session.Stamina, session.Dexterity, session.Intelligence, session.Magic) =
                (Capped(strength), Capped(stamina), Capped(dexterity), Capped(intelligence), Capped(magic));
        }

        var skills = Back(SkillsOf(session), before.Skills, after.Skills, ProgressionTable.MasteryPoolSlot);
        for (var index = 0; index < skills.Length; index++)
            session.SkillPoints[index] = Capped(skills[index]);
    }

    // Points spent since the change come back out of what they raised, so the undo leaves no more than there was.
    private static int[] Back(int[] now, int[] before, int[] after, int pool)
    {
        var back = now.Select((value, index) => value + before[index] - after[index]).ToArray();
        for (var index = 0; index < back.Length && back[pool] < 0; index++)
        {
            if (index == pool)
                continue;

            var spent = Math.Clamp(now[index] - after[index], 0, -back[pool]);
            back[index] -= spent;
            back[pool] += spent;
        }

        return [.. back.Select(value => Math.Max(value, 0))];
    }

    private static byte Capped(int value) => (byte)Math.Min(value, byte.MaxValue);
}
