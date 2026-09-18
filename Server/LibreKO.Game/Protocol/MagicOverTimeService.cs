using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public class MagicOverTimeService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IMagicItemUsageService magicItemUsageService,
    ICombatLifecycleService combatLifecycleService,
    ICombatNotificationService combatNotificationService,
    IEventSystemsPacketCoordinator eventSystemsPacketCoordinator,
    IUserNotificationService userNotificationService,
    ILogger<MagicOverTimeService> logger)
{
    private const byte PotionItemGroup = 9;
    private const byte OverTimeTickSeconds = 2;

    private const int ItemGrantedSkillIdBase = 400000;
    private const int PercentScale = 100;
    private const int ManaDrainCasterShare = 2;

    private static byte AttributeToPartyStatus(byte attribute) => (byte)((MagicAttribute)attribute switch
    {
        MagicAttribute.Disease => PartyStatusIcon.Disease,
        MagicAttribute.Poison => PartyStatusIcon.Poison,
        _ => PartyStatusIcon.OverTimeDamage,
    });
    public async Task ExecuteAsync(UserSession caster, MagicData magic, int skillId, int targetId, int[] data)
    {
        if (!MagicTypeLookup.TryResolve(gameDataService.MagicType3Table, magic, skillId, out var type3Data))
        {
            await MagicCombatHelper.SendMagicFailAsync(caster, skillId);
            return;
        }

        if (magic.UseItem != 0)
        {
            if ((magic.ItemGroup == PotionItemGroup && !caster.CanUsePotions)
                || !magicItemUsageService.CanUseItem(caster, magic.UseItem))
            {
                logger.LogWarning(
                    "Skill {SkillId} refused for {Name}: item {ItemId} is {Reason} (class {Class}, level {Level})",
                    skillId, caster.Name, magic.UseItem,
                    magicItemUsageService.CheckItem(caster, magic.UseItem), caster.Class, caster.Level);
                await MagicCombatHelper.SendMagicFailAsync(caster, skillId);
                return;
            }
        }

        if ((MagicDirectType)type3Data.DirectType == MagicDirectType.AngerExplosion)
        {
            if (!caster.HasFullAngerGauge)
            {
                await MagicCombatHelper.SendMagicFailAsync(caster, skillId);
                return;
            }

            await eventSystemsPacketCoordinator.UpdateAngerGaugeAsync(caster, 0);
        }

        var isHeal = (SkillMoral)magic.Moral is SkillMoral.Self or SkillMoral.FriendWithMe
            or SkillMoral.FriendExceptMe or SkillMoral.Party or SkillMoral.PartyAll or SkillMoral.AreaFriend;
        var playerTargets = new List<UserSession>();
        var npcTargets = new List<NpcInstance>();
        ResolveOverTimeTargets(caster, magic, type3Data, targetId, data, isHeal, playerTargets, npcTargets);

        if (targetId != -1 && playerTargets.Count == 0 && npcTargets.Count == 0)
        {
            await MagicCombatHelper.SendMagicFailAsync(caster, skillId);
            return;
        }

        var actualTargetIds = new HashSet<int>();

        foreach (var playerTarget in playerTargets)
        {
            await ApplyOverTimeToPlayerAsync(caster, playerTarget, skillId, type3Data, isHeal);
            actualTargetIds.Add(playerTarget.CharacterId);
        }

        foreach (var npcTarget in npcTargets)
        {
            await ApplyOverTimeToNpcAsync(caster, npcTarget, skillId, type3Data, isHeal);
            actualTargetIds.Add(npcTarget.UniqueId);
        }

        if (magic.UseItem != 0 && !await magicItemUsageService.TryConsumeItemAsync(caster, magic.UseItem))
        {
            await MagicCombatHelper.SendMagicFailAsync(caster, skillId);
            return;
        }

        var isAreaEffect = IsAreaEffect(type3Data, targetId, data);

        if (isAreaEffect && actualTargetIds.Count > 0)
        {
            foreach (var actualTargetId in actualTargetIds)
            {
                await sessionManager.Regions.SendToRegion(
                    caster,
                    MagicProcessPacketWriter.Create(
                        MagicProcessOpcode.Effecting,
                        skillId,
                        (short)caster.CharacterId,
                        actualTargetId,
                        data),
                    excludeSender: false);
            }
        }

        if (isAreaEffect)
        {
            await sessionManager.Regions.SendToRegion(
                caster,
                MagicProcessPacketWriter.Create(
                    MagicProcessOpcode.Effecting,
                    skillId,
                    (short)caster.CharacterId,
                    -1,
                    data),
                excludeSender: false);
            return;
        }

        await sessionManager.Regions.SendToRegion(
            caster,
            MagicProcessPacketWriter.Create(
                MagicProcessOpcode.Effecting,
                skillId,
                (short)caster.CharacterId,
                targetId,
                data),
            excludeSender: false);
    }

    private void ResolveOverTimeTargets(
        UserSession caster,
        MagicData magic,
        MagicType3Data type3Data,
        int targetId,
        int[] data,
        bool isHeal,
        List<UserSession> playerTargets,
        List<NpcInstance> npcTargets)
    {
        if (type3Data.Radius > 0 && (targetId == -1 || HasAreaCoordinates(data)))
        {
            ResolveAreaTargets(caster, magic, type3Data, data, playerTargets, npcTargets);
            return;
        }

        if (isHeal)
        {
            var playerTarget = targetId == caster.CharacterId
                ? caster
                : sessionManager.GetByCharacterId(targetId) ?? caster;

            if (playerTarget.Hp > 0)
                playerTargets.Add(playerTarget);
            return;
        }

        var directPlayerTarget = sessionManager.GetByCharacterId(targetId);
        if (directPlayerTarget != null && directPlayerTarget.Hp > 0)
        {
            playerTargets.Add(directPlayerTarget);
            return;
        }

        var npcTarget = sessionManager.Regions.GetNpc(targetId);
        if (npcTarget != null && npcTarget.IsAlive && npcTarget.ZoneId == caster.ZoneId
            && NpcHostility.IsAttackableBy(npcTarget, caster))
            npcTargets.Add(npcTarget);
    }

    private void ResolveAreaTargets(
        UserSession caster,
        MagicData magic,
        MagicType3Data type3Data,
        int[] data,
        List<UserSession> playerTargets,
        List<NpcInstance> npcTargets)
    {
        var centerX = GetAreaCoordinate(data.Length > 0 ? data[0] : 0, caster.X);
        var centerZ = GetAreaCoordinate(data.Length > 2 ? data[2] : 0, caster.Z);
        var radiusSq = type3Data.Radius * type3Data.Radius;

        if (ShouldAffectAreaPlayer(caster, caster, magic.Moral)
            && IsWithinAreaRadius(caster.X, caster.Z, centerX, centerZ, radiusSq))
        {
            playerTargets.Add(caster);
        }

        foreach (var player in sessionManager.Regions.GetNearbyUsers(caster))
        {
            if (!ShouldAffectAreaPlayer(caster, player, magic.Moral))
                continue;

            if (IsWithinAreaRadius(player.X, player.Z, centerX, centerZ, radiusSq))
                playerTargets.Add(player);
        }

        if (!ShouldAffectAreaNpc(magic.Moral))
            return;

        foreach (var npc in sessionManager.Regions.GetNearbyNpcs(caster))
        {
            if (!npc.IsAlive || npc.ZoneId != caster.ZoneId || !NpcHostility.IsAttackableBy(npc, caster))
                continue;

            if (IsWithinAreaRadius(npc.X, npc.Z, centerX, centerZ, radiusSq))
                npcTargets.Add(npc);
        }
    }

    private async Task ApplyOverTimeToPlayerAsync(
        UserSession caster,
        UserSession target,
        int skillId,
        MagicType3Data type3Data,
        bool isHeal)
    {
        var directType = (MagicDirectType)type3Data.DirectType;

        if (directType is MagicDirectType.Durability
            or MagicDirectType.DurabilityAttack
            or MagicDirectType.Destination)
            return;

        if (isHeal)
        {
            await RestorePlayerAsync(caster, target, type3Data, directType);
        }
        else
        {
            if (target.CharacterId != caster.CharacterId && !PvpRules.CanAttackPlayer(caster, target))
                return;

            if (directType == MagicDirectType.ManaDrain)
            {
                await DrainPlayerManaAsync(caster, target, type3Data);
            }
            else
            {
                var immediateDamage = CalculateImmediateDamageForPlayer(caster, target, skillId, type3Data, directType);
                if (immediateDamage > 0)
                {
                    target.Hp = (short)Math.Max(0, target.Hp - immediateDamage);
                    await combatLifecycleService.SendHpChangeAsync(target, caster.CharacterId);
                    await combatLifecycleService.SendPlayerTargetHpAsync(caster, target, immediateDamage);

                    if (directType is MagicDirectType.DamageAbsorb or MagicDirectType.PercentDrain)
                        await RestoreCasterHealthAsync(caster, immediateDamage);

                    if (target.Hp <= 0)
                    {
                        target.ActiveOverTimeEffects.TryRemove(skillId, out _);
                        await combatLifecycleService.HandlePlayerDeathAsync(target, caster);
                        return;
                    }
                }
            }
        }

        var durationTotal = CalculateTickAmountForPlayer(caster, target, type3Data);
        ScheduleOverTimeEffect(target.ActiveOverTimeEffects, skillId, caster.CharacterId, durationTotal, type3Data.Duration);

        if (!isHeal && durationTotal < 0
            && target.ActiveOverTimeEffects.TryGetValue(skillId, out var applied))
        {
            applied.PartyStatusCode = AttributeToPartyStatus(type3Data.Attribute);
            await combatNotificationService.SendPartyStatusUpdateAsync(target, applied.PartyStatusCode, applied: true);
        }
    }

    private async Task ApplyOverTimeToNpcAsync(
        UserSession caster,
        NpcInstance npc,
        int skillId,
        MagicType3Data type3Data,
        bool isHeal)
    {
        var directType = (MagicDirectType)type3Data.DirectType;

        if (directType is MagicDirectType.Durability
            or MagicDirectType.DurabilityAttack
            or MagicDirectType.Destination
            or MagicDirectType.ManaDrain)
            return;

        if (!isHeal)
            combatLifecycleService.SetNpcAggro(npc, caster);

        var delta = directType is MagicDirectType.HealthPercent or MagicDirectType.PercentDrain
            ? PercentOfHealth(npc.Hp, npc.MaxHp, type3Data.FirstDamage)
            : type3Data.FirstDamage;

        if (delta != 0)
        {
            if (delta > 0)
            {
                npc.Hp = Math.Min(npc.MaxHp, npc.Hp + delta);
            }
            else
            {
                var damage = ScalesWithMagicAttack(directType, skillId)
                    ? MagicCombatHelper.GetMagicDamage(caster, npc, -delta, type3Data.Attribute, gameDataService)
                    : -delta;
                npc.Hp = Math.Max(0, npc.Hp - damage);
                npc.RecordDamage(caster.CharacterId, damage, caster, id => sessionManager.GetByCharacterId(id));
                await combatLifecycleService.SendNpcTargetHpAsync(caster, npc, damage);

                if (npc.Hp <= 0)
                {
                    npc.ActiveOverTimeEffects.TryRemove(skillId, out _);
                    await combatLifecycleService.HandleNpcDeathAsync(npc, caster);
                    return;
                }
            }
        }

        var durationTotal = CalculateTickAmountForNpc(caster, npc, type3Data);
        ScheduleOverTimeEffect(npc.ActiveOverTimeEffects, skillId, caster.CharacterId, durationTotal, type3Data.Duration);
    }

    private async Task RestorePlayerAsync(
        UserSession caster, UserSession target, MagicType3Data type3Data, MagicDirectType directType)
    {
        if (directType is MagicDirectType.HealthPurchase or MagicDirectType.ManaPurchase)
            await ChargeRestorationAsync(caster, type3Data.TimeDamage);

        if (directType is MagicDirectType.Mana or MagicDirectType.ManaShell or MagicDirectType.ManaPurchase)
        {
            if (type3Data.FirstDamage > 0)
            {
                target.Mp = (short)Math.Min(target.MaxMp, target.Mp + type3Data.FirstDamage);
                await combatLifecycleService.SendMspChangeAsync(target);
            }

            return;
        }

        var restored = directType == MagicDirectType.HealthPercent
            ? PercentOfHealth(target.Hp, target.MaxHp, type3Data.FirstDamage)
            : type3Data.FirstDamage;

        if (restored > 0)
        {
            target.Hp = (short)Math.Min(target.MaxHp, target.Hp + restored);
            await combatLifecycleService.SendHpChangeAsync(target);
        }
    }

    private async Task ChargeRestorationAsync(UserSession caster, int cost)
    {
        if (cost <= 0)
            return;

        caster.Money = Math.Max(0, caster.Money - cost);
        await userNotificationService.SendGoldLossAsync(caster, cost);
    }

    private async Task RestoreCasterHealthAsync(UserSession caster, int amount)
    {
        if (amount <= 0 || caster.Hp <= 0)
            return;

        caster.Hp = (short)Math.Min(caster.MaxHp, caster.Hp + amount);
        await combatLifecycleService.SendHpChangeAsync(caster);
    }

    private async Task DrainPlayerManaAsync(UserSession caster, UserSession target, MagicType3Data type3Data)
    {
        var drained = Math.Min((int)target.Mp, Math.Abs(type3Data.FirstDamage));
        if (drained <= 0)
            return;

        target.Mp = (short)(target.Mp - drained);
        await combatLifecycleService.SendMspChangeAsync(target);
        await RestoreCasterHealthAsync(caster, drained / ManaDrainCasterShare);
    }

    private static int PercentOfHealth(int currentHp, int maxHp, int firstDamage) =>
        firstDamage < PercentScale
            ? firstDamage * currentHp / -PercentScale
            : maxHp * (firstDamage - PercentScale) / PercentScale;

    private static bool ScalesWithMagicAttack(MagicDirectType directType, int skillId) =>
        directType is MagicDirectType.Health or MagicDirectType.DamageAbsorb
        && skillId < ItemGrantedSkillIdBase;

    private int CalculateImmediateDamageForPlayer(
        UserSession caster,
        UserSession target,
        int skillId,
        MagicType3Data type3Data,
        MagicDirectType directType)
    {
        if (target.BlockMagic)
            return 0;

        if (directType is MagicDirectType.HealthPercent or MagicDirectType.PercentDrain)
            return Math.Max(0, -PercentOfHealth(target.Hp, target.MaxHp, type3Data.FirstDamage));

        if (type3Data.FirstDamage >= 0)
            return 0;

        return ScalesWithMagicAttack(directType, skillId)
            ? MagicCombatHelper.GetMagicDamage(caster, target, Math.Abs(type3Data.FirstDamage), type3Data.Attribute, gameDataService)
            : Math.Abs(type3Data.FirstDamage);
    }

    private int CalculateTickAmountForPlayer(UserSession caster, UserSession target, MagicType3Data type3Data)
    {
        if (type3Data.Duration == 0 || type3Data.TimeDamage == 0)
            return 0;

        if ((MagicDirectType)type3Data.DirectType == MagicDirectType.HealthBooster)
            return type3Data.Duration / OverTimeTickSeconds * ((int)(caster.Level * (1 + caster.Level / 30.0)) + 3);

        if (type3Data.TimeDamage < 0)
        {
            if (target.BlockMagic)
                return 0;

            return (MagicAttribute)type3Data.Attribute != MagicAttribute.Magic
                ? -MagicCombatHelper.GetMagicDamage(caster, target, Math.Abs(type3Data.TimeDamage), type3Data.Attribute, gameDataService)
                : type3Data.TimeDamage;
        }

        return type3Data.TimeDamage;
    }

    private int CalculateTickAmountForNpc(UserSession caster, NpcInstance npc, MagicType3Data type3Data)
    {
        if (type3Data.Duration == 0 || type3Data.TimeDamage == 0)
            return 0;

        if ((MagicDirectType)type3Data.DirectType == MagicDirectType.HealthBooster)
            return type3Data.Duration / OverTimeTickSeconds * ((int)(caster.Level * (1 + caster.Level / 30.0)) + 3);

        if (type3Data.TimeDamage < 0)
        {
            return -MagicCombatHelper.GetMagicDamage(caster, npc, Math.Abs(type3Data.TimeDamage), type3Data.Attribute, gameDataService);
        }

        return type3Data.TimeDamage;
    }

    private static void ScheduleOverTimeEffect(
        IDictionary<int, ActiveOverTimeEffect> activeEffects,
        int skillId,
        int casterId,
        int totalAmount,
        byte duration)
    {
        if (duration == 0 || totalAmount == 0)
        {
            activeEffects.Remove(skillId);
            return;
        }

        var tickCountFloat = duration / (float)OverTimeTickSeconds;
        var tickLimit = Math.Max(1, (int)tickCountFloat);
        var tickAmount = (short)(totalAmount / tickCountFloat);
        if (tickAmount == 0)
        {
            activeEffects.Remove(skillId);
            return;
        }

        activeEffects[skillId] = new ActiveOverTimeEffect
        {
            MagicId = skillId,
            CasterId = casterId,
            TickAmount = tickAmount,
            TickIntervalSeconds = OverTimeTickSeconds,
            TickCount = 0,
            TickLimit = (byte)Math.Min(byte.MaxValue, tickLimit),
            NextTickTicks = DateTime.UtcNow.AddSeconds(OverTimeTickSeconds).Ticks
        };
    }

    private static bool ShouldAffectAreaPlayer(UserSession caster, UserSession target, byte moral)
    {
        if (target.Hp <= 0)
            return false;

        if (target.CharacterId == caster.CharacterId)
            return (SkillMoral)moral is SkillMoral.Self or SkillMoral.FriendWithMe or SkillMoral.FriendExceptMe
                or SkillMoral.Party or SkillMoral.PartyAll or SkillMoral.AreaFriend or SkillMoral.AreaAll;

        var isAlly = target.Nation == caster.Nation;
        var isFoe = PvpRules.IsEnemy(caster, target);
        return (SkillMoral)moral switch
        {
            SkillMoral.AreaEnemy or SkillMoral.SelfArea => isFoe,
            SkillMoral.AreaFriend => isAlly,
            SkillMoral.AreaAll => true,
            SkillMoral.Npc or SkillMoral.Enemy or SkillMoral.All or SkillMoral.None => isFoe,
            SkillMoral.Self or SkillMoral.FriendWithMe or SkillMoral.FriendExceptMe
                or SkillMoral.Party or SkillMoral.PartyAll => isAlly,
            _ => isFoe
        };
    }

    private static bool ShouldAffectAreaNpc(byte moral) =>
        (SkillMoral)moral is SkillMoral.AreaEnemy or SkillMoral.AreaAll or SkillMoral.SelfArea
            or SkillMoral.Npc or SkillMoral.Enemy or SkillMoral.All or SkillMoral.None;

    private static bool IsWithinAreaRadius(float x, float z, float centerX, float centerZ, float radiusSq)
    {
        var dx = x - centerX;
        var dz = z - centerZ;
        return dx * dx + dz * dz <= radiusSq;
    }

    private static bool HasAreaCoordinates(int[] data) =>
        (data.Length > 0 && data[0] != 0)
        || (data.Length > 2 && data[2] != 0);

    private static bool IsAreaEffect(MagicType3Data type3Data, int targetId, int[] data) =>
        type3Data.Radius > 0 && (targetId == -1 || HasAreaCoordinates(data));

    private static float GetAreaCoordinate(int value, float fallback)
    {
        if (value == 0)
            return fallback;

        return value;
    }
}
