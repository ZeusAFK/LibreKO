using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.World;

using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IItemEquipmentEffectService
{
    Task ApplyMoveEffectsAsync(UserSession session, ItemMoveDirection direction, int sourceItemIdBeforeMove, int destinationItemIdBeforeMove);
    Task ApplyRemovalEffectsAsync(UserSession session, int removedItemId);
}

public class ItemEquipmentEffectService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IItemInventoryRuleService itemInventoryRuleService) : IItemEquipmentEffectService
{
    private const byte ItemOpOnEquip = 3;

    public async Task ApplyMoveEffectsAsync(UserSession session, ItemMoveDirection direction, int sourceItemIdBeforeMove, int destinationItemIdBeforeMove)
    {
        if (!itemInventoryRuleService.IsEquipmentChange(direction))
            return;

        RecalculateStats(session);

        if (itemInventoryRuleService.GetItemLeavingEquipment(direction, sourceItemIdBeforeMove, destinationItemIdBeforeMove) is int removedItemId
            && removedItemId > 0)
        {
            await RemoveEquippedItemOpsAsync(session, removedItemId);
        }

        if (itemInventoryRuleService.GetItemEnteringEquipment(direction, sourceItemIdBeforeMove, destinationItemIdBeforeMove) is int equippedItemId
            && equippedItemId > 0)
        {
            await TriggerItemOpsAsync(session, equippedItemId);
        }
    }

    public async Task ApplyRemovalEffectsAsync(UserSession session, int removedItemId)
    {
        RecalculateStats(session);
        await RemoveEquippedItemOpsAsync(session, removedItemId);
    }

    private async Task TriggerItemOpsAsync(UserSession session, int itemId)
    {
        var operations = gameDataService.GetItemOps(itemId) ?? [];
        foreach (var operation in operations)
        {
            if (operation.TriggerType != ItemOpOnEquip || operation.SkillId <= 0)
                continue;

            if (operation.TriggerRate < 100 && Random.Shared.Next(1, 101) > operation.TriggerRate)
                continue;

            var magic = gameDataService.GetMagic(operation.SkillId);
            if (magic == null)
                continue;

            await ApplyItemOpSkillAsync(session, magic, operation.SkillId);
        }
    }

    private async Task ApplyItemOpSkillAsync(UserSession session, MagicData magic, int skillId)
    {
        if (!MagicTypeLookup.TryResolve(gameDataService.MagicType4Table, magic, skillId, out var type4Data))
            return;

        var buff = new ActiveBuff
        {
            MagicId = skillId,
            CasterId = session.CharacterId,
            Duration = type4Data.Duration,
            ExpireTicks = type4Data.Duration > 0
                ? DateTime.UtcNow.AddSeconds(type4Data.Duration).Ticks
                : long.MaxValue,
            BuffType = (BuffType)type4Data.BuffType,
            SpecialAmount = type4Data.SpecialAmount > 0 ? type4Data.SpecialAmount : type4Data.ExpPct,
            BonusAc = type4Data.Ac,
            BonusAcPct = type4Data.AcPct,
            BonusAttack = type4Data.Attack,
            BonusMagicAttack = type4Data.MagicAttack,
            BonusMaxHp = type4Data.MaxHP,
            BonusMaxHpPct = type4Data.MaxHPPct,
            BonusMaxMp = type4Data.MaxMP,
            BonusMaxMpPct = type4Data.MaxMPPct,
            BonusHitRate = type4Data.HitRate,
            BonusAvoidRate = type4Data.AvoidRate,
            BonusStr = type4Data.Str,
            BonusSta = type4Data.Sta,
            BonusDex = type4Data.Dex,
            BonusIntel = type4Data.Intel,
            BonusCha = type4Data.Cha,
            BonusFireR = type4Data.FireR,
            BonusColdR = type4Data.ColdR,
            BonusLightningR = type4Data.LightningR,
            BonusMagicR = type4Data.MagicR,
            BonusPoisonR = type4Data.PoisonR,
            BonusDiseaseR = type4Data.DiseaseR,
            BonusSpeed = type4Data.Speed,
            BonusAttackSpeed = type4Data.AttackSpeed
        };

        session.ActiveBuffs[skillId] = buff;
        session.RecalculateStatsWithBuffs(gameDataService);

        await sessionManager.Regions.SendToRegion(
            session,
            MagicProcessPacketWriter.Create(
                MagicProcessOpcode.Effecting,
                skillId,
                (short)session.CharacterId,
                (short)session.CharacterId,
                [type4Data.Duration]),
            excludeSender: false);
    }

    private async Task RemoveEquippedItemOpsAsync(UserSession session, int itemId)
    {
        var operations = gameDataService.GetItemOps(itemId) ?? [];
        var removedBuffs = new List<(int SkillId, ActiveBuff Buff)>();

        foreach (var operation in operations)
        {
            if (operation.TriggerType != ItemOpOnEquip || operation.SkillId <= 0)
                continue;

            if (HasOtherEquippedItemOp(session, itemId, operation.SkillId))
                continue;

            if (!session.ActiveBuffs.TryRemove(operation.SkillId, out var removedBuff))
                continue;

            removedBuffs.Add((operation.SkillId, removedBuff));
        }

        if (removedBuffs.Count == 0)
            return;

        session.RecalculateStatsWithBuffs(gameDataService);

        foreach (var (skillId, buff) in removedBuffs)
        {
            if (buff.BuffType != BuffType.None)
            {
                await session.Client.SendPacket(
                    MagicProcessPacketWriter.CreateDurationExpired((byte)buff.BuffType));
                continue;
            }

            await sessionManager.Regions.SendToRegion(
                session,
                MagicProcessPacketWriter.Create(
                    MagicProcessOpcode.DurationExpired,
                    skillId,
                    (short)session.CharacterId,
                    (short)session.CharacterId),
                excludeSender: false);
        }
    }

    private bool HasOtherEquippedItemOp(UserSession session, int removedItemId, int skillId)
    {
        for (var slotIndex = 0; slotIndex < InventoryConstants.SlotMax; slotIndex++)
        {
            var equippedItem = session.Inventory[slotIndex];
            if (equippedItem.ItemId == 0 || equippedItem.ItemId == removedItemId)
                continue;

            var operations = gameDataService.GetItemOps(equippedItem.ItemId) ?? [];
            if (operations.Any(operation => operation.TriggerType == ItemOpOnEquip && operation.SkillId == skillId))
                return true;
        }

        return false;
    }

    private void RecalculateStats(UserSession session)
    {
        var coefficient = gameDataService.GetCoefficient(session.Class);
        if (coefficient != null)
            session.RecalculateStats(coefficient, gameDataService);
    }
}
