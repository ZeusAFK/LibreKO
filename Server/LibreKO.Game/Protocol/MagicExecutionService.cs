using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public interface IMagicExecutionService
{
    bool AllowsTarget(UserSession session, MagicData magic, int skillId, int targetId, int[] data);
    Task<bool> ExecuteAsync(UserSession session, MagicData magic, int skillId, int targetId, int[] data);
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
    private const int AreaTarget = -1;
    private const int AreaCentreX = 0;
    private const int AreaCentreZ = 2;
    private const float RangedPlayerShortcut = 38f;
    // The client allows spells 15 m past their range on players; the rest covers movement.
    private const float SkillReachSlack = 20f;
    private const int PercentScale = 100;

    public bool AllowsTarget(UserSession session, MagicData magic, int skillId, int targetId, int[] data) =>
        AreaCastAllowed(session, magic, skillId, targetId, data) && MayReach(session, magic, skillId, targetId);

    public async Task<bool> ExecuteAsync(UserSession session, MagicData magic, int skillId, int targetId, int[] data)
    {
        var tookEffect = await ExecuteTypeAsync(session, magic, magic.PrimaryType, skillId, targetId, data, isPrimary: true);

        if (magic.SecondaryType != MagicSkillType.None
            && HasTypeData(magic, magic.SecondaryType, skillId))
        {
            tookEffect = await ExecuteTypeAsync(
                session, magic, magic.SecondaryType, skillId, targetId, data, isPrimary: false) || tookEffect;
        }

        return await ExecuteExtendedEffectAsync(session, magic, skillId, targetId, data) || tookEffect;
    }

    private const int ExtendedEffectOffset = 80000;

    private async Task<bool> ExecuteExtendedEffectAsync(
        UserSession caster, MagicData magic, int skillId, int targetId, int[] data)
    {
        if (magic.PrimaryType is not (MagicSkillType.Melee or MagicSkillType.Ranged or MagicSkillType.OverTime or MagicSkillType.Warp))
            return false;

        var extendedId = skillId + ExtendedEffectOffset;
        if (gameDataService.GetMagic(extendedId) is not { PrimaryType: MagicSkillType.Buff } extended
            || !gameDataService.MagicType4Table.TryGetValue(extendedId, out var type4Data))
            return false;

        if (sessionManager.GetByCharacterId(targetId) is not { } target || !PvpRules.CanAttackPlayer(caster, target))
            return false;

        if (!StatusEffectChance.Lands(caster, target, (BuffType)type4Data.BuffType, StatusEffectChance.ExtendedRowChance))
            return false;

        return await magicStatusEffectService.ExecuteAsync(
            caster, extended, MagicSkillType.Buff, extendedId, targetId, data, isPrimary: false);
    }

    private bool AreaCastAllowed(UserSession caster, MagicData magic, int skillId, int targetId, int[] data)
    {
        MagicSkillType[] types = [magic.PrimaryType, magic.SecondaryType];
        var centred = data.Length > AreaCentreZ && (data[AreaCentreX] != 0 || data[AreaCentreZ] != 0);
        var centresAnOverTimeArea = centred && types.Contains(MagicSkillType.OverTime)
            && MagicTypeLookup.TryResolve(gameDataService.MagicType3Table, magic, skillId, out var type3) && type3.Radius > 0;
        if ((targetId == AreaTarget || centresAnOverTimeArea) && !IsAreaCast(magic, skillId))
            return false;

        return !centred
            || !types.Any(type => type is MagicSkillType.Melee or MagicSkillType.OverTime or MagicSkillType.Buff or MagicSkillType.Area)
            || Within(caster, data[AreaCentreX], data[AreaCentreZ], 0f, magic.Range > 0 ? magic.Range : AttackReach.Melee);
    }

    private bool IsAreaCast(MagicData magic, int skillId)
    {
        var moral = (SkillMoral)magic.Moral;
        var groundMoral = moral is SkillMoral.AreaEnemy or SkillMoral.AreaFriend or SkillMoral.AreaAll;
        var casterMoral = moral is SkillMoral.PartyAll or SkillMoral.SelfArea;
        var radius = AreaRadius(magic, skillId);
        var isArea = radius > 0
            && magic.PrimaryType is MagicSkillType.Melee or MagicSkillType.OverTime or MagicSkillType.Buff or MagicSkillType.Area;

        return (isArea && (groundMoral || casterMoral))
            || (casterMoral && magic.PrimaryType is MagicSkillType.Buff or MagicSkillType.Special)
            || (moral == SkillMoral.PartyAll && radius == 0 && magic.PrimaryType is MagicSkillType.OverTime or MagicSkillType.Warp);
    }

    private int AreaRadius(MagicData magic, int skillId) => magic.PrimaryType switch
    {
        // The client takes a melee row's radius from the over-time row with the same id.
        MagicSkillType.Melee when gameDataService.MagicType3Table.TryGetValue(skillId, out var melee) => melee.Radius,
        MagicSkillType.OverTime when MagicTypeLookup.TryResolve(gameDataService.MagicType3Table, magic, skillId, out var type3) => type3.Radius,
        MagicSkillType.Buff when MagicTypeLookup.TryResolve(gameDataService.MagicType4Table, magic, skillId, out var type4) => type4.Radius,
        MagicSkillType.Area when MagicTypeLookup.TryResolve(gameDataService.MagicType7Table, magic, skillId, out var type7) => type7.Radius,
        _ => 0,
    };

    private bool MayReach(UserSession caster, MagicData magic, int skillId, int targetId)
    {
        if (targetId == AreaTarget || targetId == caster.CharacterId
            || (SkillMoral)magic.Moral is SkillMoral.Self or SkillMoral.SelfArea
            || !IsAimed(magic, skillId))
            return true;

        if (sessionManager.GetByCharacterId(targetId) is { } player)
            return player.ZoneId == caster.ZoneId && player.Room == caster.Room
                && Within(caster, player.X, player.Z, AttackReach.PlayerBodyRadius, Reach(caster, magic, skillId))
                && MayTouch(caster, player, magic, skillId);

        if (sessionManager.Regions.GetNpc(targetId) is { } npc)
            return npc.ZoneId == caster.ZoneId && npc.Room == caster.Room
                && Within(caster, npc.X, npc.Z, npc.BulkRadius, Reach(caster, magic, skillId));

        return true;
    }

    private bool IsAimed(MagicData magic, int skillId) => magic.PrimaryType switch
    {
        MagicSkillType.Melee or MagicSkillType.Ranged or MagicSkillType.OverTime or MagicSkillType.Buff
            or MagicSkillType.Special => true,
        MagicSkillType.Area => AreaRadius(magic, skillId) == 0,
        _ => false,
    };

    private bool MayTouch(UserSession caster, UserSession target, MagicData magic, int skillId) => (SkillMoral)magic.Moral switch
    {
        SkillMoral.Enemy or SkillMoral.AreaEnemy or SkillMoral.Npc => PvpRules.CanAttackPlayer(caster, target),
        SkillMoral.Party or SkillMoral.PartyAll => caster.IsInParty && target.PartyIndex == caster.PartyIndex,
        SkillMoral.FriendWithMe or SkillMoral.FriendExceptMe or SkillMoral.AreaFriend or SkillMoral.Clan
            or SkillMoral.ClanAll or SkillMoral.CorpseFriend
            => !PvpRules.IsHostile(caster, target)
                || (HealStrikes(caster, magic, skillId) && PvpRules.CanAttackPlayer(caster, target)),
        _ => true,
    };

    private bool HealStrikes(UserSession caster, MagicData magic, int skillId) =>
        MagicTypeLookup.TryResolve(gameDataService.MagicType3Table, magic, skillId, out var type3)
        && HealStrike.Turns(caster.HealStrikesEnemies, (SkillMoral)magic.Moral, (MagicDirectType)type3.DirectType);

    private float Reach(UserSession caster, MagicData magic, int skillId)
    {
        if (magic.PrimaryType == MagicSkillType.Ranged || magic.SecondaryType == MagicSkillType.Ranged)
            return Math.Max(Math.Max(magic.Range, RangedPlayerShortcut), BowReach(caster, magic, skillId));

        if (magic.Range > 0)
            return magic.Range;

        return AttackReach.Melee;
    }

    private float BowReach(UserSession caster, MagicData magic, int skillId)
    {
        if (AttackReach.HeldBow(caster, gameDataService) is not { } bow)
            return 0f;

        var addRange = MagicTypeLookup.TryResolve(gameDataService.MagicType2Table, magic, skillId, out var type2)
            ? Math.Max(PercentScale, (int)type2.AddRange)
            : PercentScale;
        return bow.Range / AttackReach.RangeUnitsPerMetre * addRange / PercentScale;
    }

    private static bool Within(UserSession caster, float x, float z, float targetRadius, float reach) =>
        AttackReach.Within(caster, x, z, reach + AttackReach.PlayerBodyRadius + targetRadius + SkillReachSlack);

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

    private async Task<bool> ExecuteTypeAsync(
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
                return true;
            case MagicSkillType.Buff:
            case MagicSkillType.Special:
            case MagicSkillType.Transform:
            case MagicSkillType.Stealth:
                return await magicStatusEffectService.ExecuteAsync(session, magic, skillType, skillId, targetId, data, isPrimary);
            case MagicSkillType.Warp:
                return await magicMovementEffectService.ExecuteAsync(session, magic, skillId, targetId, data);
            default:
                if (!isPrimary)
                    return false;

                await sessionManager.Regions.SendToRegion(
                    session,
                    MagicProcessPacketWriter.Create(
                        MagicProcessOpcode.Effecting,
                        skillId,
                        (short)session.CharacterId,
                        targetId,
                        data),
                    excludeSender: false);
                return false;
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
