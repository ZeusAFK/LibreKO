using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public interface IMagicCombatEffectService
{
    Task ExecuteAsync(
        UserSession caster, MagicData magic, MagicSkillType skillType, int skillId, int targetId,
        int[] data);
    Task CancelAsync(UserSession target, int skillId);
}

public class MagicCombatEffectService(
    MagicMeleeService meleeService,
    MagicRangedService rangedService,
    MagicOverTimeService overTimeService,
    MagicAreaService areaService) : IMagicCombatEffectService
{
    public Task ExecuteAsync(
        UserSession caster, MagicData magic, MagicSkillType skillType, int skillId, int targetId,
        int[] data)
    {
        caster.MarkCombat();
        return skillType switch
        {
            MagicSkillType.Melee => meleeService.ExecuteAsync(caster, magic, skillId, targetId, data),
            MagicSkillType.Ranged => rangedService.ExecuteAsync(caster, magic, skillId, targetId, data),
            MagicSkillType.OverTime => overTimeService.ExecuteAsync(caster, magic, skillId, targetId, data),
            MagicSkillType.Area => areaService.ExecuteAsync(caster, magic, skillId, targetId, data),
            _ => Task.CompletedTask
        };
    }

    public Task CancelAsync(UserSession target, int skillId)
    {
        target.ActiveOverTimeEffects.TryRemove(skillId, out _);
        return Task.CompletedTask;
    }
}
