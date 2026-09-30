using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public interface IGenieHammerService
{
    Task<bool> UseAsync(UserSession session, int threshold);
}

public class GenieHammerService(IGameDataService data, IUserNotificationService notifications) : IGenieHammerService
{
    private static readonly int[] GenieHammerItems = [810227000, 810935000, 900819000];
    public const int MinRepairThreshold = 1;
    public const int MaxRepairThreshold = 50;

    public static bool IsHammer(int id) => Array.IndexOf(GenieHammerItems, id) >= 0;

    public async Task<bool> UseAsync(UserSession session, int threshold)
    {
        if (threshold is < MinRepairThreshold or > MaxRepairThreshold) return false;
        var changes = new List<(byte Slot, short Durability)>();
        int hammerSlot = -1, remainingId = 0;
        ushort count = 0;
        short charges = 0;
        bool used = session.WithLock(s =>
        {
            if (!s.GenieActive || s.GenieMinutes == 0 || s.Hp <= 0
                || s.Trade.IsTrading || s.Trade.IsMerchanting) return false;
            bool needsRepair = false;
            for (int i = 0; i < InventoryConstants.SlotMax; i++)
            {
                var slot = s.Inventory[i];
                var def = data.GetItem(slot.ItemId);
                if (slot.IsEmpty || def == null || def.Duration <= 1) continue;
                if (slot.Durability * 100L <= def.Duration * threshold) needsRepair = true;
                if (slot.Durability < def.Duration) changes.Add(((byte)i, def.Duration));
            }
            if (!needsRepair || changes.Count == 0) return false;
            for (int i = InventoryConstants.InventoryStart; i < s.Inventory.Length; i++)
            {
                // Exclude cosmetic and bag-equipment slots: only carried inventory is usable.
                if (i >= InventoryConstants.CospreStart && i < InventoryConstants.MagicBagStart) continue;
                var slot = s.Inventory[i];
                if (!IsHammer(slot.ItemId) || slot.Durability <= 0
                    || slot.HasExpired(DateTimeOffset.UtcNow.ToUnixTimeSeconds())) continue;
                var def = data.GetItem(slot.ItemId);
                if (def == null || s.Level < def.ReqLevel || (def.ReqLevelMax > 0 && s.Level > def.ReqLevelMax)) continue;
                hammerSlot = i;
                slot.Durability--;
                if (slot.Durability == 0) slot.Clear();
                remainingId = slot.ItemId; count = slot.Count; charges = slot.Durability;
                break;
            }
            if (hammerSlot < 0) return false;
            foreach (var change in changes) s.Inventory[change.Slot].Durability = change.Durability;
            s.RecalculateStatsWithBuffs(data);
            return true;
        });
        if (!used) return false;
        await notifications.SendStackChangeAsync(session, (byte)hammerSlot, remainingId, count, charges);
        foreach (var change in changes)
            await session.Client.SendPacket(MiningPacketWriter.Durability(change.Slot, change.Durability));
        await notifications.SendStatUpdateAsync(session);
        await notifications.SendWeightChangeAsync(session);
        return true;
    }
}
