using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public interface IItemGrantService
{
    Task<int> GrantAsync(UserSession session, ItemData item, int count);
}

public class ItemGrantService(
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService) : IItemGrantService
{
    public async Task<int> GrantAsync(UserSession session, ItemData item, int count)
    {
        if (count <= 0)
            return 0;

        if (item.Countable == 0)
        {
            var placed = 0;
            for (var i = 0; i < count; i++)
            {
                var slotOutcome = session.WithLock(s =>
                {
                    var slotIndex = s.FindSlotForItem(item.Num, gameDataService, 1);
                    if (slotIndex < 0)
                        return (Success: false, SlotIndex: 0, ItemId: 0, Count: (ushort)0, Durability: (short)0);

                    var slot = s.Inventory[slotIndex];
                    slot.ItemId = item.Num;
                    slot.Durability = item.Duration;
                    slot.Count = 1;

                    s.RecalculateStatsWithBuffs(gameDataService);
                    return (Success: true, SlotIndex: slotIndex, ItemId: slot.ItemId, Count: slot.Count, Durability: slot.Durability);
                });

                if (!slotOutcome.Success)
                    break;

                placed++;
                await userNotificationService.SendStackChangeAsync(
                    session, (byte)slotOutcome.SlotIndex, slotOutcome.ItemId, slotOutcome.Count, slotOutcome.Durability, true);
            }

            if (placed > 0)
                await userNotificationService.SendWeightChangeAsync(session);

            return placed;
        }

        var outcome = session.WithLock(s =>
        {
            var slotIndex = s.FindSlotForItem(item.Num, gameDataService, (ushort)Math.Min(count, (int)InventoryConstants.MaxStackCount));
            if (slotIndex < 0)
                return (Placed: false, SlotIndex: 0, ItemId: 0, Count: (ushort)0, Durability: (short)0, IsNew: false, Added: 0);

            var slot = s.Inventory[slotIndex];
            var isNew = slot.IsEmpty;
            if (isNew)
            {
                slot.ItemId = item.Num;
                slot.Durability = item.Duration;
                slot.Count = 0;
            }

            var previousCount = slot.Count;
            slot.Count = (ushort)Math.Min((int)InventoryConstants.MaxStackCount, slot.Count + count);
            var added = slot.Count - previousCount;

            s.RecalculateStatsWithBuffs(gameDataService);
            return (Placed: true, SlotIndex: slotIndex, ItemId: slot.ItemId, Count: slot.Count, Durability: slot.Durability, IsNew: isNew, Added: added);
        });

        if (!outcome.Placed || outcome.Added == 0)
            return 0;

        await userNotificationService.SendStackChangeAsync(
            session, (byte)outcome.SlotIndex, outcome.ItemId, outcome.Count, outcome.Durability, outcome.IsNew);
        await userNotificationService.SendWeightChangeAsync(session);
        return outcome.Added;
    }
}
