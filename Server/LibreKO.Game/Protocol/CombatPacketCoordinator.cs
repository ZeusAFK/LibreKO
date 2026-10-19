using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface ICombatPacketCoordinator
{
    Task HandleAttackAsync(IClient client, Packet packet);
    Task HandleRegeneAsync(IClient client, Packet packet);
    Task HandleSkillDataAsync(IClient client, Packet packet);
    Task HandleTargetHpAsync(IClient client, Packet packet);
}

public class CombatPacketCoordinator(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IMagicItemUsageService magicItemUsageService,
    ICombatLifecycleService combatLifecycleService,
    IStealthService stealthService,
    IResurrectionOfferService resurrectionOfferService,
    IQuestDefinitionSource questDefinitions,
    ILogger<CombatPacketCoordinator> logger) : ICombatPacketCoordinator
{
    private const short MaxSwingInterval = 500;
    private const short BareHandedSwingInterval = 100;

    public async Task HandleAttackAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || session.Hp <= 0 || session.IsBlinking)
            return;

        if (packet.RemainingBytes < 10)
            return;

        var attackType = packet.ReadByte();
        _ = packet.ReadByte();
        var targetId = packet.ReadInt();
        var delayTime = packet.ReadShort();
        var distance = packet.ReadShort();
        var criticalHit = packet.RemainingBytes > 0 ? packet.ReadByte() : AttackPacketWriter.NoCritical;

        if (delayTime > MaxSwingInterval)
            return;

        await stealthService.RevealAsync(session, InvisibilityType.None);
        session.MarkCombat();

        var equippedWeapon = session.GetEquippedItem(InventoryConstants.RightHand);
        if (equippedWeapon.IsEmpty)
            equippedWeapon = session.GetEquippedItem(InventoryConstants.LeftHand);

        ItemData? weaponProto = null;
        var isBowAttack = false;
        if (!equippedWeapon.IsEmpty)
        {
            weaponProto = gameDataService.GetItem(equippedWeapon.ItemId);
            isBowAttack = weaponProto?.IsBow() == true;
            if (weaponProto != null && (delayTime < weaponProto.Delay || distance > weaponProto.Range))
            {
                logger.LogDebug(
                    "Attack rejected for {Name}: weapon={Item} delay={Delay}/{NeedDelay} distance={Distance}/{Range}",
                    session.Name, equippedWeapon.ItemId, delayTime, weaponProto.Delay, distance, weaponProto.Range);
                return;
            }
        }
        else if (delayTime < BareHandedSwingInterval)
        {
            return;
        }

        if (isBowAttack && !magicItemUsageService.HasArrows(session))
        {
            logger.LogDebug("Attack rejected for {Name}: bow equipped with no arrows", session.Name);
            return;
        }

        var attackResult = AttackResult.Failed;

        var target = sessionManager.GetByCharacterId(targetId);
        if (target != null)
        {
            var mayAttack = target.Hp > 0 && PvpRules.CanAttackPlayer(session, target);
            var hit = mayAttack
                ? PhysicalDamageCalculator.CalculateHit(session, PhysicalDefender.Of(target), gameDataService)
                : default;
            var damage = mayAttack
                ? GmMode.Taken(target, GmMode.Dealt(session, target.Hp, hit.Damage))
                : 0;
            if (damage <= 0)
            {
                logger.LogDebug(
                    "PvP swing by {Name} on {Target} did nothing: mayAttack={May} arena={A}/{B} zone={Zone} hit={Hit} targetAc={Ac}",
                    session.Name, target.Name, mayAttack, session.ArenaId, target.ArenaId,
                    session.ZoneId, session.Stats.TotalHit, target.Stats.TotalAc);
            }
            if (damage > 0)
            {
                target.Hp -= (short)Math.Min(damage, target.Hp);
                target.MarkCombat();
                await combatLifecycleService.SendHpChangeAsync(target, session.CharacterId);
                await combatLifecycleService.SendPlayerTargetHpAsync(session, target, damage);

                if (target.Hp <= 0)
                {
                    attackResult = AttackResult.TargetDead;
                    await combatLifecycleService.HandlePlayerDeathAsync(target, session);
                }
                else
                {
                    attackResult = AttackResult.Succeeded;
                }
            }

            await ItemHitEffects.ApplyAsync(session, target, hit.Items, hit.DrainBase, combatLifecycleService, gameDataService);
        }
        else
        {
            var npcTarget = sessionManager.Regions.GetNpc(targetId);
            if (npcTarget != null && npcTarget.IsAlive && npcTarget.ZoneId == session.ZoneId
                && NpcHostility.IsAttackableBy(npcTarget, session))
            {
                combatLifecycleService.SetNpcAggro(npcTarget, session);

                var hit = PhysicalDamageCalculator.CalculateHit(session, PhysicalDefender.Of(npcTarget), gameDataService);
                var damage = GmMode.Dealt(session, npcTarget.Hp, hit.Damage);
                if (damage > 0)
                {
                    npcTarget.Hp = Math.Max(0, npcTarget.Hp - damage);
                    npcTarget.RecordDamage(session.CharacterId, damage, session, id => sessionManager.GetByCharacterId(id));
                    await combatLifecycleService.SendNpcTargetHpAsync(session, npcTarget, damage);

                    if (npcTarget.Hp <= 0)
                    {
                        attackResult = AttackResult.TargetDead;
                        await combatLifecycleService.HandleNpcDeathAsync(npcTarget, session);
                    }
                    else
                    {
                        attackResult = AttackResult.Succeeded;
                    }
                }

                await ItemHitEffects.ApplyAsync(session, null, hit.Items, hit.DrainBase, combatLifecycleService, gameDataService);
            }
        }

        if (attackResult != AttackResult.Failed && isBowAttack)
            _ = await magicItemUsageService.TryConsumeArrowAsync(session);

        var result = AttackPacketWriter.Create(
            attackType, attackResult, session.CharacterId, targetId, criticalHit);
        await sessionManager.Regions.SendToRegion(session, result, excludeSender: false);
    }

    public async Task HandleRegeneAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var regeneType = packet.ReadByte();
        switch (regeneType)
        {
            case RegeneResurrectChoice when packet.RemainingBytes >= 5:
                await HandleResurrectChoiceAsync(client, session, packet.ReadByte(), packet.ReadInt());
                break;
            case RegeneResurrectAccept when packet.RemainingBytes >= 4:
                await resurrectionOfferService.AcceptAsync(session, packet.ReadInt());
                break;
            case RegeneResurrectChoice or RegeneResurrectAccept:
                break;
            default:
                await combatLifecycleService.HandleRegeneAsync(client, session, regeneType);
                break;
        }
    }

    private const byte RegeneResurrectChoice = 4;
    private const byte RegeneResurrectAccept = 5;

    private async Task HandleResurrectChoiceAsync(IClient client, UserSession session, byte choice, int casterId)
    {
        resurrectionOfferService.Withdraw(session, casterId);
        if (session.Hp > 0)
            return;

        if (choice != ResurrectionOfferService.ChoiceInPlace)
        {
            await combatLifecycleService.HandleRegeneAsync(client, session, RegenePacketWriter.RespawnNormal);
            return;
        }

        if (await resurrectionOfferService.PayInPlaceAsync(session))
            await combatLifecycleService.RespawnInPlaceAsync(client, session);
    }

    public async Task HandleSkillDataAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
            return;

        var subOpcode = packet.ReadByte();
        if (subOpcode == (byte)SkillBarSubOpcode.Save)
        {
            var count = packet.ReadShort();
            if (count <= 0 || count > SkillBarRequest.MaxSlots)
                return;

            var data = new byte[count * SkillBarRequest.BytesPerSlot];
            for (var index = 0; index < count; index++)
            {
                BitConverter.TryWriteBytes(
                    data.AsSpan(index * SkillBarRequest.BytesPerSlot), packet.ReadInt());
            }

            session.SkillData = data;
            logger.LogDebug("Skill data saved for {Name}: {Count} skills", session.Name, count);

            await client.SendPacket(SkillDataPacketWriter.Slots(data));
            return;
        }

        if (subOpcode == (byte)SkillBarSubOpcode.Load)
            await client.SendPacket(SkillDataPacketWriter.Slots(session.SkillData));
    }

    public async Task HandleTargetHpAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
            return;

        if (packet.RemainingBytes < 5)
            return;

        var targetId = packet.ReadInt();
        var echo = packet.ReadByte();

        var target = sessionManager.GetByCharacterId(targetId);
        if (target != null)
        {
            if (!CanSeeHpOf(session, target))
                return;

            var result = BuildTargetHpPacket(targetId, echo, target.MaxHp, target.Hp, 0);
            await client.SendPacket(result);
            return;
        }

        var npc = sessionManager.Regions.GetNpc(targetId);
        if (npc == null || !npc.IsAlive || npc.ZoneId != session.ZoneId)
            return;

        var drops = TargetQuestDrops.For(session, npc, gameDataService, questDefinitions);
        await client.SendPacket(TargetHpPacketWriter.Polled(targetId, echo, npc.MaxHp, npc.Hp, 0, drops));
    }

    private static bool CanSeeHpOf(UserSession viewer, UserSession target)
        => viewer.CharacterId == target.CharacterId
        || viewer.IsGM
        || (RegionManager.IsInView(viewer, target)
            && (!target.IsInvisible || target.Nation == viewer.Nation));

    private static Packet BuildTargetHpPacket(int targetId, byte echo, int maxHp, int hp, int damage)
    {
        return TargetHpPacketWriter.Polled(targetId, echo, maxHp, hp, damage);
    }
}
