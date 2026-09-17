using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IAttendancePacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class AttendancePacketCoordinator(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IUserNotificationService userNotification,
    ILogger<AttendancePacketCoordinator> logger) : IAttendancePacketCoordinator
{

    private const uint BoardChannel = 0;

    private const uint ResultOk = 1;
    private const uint ResultFailed = 0;

    private const byte StateClaimed = 1;
    private const byte StateExpired = 2;
    private const byte StateClaimable = 3;
    private const byte StateLocked = 5;

    private const byte ClaimKindNormal = 1;
    private const byte ClaimKindExtra = 2;

    private const int ClaimRequestBytes = 4 + 4 + 1;

    private static readonly int DailySlots =
        AttendanceRewardData.DailySlotLast - AttendanceRewardData.DailySlotFirst + 1;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = (EventBoardSubOpcode)packet.ReadByte();
        switch (sub)
        {
            case EventBoardSubOpcode.AttendanceBoard:
                await HandleBoardRequestAsync(session);
                break;
            case EventBoardSubOpcode.AttendanceClaim:
                await HandleClaimAsync(session, packet);
                break;
            default:
                logger.LogDebug("Event board sub-opcode {Sub} is not implemented", sub);
                break;
        }
    }

    private async Task HandleBoardRequestAsync(UserSession session)
    {
        if (gameDataService.AttendanceRewardTable.Count == 0)
        {
            await session.Client.SendPacket(BuildFailure(EventBoardSubOpcode.AttendanceBoard, ResultFailed));
            return;
        }

        CheckIn(session);
        await session.Client.SendPacket(BuildBoard(session, EventBoardSubOpcode.AttendanceBoard));
    }

    private async Task HandleClaimAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < ClaimRequestBytes)
        {
            await session.Client.SendPacket(BuildFailure(EventBoardSubOpcode.AttendanceClaim, ResultFailed));
            return;
        }

        packet.ReadUInt();
        var slot = packet.ReadInt();
        var claimKind = packet.ReadByte();

        if (claimKind is not (ClaimKindNormal or ClaimKindExtra))
        {
            await session.Client.SendPacket(BuildFailure(EventBoardSubOpcode.AttendanceClaim, ResultFailed));
            return;
        }

        CheckIn(session);

        if (!gameDataService.AttendanceRewardTable.TryGetValue(slot, out var reward)
            || SlotState(session, slot) != StateClaimable)
        {
            await session.Client.SendPacket(BuildFailure(EventBoardSubOpcode.AttendanceClaim, ResultFailed));
            return;
        }

        var itemData = gameDataService.GetItem(reward.ItemId);
        if (itemData == null)
        {
            logger.LogWarning("Attendance slot {Slot} rewards unknown item {ItemId}", slot, reward.ItemId);
            await session.Client.SendPacket(BuildFailure(EventBoardSubOpcode.AttendanceClaim, ResultFailed));
            return;
        }

        var count = (ushort)(reward.ItemCount < 1 ? 1 : reward.ItemCount);
        if (session.Stats.ItemWeight + (long)itemData.Weight * count > session.Stats.MaxWeight)
        {
            await session.Client.SendPacket(BuildFailure(EventBoardSubOpcode.AttendanceClaim, ResultFailed));
            return;
        }

        var slotIndex = session.FindSlotForItem(reward.ItemId, gameDataService, count);
        if (slotIndex < 0)
        {
            await session.Client.SendPacket(BuildFailure(EventBoardSubOpcode.AttendanceClaim, ResultFailed));
            return;
        }

        var inventorySlot = session.Inventory[slotIndex];
        var isNewItem = inventorySlot.IsEmpty;
        inventorySlot.ItemId = reward.ItemId;
        inventorySlot.Count += count;
        if (isNewItem)
            inventorySlot.Durability = itemData.Duration;

        MarkClaimed(session, slot);

        logger.LogInformation("{Name} claimed attendance slot {Slot}: item {ItemId} x{Count}",
            session.Name, slot, reward.ItemId, count);

        await session.Client.SendPacket(BuildBoard(session, EventBoardSubOpcode.AttendanceClaim));
        await userNotification.SendStackChangeAsync(
            session, (byte)slotIndex, reward.ItemId, inventorySlot.Count, inventorySlot.Durability, isNewItem);
        session.RecalculateStatsWithBuffs(gameDataService);
        await userNotification.SendWeightChangeAsync(session);
    }

    private static void CheckIn(UserSession session)
    {
        var today = DateTime.UtcNow.Date;
        if (session.AttendanceCheckedOn?.Date == today)
            return;

        session.AttendanceCheckedOn = today;
        if (session.AttendanceDays < AttendanceRewardData.DailySlotLast)
            session.AttendanceDays++;
    }

    private static byte SlotState(UserSession session, int slot)
    {
        if (AttendanceRewardData.IsDailySlot(slot))
        {
            if ((session.AttendanceClaimedDays & (1 << (slot - AttendanceRewardData.DailySlotFirst))) != 0)
                return StateClaimed;
            if (slot > session.AttendanceDays)
                return StateLocked;
            return slot == session.AttendanceDays ? StateClaimable : StateExpired;
        }

        if (!AttendanceRewardData.IsBonusSlot(slot))
            return StateLocked;

        if ((session.AttendanceClaimedBonus & (1 << (slot - AttendanceRewardData.BonusSlotFirst))) != 0)
            return StateClaimed;
        return session.AttendanceDays >= AttendanceRewardData.BonusThreshold(slot)
            ? StateClaimable
            : StateLocked;
    }

    private static void MarkClaimed(UserSession session, int slot)
    {
        if (AttendanceRewardData.IsDailySlot(slot))
            session.AttendanceClaimedDays |= 1 << (slot - AttendanceRewardData.DailySlotFirst);
        else if (AttendanceRewardData.IsBonusSlot(slot))
            session.AttendanceClaimedBonus |=
                (byte)(1 << (slot - AttendanceRewardData.BonusSlotFirst));
    }

    private static Packet BuildFailure(EventBoardSubOpcode sub, uint result) =>
        AttendancePacketWriter.Result(sub, BoardChannel, result);

    private static Packet BuildBoard(UserSession session, EventBoardSubOpcode sub)
    {
        var daily = new List<AttendancePacketWriter.SlotState>(DailySlots);
        for (var slot = AttendanceRewardData.DailySlotFirst;
             slot <= AttendanceRewardData.DailySlotLast;
             slot++)
        {
            daily.Add(new AttendancePacketWriter.SlotState(slot, SlotState(session, slot)));
        }

        var bonus = new List<AttendancePacketWriter.SlotState>(AttendanceRewardData.BonusSlotCount);
        for (var index = 0; index < AttendanceRewardData.BonusSlotCount; index++)
        {
            var slot = AttendanceRewardData.BonusSlotFirst + index;
            bonus.Add(new AttendancePacketWriter.SlotState(slot, SlotState(session, slot)));
        }

        return AttendancePacketWriter.Board(sub, BoardChannel, ResultOk, daily, bonus);
    }
}
