using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
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
    ILogger<CombatPacketCoordinator> logger) : ICombatPacketCoordinator
{
    private const short MaxSwingInterval = 500;
    private const short BareHandedSwingInterval = 100;

    public async Task HandleAttackAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || session.Hp <= 0)
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
            var damage = mayAttack
                ? GmMode.Taken(target, GmMode.Dealt(session, target.Hp,
                    PhysicalDamageCalculator.Calculate(session, PhysicalDefender.Of(target), gameDataService)))
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
        }
        else
        {
            var npcTarget = sessionManager.Regions.GetNpc(targetId);
            if (npcTarget != null && npcTarget.IsAlive && npcTarget.ZoneId == session.ZoneId
                && NpcHostility.IsAttackableBy(npcTarget, session))
            {
                combatLifecycleService.SetNpcAggro(npcTarget, session);

                var damage = GmMode.Dealt(session, npcTarget.Hp, PhysicalDamageCalculator.Calculate(
                    session, PhysicalDefender.Of(npcTarget), gameDataService));
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
        if (session == null)
            return;

        await combatLifecycleService.HandleRegeneAsync(client, session, packet.ReadByte());
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
            var result = BuildTargetHpPacket(targetId, echo, target.MaxHp, target.Hp, 0);
            await client.SendPacket(result);
            return;
        }

        var npc = sessionManager.Regions.GetNpc(targetId);
        if (npc == null || !npc.IsAlive || npc.ZoneId != session.ZoneId)
            return;

        var npcResult = BuildTargetHpPacket(targetId, echo, npc.MaxHp, npc.Hp, 0);
        await client.SendPacket(npcResult);
    }

    private static Packet BuildTargetHpPacket(int targetId, byte echo, int maxHp, int hp, int damage)
    {
        return TargetHpPacketWriter.Polled(targetId, echo, maxHp, hp, damage);
    }
}
