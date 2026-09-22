using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using Microsoft.Extensions.Options;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.World;

public interface IPlayerProgressionService
{
    event Func<UserSession, Task>? LevelChanged;

    Task AwardExperienceAsync(UserSession session, long baseExp);
    Task ChangeExperienceAsync(UserSession session, long expAmount);
    Task ResetToLevelAsync(UserSession session, byte level);
    Task SetLevelAsync(UserSession session, byte level);
}

public class PlayerProgressionService(
    IGameDataService gameDataService,
    IKingEventState kingEventState,
    TimeWeatherBroadcastService timeWeather,
    IOptions<GameServerSettings> settings,
    ICombatNotificationService combatNotificationService,
    IUserNotificationService userNotificationService,
    ICharacterStatePersister characterStatePersister,
    IAchievementProgressService achievementProgressService,
    SessionManager sessionManager,
    LibreKO.Game.Scripting.IQuestDefinitionSource? quests = null) : IPlayerProgressionService
{
    public event Func<UserSession, Task>? LevelChanged;

    public async Task AwardExperienceAsync(UserSession session, long baseExp)
    {
        if (baseExp <= 0)
            return;

        var expGain = ApplyExperienceBonuses(session, baseExp);
        await ChangeExperienceAsync(session, expGain);
    }

    private long RequiredExperience(UserSession session) =>
        RebirthBonus.RequiredExperience(
            gameDataService.GetMaxExpForLevel(session.Level), session.RebirthLevel);

    public async Task ChangeExperienceAsync(UserSession session, long expAmount)
    {
        if (session.Level < 6 && expAmount < 0)
            return;

        if (expAmount < 0 && session.Experience + expAmount < 0)
        {
            session.Level--;
            var diffExp = session.Experience + expAmount;
            session.Experience = RequiredExperience(session);
            await ApplyLevelChangeAsync(session, session.Level, levelUp: false);
            await ChangeExperienceAsync(session, diffExp);
            return;
        }

        session.Experience += expAmount;

        var leveledUp = false;
        while (session.Level < ProgressionTable.MaxLevel)
        {
            var maxExp = RequiredExperience(session);
            if (session.Experience < maxExp)
                break;

            session.Experience -= maxExp;
            session.Level++;
            leveledUp = true;
            await ApplyLevelChangeAsync(session, session.Level, levelUp: true, broadcast: false);
        }

        if (session.Level >= ProgressionTable.MaxLevel)
        {
            var maxExp = RequiredExperience(session);
            if (session.Experience > maxExp)
                session.Experience = maxExp;
        }

        if (leveledUp)
            await BroadcastLevelChangeAsync(session);
        else
            await SendExperienceAsync(session);
    }

    public async Task SetLevelAsync(UserSession session, byte level)
    {
        if (!ProgressionTable.IsValidLevel(level) || level == session.Level)
            return;

        session.Level = level;
        await ApplyLevelChangeAsync(session, level, levelUp: false);
        await SendExperienceAsync(session);
        await userNotificationService.SendStatUpdateAsync(session);
        await characterStatePersister.SaveAsync(session);
    }

    public async Task ResetToLevelAsync(UserSession session, byte level)
    {
        if (!ProgressionTable.IsValidLevel(level))
            return;

        session.Level = level;
        session.Experience = 0;
        session.ApplyBaseStats();
        session.StatPoints = ProgressionTable.StatPointsForLevel(level);
        session.ResetMasteryPoints();
        session.SkillData = [];

        await ApplyLevelChangeAsync(session, level, levelUp: false);
        await SendExperienceAsync(session);
        await session.Client.SendPacket(CharacterDevelopmentPacketMapper.CreateStatResetSuccess(session));
        await session.Client.SendPacket(CharacterDevelopmentPacketMapper.CreateSkillResetSuccess(session));
        await session.Client.SendPacket(SkillDataPacketWriter.Cleared());
        await userNotificationService.SendStatUpdateAsync(session);
        await characterStatePersister.SaveAsync(session);
    }

    private long ApplyExperienceBonuses(UserSession session, long expGain)
    {
        var expMultiplier = Math.Max(1, settings.Value.Global.ExpMultiplier);
        expGain *= expMultiplier;

        if (session.Stats.ItemExpBonusPercent > 0)
            expGain = expGain * (100 + session.Stats.ItemExpBonusPercent) / 100;

        var premiumExpPercent = GetPremiumExpPercent(session);
        if (premiumExpPercent > 0)
            expGain = expGain * (100 + premiumExpPercent) / 100;

        var kingExpBonus = kingEventState.GetExpBonus(session.Nation);
        if (kingExpBonus > 0)
            expGain = expGain * (100 + kingExpBonus) / 100;

        // GM `+exp_add` server-wide modifier on top.
        expGain = timeWeather.ApplyExpBonus(expGain);

        return expGain;
    }

    private async Task ApplyLevelChangeAsync(UserSession session, byte level, bool levelUp, bool broadcast = true)
    {
        if (!ProgressionTable.IsValidLevel(level))
            return;

        if (levelUp)
        {
            var statTotal = session.Strength + session.Stamina + session.Dexterity + session.Intelligence + session.Magic;
            if (session.StatPoints + statTotal < ProgressionTable.BaseStatTotal + ProgressionTable.StatPointsForLevel(level))
                session.StatPoints += (short)(level > ProgressionTable.StatBonusLevel
                    ? ProgressionTable.StatPointsPerLevel + ProgressionTable.BonusStatPointsPerLevel
                    : ProgressionTable.StatPointsPerLevel);

            var totalSkillPoints = 0;
            for (var index = 0; index < ProgressionTable.MasterySlotCount; index++)
                totalSkillPoints += session.SkillPoints[index];

            if (level >= ProgressionTable.MasteryFirstLevel
                && totalSkillPoints < ProgressionTable.MasteryPointsForLevel(level))
            {
                session.SkillPoints[ProgressionTable.MasteryPoolSlot] += ProgressionTable.MasteryPointsPerLevel;
            }
        }

        var coefficient = gameDataService.GetCoefficient(session.Class);
        if (coefficient != null)
            session.RecalculateStats(coefficient, gameDataService);

        if (session.Hp > 0)
        {
            session.Mp = session.MaxMp;
            session.Hp = session.MaxHp;
        }
        else
        {
            session.Mp = Math.Min(session.Mp, session.MaxMp);
        }

        await achievementProgressService.RefreshAsync(session);

        if (broadcast)
            await BroadcastLevelChangeAsync(session);
        if (quests is not null && session.Quest.ViewZone == session.ZoneId)
            await quests.SendViewsAsync(session, changesOnly: true);

        if (LevelChanged is { } levelChanged)
        {
            foreach (var handler in levelChanged.GetInvocationList().Cast<Func<UserSession, Task>>())
                await handler(session);
        }
    }

    private async Task BroadcastLevelChangeAsync(UserSession session)
    {
        var packet = ProgressionPacketWriter.LevelChange(new ProgressionPacketWriter.LevelState(
            session.CharacterId,
            session.Level,
            session.StatPoints,
            session.SkillPoints[ProgressionTable.MasteryPoolSlot],
            RequiredExperience(session),
            session.Experience,
            session.MaxHp,
            session.Hp,
            session.MaxMp,
            session.Mp,
            session.Stats.MaxWeight,
            session.Stats.ItemWeight));
        await sessionManager.Regions.SendToRegion(session, packet, excludeSender: false);

        // Region broadcast only updates players in the same zone. The party panel needs
        // a cross-zone broadcast so teammates can see the level change from anywhere.
        await combatNotificationService.SendPartyLevelUpdateAsync(session);
    }

    private static async Task SendExperienceAsync(UserSession session)
    {
        await session.Client.SendPacket(ExperiencePacketWriter.Current(session.Experience));
    }

    private int GetPremiumExpPercent(UserSession session)
    {
        if (session.PremiumType == 0)
            return 0;

        foreach (var entry in gameDataService.PremiumItemExpTable)
        {
            if (entry.Type == session.PremiumType
                && session.Level >= entry.MinLevel
                && session.Level <= entry.MaxLevel)
            {
                return entry.Percent;
            }
        }

        return 0;
    }
}
