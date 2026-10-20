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
    private sealed record Job(short Class, byte Race, byte Strength, byte Stamina, byte Dexterity, byte Intelligence, byte Magic,
        short StatPoints, byte[] SkillPoints);

    public async Task HandlePromoteNoviceAsync(UserSession session, byte changeType, byte newJob)
    {
        // No job change while busy, held or fighting.
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
        var journal = new InventoryJournal(session);
        Job? before = null;
        // The token and the new job are saved together, and the job's race with them.
        var outcome = await statePersister.CommitAsync(session, async db =>
        {
            var character = await db.Characters.FindAsync(session.CharacterId);
            if (character == null || !journal.Apply(s =>
                {
                    before = JobOf(s);
                    return TakeToken(s, journal, tokenId, newClass, newRace);
                }))
                return false;

            character.Race = newRace;
            return true;
        }, () => session.WithLock(s =>
        {
            journal.Restore();
            Restore(s, before!);
        }));
        if (outcome != StateCommit.Saved)
        {
            await SendFailAsync(session, outcome == StateCommit.Failed ? JobChangeOutcome.JobError : JobChangeOutcome.NoTokenOrSameClass);
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
        if (session.IsBusy || session.IsHeld || session.IsInCombat || !journal.TakeOne(tokenId))
            return false;

        session.Class = newClass;
        session.Race = newRace;
        session.ApplyBaseStats();
        session.StatPoints = ProgressionTable.StatPointsForLevel(session.Level);
        session.ResetMasteryPoints();
        return true;
    }

    private static Job JobOf(UserSession session) => new(
        session.Class, session.Race, session.Strength, session.Stamina, session.Dexterity, session.Intelligence, session.Magic,
        session.StatPoints, (byte[])session.SkillPoints.Clone());

    private static void Restore(UserSession session, Job job)
    {
        (session.Class, session.Race, session.Strength, session.Stamina, session.Dexterity, session.Intelligence, session.Magic) =
            (job.Class, job.Race, job.Strength, job.Stamina, job.Dexterity, job.Intelligence, job.Magic);
        session.StatPoints = job.StatPoints;
        Array.Copy(job.SkillPoints, session.SkillPoints, job.SkillPoints.Length);
    }
}
