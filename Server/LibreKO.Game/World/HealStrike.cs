using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public static class HealStrike
{
    public const int ArmingSkill = 492030;
    public const byte ItemOpOnEquip = 3;
    private const int StrikePercent = 50;
    private const int PercentScale = 100;

    public static bool Arms(IEnumerable<ItemOpData>? operations) =>
        operations?.Any(operation => operation.TriggerType == ItemOpOnEquip && operation.SkillId == ArmingSkill) == true;

    public static bool Armed(IReadOnlyList<ItemSlot> inventory, Func<int, IEnumerable<ItemOpData>?> operations, long nowUnixSeconds)
    {
        for (var slot = 0; slot < InventoryConstants.SlotMax && slot < inventory.Count; slot++)
        {
            var itemId = inventory[slot].ItemId;
            if (itemId != 0 && !inventory[slot].HasExpired(nowUnixSeconds) && Arms(operations(itemId)))
                return true;
        }

        return false;
    }

    public static bool Turns(bool armed, SkillMoral moral, MagicDirectType directType) =>
        armed && moral == SkillMoral.FriendWithMe && directType == MagicDirectType.Health;

    public static int Damage(int healAmount) => Math.Max(0, healAmount) * StrikePercent / PercentScale;
}
