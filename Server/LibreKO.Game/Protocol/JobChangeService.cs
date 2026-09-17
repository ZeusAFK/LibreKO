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
    IServiceScopeFactory scopeFactory,
    ICombatNotificationService combatNotificationService,
    ILogger<JobChangeService> logger) : IJobChangeService
{

    public async Task HandlePromoteNoviceAsync(UserSession session, byte changeType, byte newJob)
    {
        if (!Enum.IsDefined((JobChangeTarget)newJob) || changeType is > 1)
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

        if (!ConsumeToken(session, tokenId))
        {
            await SendFailAsync(session, JobChangeOutcome.NoTokenOrSameClass);
            return;
        }

        var oldClass = session.Class;
        var oldRace = session.Race;
        session.Class = newClass;
        session.Race = newRace;

        session.ApplyBaseStats();
        session.StatPoints = ProgressionTable.StatPointsForLevel(session.Level);
        session.ResetMasteryPoints();

        var coefficient = gameDataService.GetCoefficient(session.Class);
        if (coefficient != null)
            session.RecalculateStats(coefficient, gameDataService);
        session.Hp = session.MaxHp;
        session.Mp = session.MaxMp;

        _ = PersistAsync(session);

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

    private static bool ConsumeToken(UserSession session, int itemId)
    {
        for (var i = InventoryConstants.SlotMax; i < InventoryConstants.SlotMax + InventoryConstants.HaveMax; i++)
        {
            var slot = session.Inventory[i];
            if (slot.ItemId == itemId && slot.Count > 0)
            {
                slot.Count--;
                if (slot.Count == 0) slot.Clear();
                return true;
            }
        }
        return false;
    }

    private async Task PersistAsync(UserSession session)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var charRepo = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
            var character = await charRepo.GetById(session.CharacterId);
            if (character == null) return;
            character.Class = session.Class;
            character.Race = session.Race;
            character.Strength = session.Strength;
            character.Stamina = session.Stamina;
            character.Dexterity = session.Dexterity;
            character.Intelligence = session.Intelligence;
            character.Magic = session.Magic;
            character.StatPoints = session.StatPoints;
            character.SkillPointData = session.SkillPoints;
            await charRepo.UpdateAsync(character);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Job change persist failed for {Name}", session.Name);
        }
    }

}
