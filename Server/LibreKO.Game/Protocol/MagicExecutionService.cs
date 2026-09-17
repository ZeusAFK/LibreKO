using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public interface IMagicExecutionService
{
    Task ExecuteAsync(UserSession session, MagicData magic, int skillId, int targetId, int[] data);
    Task CancelAsync(UserSession session, int skillId);
}

public class MagicExecutionService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IMagicCombatEffectService magicCombatEffectService,
    IMagicStatusEffectService magicStatusEffectService,
    IMagicMovementEffectService magicMovementEffectService,
    IStealthService stealthService) : IMagicExecutionService
{
    public async Task ExecuteAsync(UserSession session, MagicData magic, int skillId, int targetId, int[] data)
    {
        await ExecuteTypeAsync(session, magic, magic.PrimaryType, skillId, targetId, data, isPrimary: true);

        if (magic.SecondaryType != MagicSkillType.None
            && HasTypeData(magic, magic.SecondaryType, skillId))
        {
            await ExecuteTypeAsync(
                session, magic, magic.SecondaryType, skillId, targetId, data, isPrimary: false);
        }
    }

    private bool HasTypeData(MagicData magic, MagicSkillType skillType, int skillId) => skillType switch
    {
        MagicSkillType.Melee => Has(gameDataService.MagicType1Table, magic, skillId),
        MagicSkillType.Ranged => Has(gameDataService.MagicType2Table, magic, skillId),
        MagicSkillType.OverTime => Has(gameDataService.MagicType3Table, magic, skillId),
        MagicSkillType.Buff => Has(gameDataService.MagicType4Table, magic, skillId),
        MagicSkillType.Special => Has(gameDataService.MagicType5Table, magic, skillId),
        MagicSkillType.Transform => Has(gameDataService.MagicType6Table, magic, skillId),
        MagicSkillType.Area => Has(gameDataService.MagicType7Table, magic, skillId),
        MagicSkillType.Warp => Has(gameDataService.MagicType8Table, magic, skillId),
        MagicSkillType.Stealth => Has(gameDataService.MagicType9Table, magic, skillId),
        _ => false,
    };

    private static bool Has<T>(IReadOnlyDictionary<int, T> table, MagicData magic, int skillId)
        where T : class => MagicTypeLookup.TryResolve(table, magic, skillId, out _);

    private async Task ExecuteTypeAsync(
        UserSession session, MagicData magic, MagicSkillType skillType, int skillId, int targetId,
        int[] data, bool isPrimary)
    {
        switch (skillType)
        {
            case MagicSkillType.Melee:
            case MagicSkillType.Ranged:
            case MagicSkillType.OverTime:
            case MagicSkillType.Area:
                await stealthService.RevealAsync(session, InvisibilityType.None);
                await magicCombatEffectService.ExecuteAsync(session, magic, skillType, skillId, targetId, data);
                break;
            case MagicSkillType.Buff:
            case MagicSkillType.Special:
            case MagicSkillType.Transform:
            case MagicSkillType.Stealth:
                await magicStatusEffectService.ExecuteAsync(session, magic, skillType, skillId, targetId, data);
                break;
            case MagicSkillType.Warp:
                await magicMovementEffectService.ExecuteAsync(session, magic, skillId, targetId, data);
                break;
            default:
                if (!isPrimary)
                    break;

                await sessionManager.Regions.SendToRegion(
                    session,
                    MagicProcessPacketWriter.Create(
                        MagicProcessOpcode.Effecting,
                        skillId,
                        (short)session.CharacterId,
                        targetId,
                        data),
                    excludeSender: false);
                break;
        }
    }

    public Task CancelAsync(UserSession session, int skillId)
    {
        var magic = gameDataService.GetMagic(skillId);
        if (magic?.PrimaryType == MagicSkillType.OverTime)
            return magicCombatEffectService.CancelAsync(session, skillId);

        return magicStatusEffectService.CancelAsync(session, skillId);
    }
}
