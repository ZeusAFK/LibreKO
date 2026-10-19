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
    private const int AreaTarget = -1;
    private const float MeleeReach = 6f;
    private const float PlayerBodyRadius = 1f;
    private const float RangedPlayerShortcut = 38f;
    // The client allows spells 15 m past their range on players; the rest covers movement.
    private const float SkillReachSlack = 20f;
    private const float RangeUnitsPerMetre = 10f;
    private const int PercentScale = 100;

    public async Task ExecuteAsync(UserSession session, MagicData magic, int skillId, int targetId, int[] data)
    {
        if (!MayReach(session, magic, skillId, targetId))
        {
            await MagicCombatHelper.SendMagicFailAsync(session, skillId);
            return;
        }

        await ExecuteTypeAsync(session, magic, magic.PrimaryType, skillId, targetId, data, isPrimary: true);

        if (magic.SecondaryType != MagicSkillType.None
            && HasTypeData(magic, magic.SecondaryType, skillId))
        {
            await ExecuteTypeAsync(
                session, magic, magic.SecondaryType, skillId, targetId, data, isPrimary: false);
        }

        await ExecuteExtendedEffectAsync(session, magic, skillId, targetId, data);
    }

    private const int ExtendedEffectOffset = 80000;

    private async Task ExecuteExtendedEffectAsync(
        UserSession caster, MagicData magic, int skillId, int targetId, int[] data)
    {
        if (magic.PrimaryType is not (MagicSkillType.Melee or MagicSkillType.Ranged or MagicSkillType.OverTime or MagicSkillType.Warp))
            return;

        var extendedId = skillId + ExtendedEffectOffset;
        if (gameDataService.GetMagic(extendedId) is not { PrimaryType: MagicSkillType.Buff } extended
            || !gameDataService.MagicType4Table.TryGetValue(extendedId, out var type4Data))
            return;

        if (sessionManager.GetByCharacterId(targetId) is not { } target || !PvpRules.CanAttackPlayer(caster, target))
            return;

        if (!StatusEffectChance.Lands(caster, target, (BuffType)type4Data.BuffType, StatusEffectChance.ExtendedRowChance))
            return;

        await magicStatusEffectService.ExecuteAsync(
            caster, extended, MagicSkillType.Buff, extendedId, targetId, data, isPrimary: false);
    }

    private bool MayReach(UserSession caster, MagicData magic, int skillId, int targetId)
    {
        if (targetId == AreaTarget || targetId == caster.CharacterId
            || (SkillMoral)magic.Moral is SkillMoral.Self or SkillMoral.SelfArea
            || magic.PrimaryType is not (MagicSkillType.Melee or MagicSkillType.Ranged or MagicSkillType.OverTime
                or MagicSkillType.Buff or MagicSkillType.Special))
            return true;

        if (sessionManager.GetByCharacterId(targetId) is { } player)
            return player.ZoneId == caster.ZoneId && player.Room == caster.Room
                && Within(caster, player.X, player.Z, PlayerBodyRadius, Reach(caster, magic, skillId))
                && MayTouch(caster, player, (SkillMoral)magic.Moral);

        if (sessionManager.Regions.GetNpc(targetId) is { } npc)
            return npc.ZoneId == caster.ZoneId && npc.Room == caster.Room
                && Within(caster, npc.X, npc.Z, npc.BulkRadius, Reach(caster, magic, skillId));

        return true;
    }

    private static bool MayTouch(UserSession caster, UserSession target, SkillMoral moral) => moral switch
    {
        SkillMoral.Enemy or SkillMoral.AreaEnemy or SkillMoral.Npc => PvpRules.CanAttackPlayer(caster, target),
        SkillMoral.FriendWithMe or SkillMoral.FriendExceptMe or SkillMoral.Party or SkillMoral.PartyAll
            or SkillMoral.AreaFriend or SkillMoral.Clan or SkillMoral.ClanAll or SkillMoral.CorpseFriend
            => !PvpRules.IsEnemy(caster, target),
        _ => true,
    };

    private float Reach(UserSession caster, MagicData magic, int skillId)
    {
        if (magic.PrimaryType == MagicSkillType.Ranged || magic.SecondaryType == MagicSkillType.Ranged)
            return Math.Max(Math.Max(magic.Range, RangedPlayerShortcut), BowReach(caster, magic, skillId));

        if (magic.Range > 0)
            return magic.Range;

        return MeleeReach;
    }

    private float BowReach(UserSession caster, MagicData magic, int skillId)
    {
        var bow = new[] { InventoryConstants.LeftHand, InventoryConstants.RightHand }
            .Select(slot => caster.GetEquippedItem(slot))
            .Where(slot => !slot.IsEmpty)
            .Select(slot => gameDataService.GetItem(slot.ItemId))
            .FirstOrDefault(item => item?.IsBow() == true);
        if (bow == null)
            return 0f;

        var addRange = MagicTypeLookup.TryResolve(gameDataService.MagicType2Table, magic, skillId, out var type2)
            ? Math.Max(PercentScale, (int)type2.AddRange)
            : PercentScale;
        return bow.Range / RangeUnitsPerMetre * addRange / PercentScale;
    }

    private static bool Within(UserSession caster, float x, float z, float targetRadius, float reach)
    {
        var allowed = reach + PlayerBodyRadius + targetRadius + SkillReachSlack;
        var dx = caster.X - x;
        var dz = caster.Z - z;
        return dx * dx + dz * dz <= allowed * allowed;
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
                await magicStatusEffectService.ExecuteAsync(session, magic, skillType, skillId, targetId, data, isPrimary);
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
