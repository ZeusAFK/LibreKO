using LibreKO.Common.Enums;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IChaoticGeneratorService
{
    Task HandlePieceExchangeAsync(UserSession session, Packet packet);
}

public class ChaoticGeneratorService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService,
    ILogger<ChaoticGeneratorService> logger) : IChaoticGeneratorService
{

    private const byte ObjectArtifact = 9;
    private const byte NoticeRareItem = 4;

    private const int ExchangeCooldownMs = 1500;
    private const byte RandomFlagFirst = 1;
    private const byte RandomFlagLast = 3;
    private const int RewardWeightDivisor = 5;
    private const int RewardSlotMax = 10000;
    private const int RareNoticeItemId = 379068000;
    private const int RequestBytes = 4 + 4 + 1;

    private readonly Lock rewardLock = new();
    private Dictionary<int, List<ItemExchangeData>>? rewardsByPiece;

    public async Task HandlePieceExchangeAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < RequestBytes)
        {
            await SendFailureAsync(session, ItemUpgradePacketWriter.BifrostResult.Rejected);
            return;
        }

        var npcUniqueId = packet.ReadInt();
        var pieceItemId = packet.ReadInt();
        var sourcePosition = unchecked((sbyte)packet.ReadByte());

        var now = DateTime.UtcNow;
        if (now < session.LastPieceExchangeTime.AddMilliseconds(ExchangeCooldownMs)
            || session.Stats.ItemWeight >= session.Stats.MaxWeight)
        {
            await SendFailureAsync(session, ItemUpgradePacketWriter.BifrostResult.Rejected);
            return;
        }

        session.LastPieceExchangeTime = now;

        if (!IsAtGenerator(session, npcUniqueId))
        {
            await SendFailureAsync(session, ItemUpgradePacketWriter.BifrostResult.Rejected);
            return;
        }

        var pieceData = gameDataService.GetItem(pieceItemId);
        if (pieceData == null || pieceData.Countable == 0
            || pieceData.Effect2 != ItemData.Effect2ExchangePiece)
        {
            logger.LogDebug(
                "Piece exchange rejected for {Name}: item {ItemId} is not a generator piece (countable={Countable} effect2={Effect2})",
                session.Name, pieceItemId, pieceData?.Countable, pieceData?.Effect2);
            await SendFailureAsync(session, ItemUpgradePacketWriter.BifrostResult.NoPiece);
            return;
        }

        if (sourcePosition < 0 || sourcePosition >= InventoryConstants.HaveMax)
        {
            await SendFailureAsync(session, ItemUpgradePacketWriter.BifrostResult.NoPiece);
            return;
        }

        var sourceSlot = session.Inventory[InventoryConstants.InventoryStart + sourcePosition];
        if (sourceSlot.IsEmpty || sourceSlot.ItemId != pieceItemId || sourceSlot.Count == 0
            || IsExchangeBlockedByFlag(sourceSlot.State))
        {
            await SendFailureAsync(session, ItemUpgradePacketWriter.BifrostResult.NoPiece);
            return;
        }

        if (!HasFreeInventorySlot(session))
        {
            await SendFailureAsync(session, ItemUpgradePacketWriter.BifrostResult.Rejected);
            return;
        }

        var rewardItemId = RollReward(pieceItemId);
        var rewardData = rewardItemId == 0 ? null : gameDataService.GetItem(rewardItemId);
        if (rewardData == null
            || session.Stats.ItemWeight + rewardData.Weight >= session.Stats.MaxWeight)
        {
            await SendFailureAsync(session, ItemUpgradePacketWriter.BifrostResult.Rejected);
            return;
        }

        var rewardSlotIndex = session.FindSlotForItem(rewardItemId, gameDataService);
        if (rewardSlotIndex < 0)
        {
            await SendFailureAsync(session, ItemUpgradePacketWriter.BifrostResult.Rejected);
            return;
        }

        var remaining = (ushort)(sourceSlot.Count - 1);
        var sourceDurability = sourceSlot.Durability;
        sourceSlot.Count = remaining;
        if (remaining == 0)
            sourceSlot.Clear();

        var rewardSlot = session.Inventory[rewardSlotIndex];
        var isNewItem = rewardSlot.IsEmpty;
        rewardSlot.ItemId = rewardItemId;
        rewardSlot.Count += 1;
        if (isNewItem)
            rewardSlot.Durability = rewardData.Duration;

        var effect = RarityEffect(rewardData);
        var grantedPosition = (byte)(rewardSlotIndex - InventoryConstants.InventoryStart);

        logger.LogInformation(
            "Piece exchange for {Name}: piece {PieceItemId}@{SourcePosition} -> item {RewardItemId}@{RewardPosition} effect={Effect}",
            session.Name, pieceItemId, sourcePosition, rewardItemId, grantedPosition, effect);

        var result = ItemUpgradePacketWriter.BifrostExchangeSucceeded(
            rewardItemId, grantedPosition, pieceItemId, (byte)sourcePosition, effect);
        await session.Client.SendPacket(result);

        await userNotificationService.SendStackChangeAsync(
            session, (byte)(InventoryConstants.InventoryStart + sourcePosition),
            pieceItemId, remaining, sourceDurability);
        await userNotificationService.SendStackChangeAsync(
            session, (byte)rewardSlotIndex, rewardItemId, rewardSlot.Count, rewardSlot.Durability, isNewItem);

        session.RecalculateStatsWithBuffs(gameDataService);
        await userNotificationService.SendWeightChangeAsync(session);

        var artifact = MiscPacketWriter.ObjectEvent(ObjectArtifact, (byte)effect, npcUniqueId);
        await sessionManager.Regions.SendToRegion(session, artifact, excludeSender: false);

        if (rewardData.ItemType == ItemData.TypeKrowaz || rewardItemId == RareNoticeItemId)
            await SendRareNoticeAsync(session, rewardItemId);
    }

    private async Task SendRareNoticeAsync(UserSession session, int rewardItemId)
    {
        var notice = LogosShoutPacketWriter.RareItemAnnouncement(
            LogosShoutSubOpcode.Broadcast, NoticeRareItem, session.Name, rewardItemId, (byte)session.Nation);
        await sessionManager.BroadcastToAll(notice);
    }

    private bool IsAtGenerator(UserSession session, int npcUniqueId)
    {
        if (session.ZoneId != BattleZoneManager.ZONE_MORADON
            || session.Hp <= 0
            || session.Trade.IsTrading
            || session.Trade.IsMerchanting
            || session.IsGathering)
            return false;

        var npc = sessionManager.Regions.GetNpc(npcUniqueId);
        if (npc == null || !npc.IsAlive)
            return false;

        var dx = session.X - npc.X;
        var dz = session.Z - npc.Z;
        if (dx * dx + dz * dz > GameConstants.MaxNpcInteractionRangeSq)
            return false;

        var npcData = gameDataService.GetNpc(npc.NpcId, !npc.UsesNpcSpawnStyle);
        return npcData != null && npcData.NpcType == NpcData.TypeChaoticGenerator;
    }

    private static bool HasFreeInventorySlot(UserSession session)
    {
        for (var index = InventoryConstants.InventoryStart;
             index < InventoryConstants.InventoryStart + InventoryConstants.HaveMax;
             index++)
        {
            if (session.Inventory[index].IsEmpty)
                return true;
        }

        return false;
    }

    private static bool IsExchangeBlockedByFlag(ItemFlag flag)
        => flag is ItemFlag.Rented or ItemFlag.CharacterSeal or ItemFlag.Duplicate
            or ItemFlag.Sealed or ItemFlag.Bound;

    private static ItemUpgradePacketWriter.BifrostRarity RarityEffect(ItemData reward) => reward.ItemType switch
    {
        ItemData.TypeKrowaz => ItemUpgradePacketWriter.BifrostRarity.White,
        ItemData.TypeStandard => ItemUpgradePacketWriter.BifrostRarity.Green,
        _ => ItemUpgradePacketWriter.BifrostRarity.Red,
    };

    private int RollReward(int pieceItemId)
    {
        var candidates = RewardsFor(pieceItemId);
        if (candidates.Count == 0)
            return 0;

        var total = 0;
        foreach (var row in candidates)
        {
            var weight = row.ExchangeCount1 / RewardWeightDivisor;
            if (weight > 0)
                total += weight;
            if (total >= RewardSlotMax)
            {
                total = RewardSlotMax;
                break;
            }
        }

        if (total <= 0)
            return 0;

        var pick = Random.Shared.Next(total);
        var offset = 0;
        foreach (var row in candidates)
        {
            var weight = row.ExchangeCount1 / RewardWeightDivisor;
            if (weight <= 0)
                continue;
            offset += weight;
            if (pick < offset)
                return row.ExchangeItem1;
        }

        return candidates[^1].ExchangeItem1;
    }

    private List<ItemExchangeData> RewardsFor(int pieceItemId)
    {
        var index = rewardsByPiece;
        if (index == null)
        {
            lock (rewardLock)
            {
                index = rewardsByPiece ??= BuildRewardIndex();
            }
        }

        return index.TryGetValue(pieceItemId, out var rows) ? rows : [];
    }

    private Dictionary<int, List<ItemExchangeData>> BuildRewardIndex()
    {
        var index = new Dictionary<int, List<ItemExchangeData>>();
        foreach (var row in gameDataService.ItemExchangeTable.Values)
        {
            if (row.RandomFlag is < RandomFlagFirst or > RandomFlagLast)
                continue;
            if (row.OriginItem1 == 0 || row.ExchangeItem1 == 0)
                continue;
            if (!index.TryGetValue(row.OriginItem1, out var rows))
                index[row.OriginItem1] = rows = [];
            rows.Add(row);
        }

        foreach (var rows in index.Values)
            rows.Sort((left, right) => left.Index.CompareTo(right.Index));

        logger.LogInformation("Chaotic generator reward index built: {Pieces} pieces, {Rows} rows",
            index.Count, index.Values.Sum(rows => rows.Count));
        return index;
    }

    private static async Task SendFailureAsync(UserSession session, ItemUpgradePacketWriter.BifrostResult resultCode)
    {
        var result = ItemUpgradePacketWriter.BifrostExchangeResult(resultCode);
        await session.Client.SendPacket(result);
    }
}
