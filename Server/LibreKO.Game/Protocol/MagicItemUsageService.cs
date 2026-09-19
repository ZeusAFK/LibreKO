using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public enum ItemUsability
{
    Usable,
    NoSuchItem,
    WrongClass,
    LevelTooLow,
    LevelTooHigh,
    NotCarryingEnough,
}

public interface IMagicItemUsageService
{
    bool CanUseItem(UserSession session, int itemId, int count = 1);
    bool CanUseSkillItems(UserSession session, MagicData magic, int count = 1);
    ItemUsability CheckItem(UserSession session, int itemId, int count = 1);
    bool HasArrows(UserSession session, int count = 1);
    Task<bool> TryConsumeItemAsync(UserSession session, int itemId, int count = 1);
    Task<bool> TryConsumeSkillItemAsync(UserSession session, MagicData magic, int count = 1);
    Task<bool> TryConsumeArrowAsync(UserSession session, int count = 1);
}

public class MagicItemUsageService(
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService) : IMagicItemUsageService
{
    private static readonly HashSet<int> SpellRequirementItems =
    [
        370001000, 370002000, 370003000,
        379069000, 379070000,
        379063000, 379064000, 379065000, 379066000,
    ];

    private const int ArrowKind = 120;

    public bool CanUseItem(UserSession session, int itemId, int count = 1) =>
        CheckItem(session, itemId, count) == ItemUsability.Usable;

    public bool CanUseSkillItems(UserSession session, MagicData magic, int count = 1) =>
        magic.UseItem == 0
        || (magic.ConsumedItem == magic.UseItem
            ? CanUseItem(session, magic.UseItem, count)
            : CanUseItem(session, magic.UseItem) && CanUseItem(session, magic.ConsumedItem, count));

    public ItemUsability CheckItem(UserSession session, int itemId, int count = 1)
    {
        if (count <= 0)
            return ItemUsability.Usable;

        var itemData = gameDataService.GetItem(itemId);
        if (itemData == null)
            return ItemUsability.NoSuchItem;

        if (itemData.Class != 0 && !ClassIdHelper.MatchesJobGroup(session.Class, itemData.Class))
            return ItemUsability.WrongClass;

        if (session.Level < itemData.ReqLevel)
            return ItemUsability.LevelTooLow;

        if (itemData.ReqLevelMax > 0 && session.Level > itemData.ReqLevelMax)
            return ItemUsability.LevelTooHigh;

        return CountItem(session, itemId, itemData.Category == ItemKind.PowerUpStore) >= count
            ? ItemUsability.Usable
            : ItemUsability.NotCarryingEnough;
    }

    public Task<bool> TryConsumeSkillItemAsync(UserSession session, MagicData magic, int count = 1) =>
        magic.UseItem == 0 ? Task.FromResult(true)
        : !CanUseSkillItems(session, magic, count) ? Task.FromResult(false)
        : TryConsumeItemAsync(session, magic.ConsumedItem, count);

    public async Task<bool> TryConsumeItemAsync(UserSession session, int itemId, int count = 1)
    {
        if (!CanUseItem(session, itemId, count))
            return false;

        if (SpellRequirementItems.Contains(itemId))
            return true;

        var itemData = gameDataService.GetItem(itemId);
        if (itemData == null)
            return false;

        var spendsDurability = itemData.Category == ItemKind.PowerUpStore;
        var remaining = count;
        for (var index = InventoryConstants.InventoryStart; index < session.Inventory.Length && remaining > 0; index++)
        {
            var slot = session.Inventory[index];
            if (slot.ItemId != itemId)
                continue;

            if (spendsDurability && slot.Durability > 0)
            {
                var taken = Math.Min(slot.Durability, remaining);
                slot.Durability -= (short)taken;
                slot.Count = (ushort)Math.Max(0, (int)slot.Durability);
                remaining -= taken;

                if (slot.Durability <= 0)
                    slot.Clear();
            }
            else
            {
                var taken = Math.Min((int)slot.Count, remaining);
                slot.Count -= (ushort)taken;
                remaining -= taken;

                if (slot.Count == 0)
                    slot.Clear();
            }

            await userNotificationService.SendStackChangeAsync(
                session,
                (byte)index,
                slot.ItemId,
                slot.Count,
                slot.Durability);
        }

        if (remaining > 0)
            return false;

        var coefficient = gameDataService.GetCoefficient(session.Class);
        if (coefficient != null)
            session.RecalculateStats(coefficient, gameDataService);

        await userNotificationService.SendWeightChangeAsync(session);
        return true;
    }

    public bool HasArrows(UserSession session, int count = 1)
    {
        if (count <= 0)
            return true;

        var remaining = count;
        for (var index = InventoryConstants.InventoryStart; index < session.Inventory.Length && remaining > 0; index++)
        {
            var slot = session.Inventory[index];
            if (slot.IsEmpty)
                continue;

            var itemData = gameDataService.GetItem(slot.ItemId);
            if (itemData?.Kind != ArrowKind)
                continue;

            remaining -= slot.Count;
        }

        return remaining <= 0;
    }

    public async Task<bool> TryConsumeArrowAsync(UserSession session, int count = 1)
    {
        if (!HasArrows(session, count))
            return false;

        var remaining = count;
        for (var index = InventoryConstants.InventoryStart; index < session.Inventory.Length && remaining > 0; index++)
        {
            var slot = session.Inventory[index];
            if (slot.IsEmpty)
                continue;

            var itemData = gameDataService.GetItem(slot.ItemId);
            if (itemData?.Kind != ArrowKind)
                continue;

            var taken = Math.Min((int)slot.Count, remaining);
            slot.Count -= (ushort)taken;
            remaining -= taken;

            if (slot.Count == 0)
                slot.Clear();

            await userNotificationService.SendStackChangeAsync(
                session,
                (byte)index,
                slot.ItemId,
                slot.Count,
                slot.Durability);
        }

        if (remaining > 0)
            return false;

        var coefficient = gameDataService.GetCoefficient(session.Class);
        if (coefficient != null)
            session.RecalculateStats(coefficient, gameDataService);

        await userNotificationService.SendWeightChangeAsync(session);
        return true;
    }

    private static int CountItem(UserSession session, int itemId, bool useDurability)
    {
        var total = 0;
        for (var index = InventoryConstants.InventoryStart; index < session.Inventory.Length; index++)
        {
            if (session.Inventory[index].ItemId == itemId)
            {
                total += useDurability
                    ? Math.Max(session.Inventory[index].Count, (ushort)Math.Max(0, (int)session.Inventory[index].Durability))
                    : session.Inventory[index].Count;
            }
        }

        return total;
    }
}
