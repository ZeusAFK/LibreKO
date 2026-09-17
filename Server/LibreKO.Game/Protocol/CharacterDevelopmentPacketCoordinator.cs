using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface ICharacterDevelopmentPacketCoordinator
{
    Task HandlePointChangeAsync(IClient client, Packet packet);
    Task HandleSkillPointChangeAsync(IClient client, Packet packet);
    Task HandleClassChangeAsync(IClient client, Packet packet);
    Task HandleHelmetAsync(IClient client, Packet packet);
    Task HandlePresetAsync(IClient client, Packet packet);
}

public class CharacterDevelopmentPacketCoordinator(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IJobChangeService jobChangeService,
    ILogger<CharacterDevelopmentPacketCoordinator> logger) : ICharacterDevelopmentPacketCoordinator
{
    private const byte JobChangeMinimumLevel = 10;

    private const int StatPresetBodyLength = 7;
    private const int SkillPresetBodyLength = 5;

    private const byte PremiumFreeResetType = 12;
    private const int ReturnTokenItemId = 810_512_000;

    public async Task HandlePointChangeAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var stat = (StatType)packet.ReadByte();
        if (!Enum.IsDefined(stat) || session.StatPoints < 1)
            return;

        switch (stat)
        {
            case StatType.Strength when session.Strength < ProgressionTable.StatMax:
                session.Strength++;
                break;
            case StatType.Stamina when session.Stamina < ProgressionTable.StatMax:
                session.Stamina++;
                break;
            case StatType.Dexterity when session.Dexterity < ProgressionTable.StatMax:
                session.Dexterity++;
                break;
            case StatType.Intelligence when session.Intelligence < ProgressionTable.StatMax:
                session.Intelligence++;
                break;
            case StatType.Magic when session.Magic < ProgressionTable.StatMax:
                session.Magic++;
                break;
            default:
                return;
        }

        session.StatPoints--;
        RecalculateStats(session);

        await client.SendPacket(ProgressionPacketWriter.StatPointApplied(
            (byte)stat,
            session.GetStat(stat),
            session.MaxHp, session.MaxMp,
            (short)session.Stats.TotalHit, session.Stats.MaxWeight));
    }

    public async Task HandleSkillPointChangeAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var type = packet.ReadByte();

        var isClassSlot = type is >= ProgressionTable.MasteryClassFirstSlot
            and <= ProgressionTable.MasteryMasterSlot;
        if (!isClassSlot
            || session.SkillPoints[ProgressionTable.MasteryPoolSlot] < 1
            || session.SkillPoints[type] + 1 > session.Level
            || ClassIdHelper.IsBeginner(session.Class)
            || (type == ProgressionTable.MasteryMasterSlot
                && (!ClassIdHelper.IsMastered(session.Class)
                    || session.SkillPoints[type] >= ProgressionTable.MasteryMasterMaxPoints
                    || session.SkillPoints[type] >= session.Level - ProgressionTable.MasteryMasterLevel)))
        {
            logger.LogWarning(
                "Refusing mastery point for {Name}: tree={Tree} current={Current} level={Level} "
                + "pool={Pool} class={Class}",
                session.Name,
                type,
                isClassSlot ? session.SkillPoints[type] : (byte)0,
                session.Level,
                session.SkillPoints[ProgressionTable.MasteryPoolSlot],
                session.Class);

            await client.SendPacket(ProgressionPacketWriter.SkillPointApplied(
                type, isClassSlot ? session.SkillPoints[type] : (byte)0));
            return;
        }

        session.SkillPoints[ProgressionTable.MasteryPoolSlot]--;
        session.SkillPoints[type]++;
    }

    public async Task HandlePresetAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        switch ((PresetSubOpcode)packet.ReadByte())
        {
            case PresetSubOpcode.Stat:
                await ApplyStatPresetAsync(client, session, packet);
                break;

            case PresetSubOpcode.Skill:
                await ApplySkillPresetAsync(client, session, packet);
                break;
        }
    }

    private async Task ApplyStatPresetAsync(IClient client, UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < StatPresetBodyLength)
        {
            await client.SendPacket(PresetPacketWriter.StatRejected(PresetPacketWriter.StatApplyFailed));
            return;
        }

        var requested = new BaseStatBlock(
            packet.ReadByte(), packet.ReadByte(), packet.ReadByte(), packet.ReadByte(), packet.ReadByte());
        var claimedPoints = packet.ReadUShort();

        if (gameDataService.GetCoefficient(session.Class) == null)
        {
            await client.SendPacket(PresetPacketWriter.StatRejected(PresetPacketWriter.StatClassError));
            return;
        }

        var baseStats = ProgressionTable.BaseStatsForClass(session.Class);
        if (!IsStatRedistributed(session, baseStats))
        {
            await client.SendPacket(PresetPacketWriter.StatRejected(
                PresetPacketWriter.StatNeedsRedistribution));
            return;
        }

        var spend = StatSpend(requested, baseStats);
        if (spend < 0 || spend > session.StatPoints)
        {
            await client.SendPacket(PresetPacketWriter.StatRejected(PresetPacketWriter.StatApplyFailed));
            return;
        }

        var remaining = (short)(session.StatPoints - spend);
        if (claimedPoints != remaining)
        {
            await client.SendPacket(PresetPacketWriter.StatRejected(PresetPacketWriter.StatPointsMismatch));
            return;
        }

        session.Strength = requested.Strength;
        session.Stamina = requested.Stamina;
        session.Dexterity = requested.Dexterity;
        session.Intelligence = requested.Intelligence;
        session.Magic = requested.Magic;
        session.StatPoints = remaining;
        RecalculateStats(session);

        logger.LogDebug("{Name} applied a stat preset, {Points} point(s) left", session.Name, remaining);

        await client.SendPacket(PresetPacketWriter.StatApplied(new PresetPacketWriter.StatState(
            session.Strength, session.Stamina, session.Dexterity, session.Intelligence, session.Magic,
            session.StatPoints, session.MaxHp, session.MaxMp,
            (short)session.Stats.TotalHit, session.Stats.MaxWeight)));
    }

    private async Task ApplySkillPresetAsync(IClient client, UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < SkillPresetBodyLength)
        {
            await client.SendPacket(PresetPacketWriter.SkillRejected(PresetPacketWriter.SkillApplyFailed));
            return;
        }

        var requested = new byte[ProgressionTable.MasteryClassSlotCount];
        for (var index = 0; index < ProgressionTable.MasteryClassSlotCount; index++)
            requested[index] = packet.ReadByte();
        var claimedPoints = packet.ReadByte();

        var rejection = ValidateSkillPreset(session, requested);
        if (rejection != PresetPacketWriter.Applied)
        {
            await client.SendPacket(PresetPacketWriter.SkillRejected(rejection));
            return;
        }

        var pool = session.SkillPoints[ProgressionTable.MasteryPoolSlot];
        var spend = 0;
        foreach (var points in requested)
            spend += points;

        var remaining = (byte)(pool - spend);
        if (claimedPoints != remaining)
        {
            await client.SendPacket(PresetPacketWriter.SkillRejected(PresetPacketWriter.SkillPointsMismatch));
            return;
        }

        for (var index = 0; index < ProgressionTable.MasteryClassSlotCount; index++)
            session.SkillPoints[ProgressionTable.MasteryClassFirstSlot + index] = requested[index];
        session.SkillPoints[ProgressionTable.MasteryPoolSlot] = remaining;

        logger.LogDebug("{Name} applied a skill preset, {Points} point(s) left", session.Name, remaining);

        await client.SendPacket(PresetPacketWriter.SkillApplied(new PresetPacketWriter.SkillState(
            requested[0], requested[1], requested[2], requested[3], remaining)));
    }

    private static byte ValidateSkillPreset(UserSession session, byte[] requested)
    {
        if (session.Level < ProgressionTable.MasteryFirstLevel)
            return PresetPacketWriter.SkillLevelTooLow;

        if (ClassIdHelper.IsBeginner(session.Class))
            return PresetPacketWriter.SkillNeedsFirstJobChange;

        var master = requested[ProgressionTable.MasteryMasterSlot - ProgressionTable.MasteryClassFirstSlot];
        if (master > 0)
        {
            if (!ClassIdHelper.IsMastered(session.Class))
                return PresetPacketWriter.SkillNeedsSecondJobChange;

            if (master > ProgressionTable.MasteryMasterMaxPoints
                || session.Level < ProgressionTable.MasteryMasterLevel + master)
                return PresetPacketWriter.SkillMasterFailed;
        }

        if (!IsMasteryRedistributed(session))
            return PresetPacketWriter.SkillNeedsRedistribution;

        var spend = 0;
        foreach (var points in requested)
        {
            if (points > session.Level)
                return PresetPacketWriter.SkillApplyFailed;
            spend += points;
        }

        return spend > session.SkillPoints[ProgressionTable.MasteryPoolSlot]
            ? PresetPacketWriter.SkillApplyFailed
            : PresetPacketWriter.Applied;
    }

    private static bool IsStatRedistributed(UserSession session, BaseStatBlock baseStats)
        => session.Strength == baseStats.Strength
           && session.Stamina == baseStats.Stamina
           && session.Dexterity == baseStats.Dexterity
           && session.Intelligence == baseStats.Intelligence
           && session.Magic == baseStats.Magic;

    private static bool IsMasteryRedistributed(UserSession session)
    {
        for (var index = ProgressionTable.MasteryPoolSlot + 1; index < ProgressionTable.MasterySlotCount; index++)
        {
            if (session.SkillPoints[index] != 0)
                return false;
        }
        return true;
    }

    private static int StatSpend(BaseStatBlock requested, BaseStatBlock baseStats)
    {
        if (requested.Strength < baseStats.Strength
            || requested.Stamina < baseStats.Stamina
            || requested.Dexterity < baseStats.Dexterity
            || requested.Intelligence < baseStats.Intelligence
            || requested.Magic < baseStats.Magic)
            return -1;

        return requested.Strength - baseStats.Strength
               + requested.Stamina - baseStats.Stamina
               + requested.Dexterity - baseStats.Dexterity
               + requested.Intelligence - baseStats.Intelligence
               + requested.Magic - baseStats.Magic;
    }

    public async Task HandleClassChangeAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var subOpcode = (ClassChangeSubOpcode)packet.ReadByte();
        switch (subOpcode)
        {
            case ClassChangeSubOpcode.Eligibility:
                await SendClassEligibilityAsync(client, session);
                break;

            case ClassChangeSubOpcode.StatReset:
                await HandleAllPointChangeAsync(session, IsResetFree(session));
                break;

            case ClassChangeSubOpcode.SkillReset:
                await HandleAllSkillPointChangeAsync(session, IsResetFree(session));
                break;

            case ClassChangeSubOpcode.StatResetCost:
                await HandleResetCostAsync(client, session, packet);
                break;

            case ClassChangeSubOpcode.JobChangeResult:
                if (packet.RemainingBytes >= 2)
                {
                    var changeType = packet.ReadByte();
                    var newJob = packet.ReadByte();
                    await jobChangeService.HandlePromoteNoviceAsync(session, changeType, newJob);
                }
                break;

            case ClassChangeSubOpcode.RebirthStatChange:
                await HandleRebStatChangeAsync(session, packet);
                break;

            case ClassChangeSubOpcode.RebirthStatReset:
                await HandleRebStatResetAsync(session, packet);
                break;
        }
    }

    private async Task HandleRebStatChangeAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 5)
        {
            await SendRebResultAsync(session, ClassChangeSubOpcode.RebirthStatChange, 0);
            return;
        }

        var recStr = packet.ReadByte();
        var recSta = packet.ReadByte();
        var recDex = packet.ReadByte();
        var recInt = packet.ReadByte();
        var recCha = packet.ReadByte();

        if (session.RebirthLevel >= RebirthBonus.MaxRebirthLevel
            || recStr + recSta + recDex + recInt + recCha != RebirthBonus.PointsPerRebirth)
        {
            await SendRebResultAsync(session, ClassChangeSubOpcode.RebirthStatChange, 0);
            return;
        }

        var scrollSlot = FindRebirthScrollSlot(session);
        if (scrollSlot < 0)
        {
            await SendRebResultAsync(session, ClassChangeSubOpcode.RebirthStatChange, 0);
            return;
        }

        session.RebStr = (byte)Math.Min(byte.MaxValue, session.RebStr + recStr);
        session.RebSta = (byte)Math.Min(byte.MaxValue, session.RebSta + recSta);
        session.RebDex = (byte)Math.Min(byte.MaxValue, session.RebDex + recDex);
        session.RebIntel = (byte)Math.Min(byte.MaxValue, session.RebIntel + recInt);
        session.RebMagic = (byte)Math.Min(byte.MaxValue, session.RebMagic + recCha);
        session.RebirthLevel++;
        session.Experience = 0;

        session.Inventory[scrollSlot].Count--;
        if (session.Inventory[scrollSlot].Count <= 0)
            session.Inventory[scrollSlot].Clear();

        if (session.RebirthLevel < RebirthBonus.MaxRebirthLevel)
        {
            for (short qid = 1119; qid <= 1122; qid++)
            {
                session.Quest.QuestMap.Remove(qid);
                session.Quest.RemoveQuestKillCounts(qid);
            }
        }

        await SendRebResultAsync(session, ClassChangeSubOpcode.RebirthStatChange, 1);
        logger.LogInformation(
            "{Name} rebirth +1 (now {Level}): +str={Str} sta={Sta} dex={Dex} int={Int} cha={Cha}",
            session.Name, session.RebirthLevel, recStr, recSta, recDex, recInt, recCha);
    }

    private async Task HandleRebStatResetAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 5)
        {
            await SendRebResultAsync(session, ClassChangeSubOpcode.RebirthStatReset, 0);
            return;
        }

        var recStr = packet.ReadByte();
        var recSta = packet.ReadByte();
        var recDex = packet.ReadByte();
        var recInt = packet.ReadByte();
        var recCha = packet.ReadByte();

        if (session.RebirthLevel == 0
            || recStr + recSta + recDex + recInt + recCha != session.RebirthLevel * RebirthBonus.PointsPerRebirth)
        {
            await SendRebResultAsync(session, ClassChangeSubOpcode.RebirthStatReset, 0);
            return;
        }

        var freeReset = IsResetFree(session);
        if (!freeReset)
        {
            const int rebirthResetCost = 100_000_000;
            if (session.Money < rebirthResetCost)
            {
                await SendRebResultAsync(session, ClassChangeSubOpcode.RebirthStatReset, 0);
                return;
            }
            session.Money -= rebirthResetCost;
        }

        session.RebStr = recStr;
        session.RebSta = recSta;
        session.RebDex = recDex;
        session.RebIntel = recInt;
        session.RebMagic = recCha;

        await SendRebResultAsync(session, ClassChangeSubOpcode.RebirthStatReset, 1);
        logger.LogInformation(
            "{Name} rebirth reset: str={Str} sta={Sta} dex={Dex} int={Int} cha={Cha} (free={Free})",
            session.Name, recStr, recSta, recDex, recInt, recCha, freeReset);
    }

    private static int FindRebirthScrollSlot(UserSession session)
    {
        const int qualificationOfRebirth = 900_579_000;
        for (var i = InventoryConstants.SlotMax; i < InventoryConstants.SlotMax + InventoryConstants.HaveMax; i++)
        {
            if (session.Inventory[i].ItemId == qualificationOfRebirth && session.Inventory[i].Count > 0)
                return i;
        }
        return -1;
    }

    private static async Task SendRebResultAsync(UserSession session, ClassChangeSubOpcode subOpcode, byte result)
    {
        var packet = ClassChangePacketWriter.Failure(subOpcode, (ResetResult)result, 0);
        await session.Client.SendPacket(packet);
    }

    public async Task HandleHelmetAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || session.Hp <= 0 || packet.RemainingBytes < 1)
            return;

        var isHiding = packet.ReadByte();
        var isHidingCospre = packet.RemainingBytes >= 1 ? packet.ReadByte() : isHiding;
        session.IsHidingHelmet = isHiding != 0;

        var result = HelmetPacketWriter
            .Visibility(session.CharacterId, isHiding != 0, isHidingCospre != 0)
            ;
        await sessionManager.Regions.SendToRegion(session, result, excludeSender: false);
    }

    private static async Task SendClassEligibilityAsync(IClient client, UserSession session)
    {
        var eligibility = session.Level < JobChangeMinimumLevel ? JobChangeEligibility.LevelTooLow
            : !ClassIdHelper.IsBeginner(session.Class) ? JobChangeEligibility.AlreadyDone
            : JobChangeEligibility.Allowed;

        await client.SendPacket(ClassChangePacketWriter.JobChangeEligibility(eligibility));
    }

    private async Task HandleResetCostAsync(IClient client, UserSession session, Packet packet)
    {
        var kind = packet.RemainingBytes >= 1 ? (ResetKind)packet.ReadByte() : ResetKind.Stat;

        if (IsResetFree(session))
        {
            if (kind == ResetKind.Skill)
                await HandleAllSkillPointChangeAsync(session, free: true);
            else
                await HandleAllPointChangeAsync(session, free: true);
            return;
        }

        var cost = kind == ResetKind.Skill
            ? CalculateSkillResetCost(session.Level)
            : CalculateStatResetCost(session.Level);
        await client.SendPacket(ClassChangePacketWriter.StatResetCost(cost));
    }

    internal static bool IsResetFree(UserSession session)
        => session.PremiumType == PremiumFreeResetType || HasItem(session, ReturnTokenItemId);

    private static bool HasItem(UserSession session, int itemId)
    {
        for (var i = InventoryConstants.SlotMax;
             i < InventoryConstants.SlotMax + InventoryConstants.HaveMax;
             i++)
        {
            if (session.Inventory[i].ItemId == itemId && session.Inventory[i].Count > 0)
                return true;
        }
        return false;
    }

    private async Task HandleAllPointChangeAsync(UserSession session, bool free = false)
    {
        var cost = free ? 0 : CalculateStatResetCost(session.Level);

        if (session.Level > ProgressionTable.MaxLevel)
        {
            await SendStatResetFailureAsync(session, ResetResult.Refused, cost);
            return;
        }

        for (var slotIndex = 0; slotIndex < InventoryConstants.SlotMax; slotIndex++)
        {
            if (session.Inventory[slotIndex].IsEmpty)
                continue;

            await SendStatResetFailureAsync(session, ResetResult.InventoryNotEmpty, cost);
            return;
        }

        var statTotal = session.Strength + session.Stamina + session.Dexterity + session.Intelligence + session.Magic;
        if (statTotal == ProgressionTable.BaseStatTotal)
        {
            await SendStatResetFailureAsync(session, ResetResult.NothingToReset, cost);
            return;
        }

        if (session.Money < cost)
        {
            await SendStatResetFailureAsync(session, ResetResult.Refused, cost);
            return;
        }

        session.Money -= cost;
        logger.LogInformation("{Name} reset stat points (cost {Cost})", session.Name, cost);
        session.ApplyBaseStats();
        session.StatPoints = ProgressionTable.StatPointsForLevel(session.Level);

        RecalculateStats(session);

        await session.Client.SendPacket(CharacterDevelopmentPacketMapper.CreateStatResetSuccess(session));
    }

    private static Task SendStatResetFailureAsync(UserSession session, ResetResult reason, int cost)
        => session.Client.SendPacket(CharacterDevelopmentPacketMapper.CreateStatResetFailure(reason, cost));

    private async Task HandleAllSkillPointChangeAsync(UserSession session, bool free = false)
    {
        var cost = free ? 0 : CalculateSkillResetCost(session.Level);

        if (session.Level < ProgressionTable.MasteryFirstLevel)
        {
            await SendSkillResetFailureAsync(session, ResetResult.Refused, cost);
            return;
        }

        if (session.Money < cost)
        {
            await SendSkillResetFailureAsync(session, ResetResult.Refused, cost);
            return;
        }

        var totalSkill = 0;
        for (var index = ProgressionTable.MasteryPoolSlot + 1; index < ProgressionTable.MasterySlotCount; index++)
            totalSkill += session.SkillPoints[index];

        if (totalSkill <= 0)
        {
            await SendSkillResetFailureAsync(session, ResetResult.NothingToReset, cost);
            return;
        }

        session.Money -= cost;
        logger.LogInformation("{Name} reset skill points (cost {Cost})", session.Name, cost);
        session.ResetMasteryPoints();

        await session.Client.SendPacket(CharacterDevelopmentPacketMapper.CreateSkillResetSuccess(session));
    }

    private static Task SendSkillResetFailureAsync(UserSession session, ResetResult reason, int cost)
        => session.Client.SendPacket(CharacterDevelopmentPacketMapper.CreateSkillResetFailure(reason, cost));

    private void RecalculateStats(UserSession session)
    {
        var coefficient = gameDataService.GetCoefficient(session.Class);
        if (coefficient != null)
            session.RecalculateStats(coefficient, gameDataService);
    }

    private static int CalculateSkillResetCost(byte level)
    {
        var cost = (int)(Math.Pow(level * 2.0f, 3.4f) * 1.5f);
        if (level < 30)
            cost = (int)(cost * 0.4f);
        else if (level >= ProgressionTable.StatBonusLevel)
            cost = (int)(cost * 1.5f);

        return cost;
    }

    private static int CalculateStatResetCost(byte level)
    {
        var cost = (int)Math.Pow(level * 2.0f, 3.4f);
        if (level < 30)
            cost = (int)(cost * 0.4f);
        else if (level >= 60)
            cost = (int)(cost * 1.5f);

        return cost;
    }

}
