using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Game.World;

public sealed class PetState(Pet record, int itemId)
{
    public const byte MaxLevel = 60;
    public const int NoTarget = -1;

    public Pet Record { get; } = record;
    public int ItemId { get; } = itemId;
    public ItemSlot[] Items { get; } = PetInventory.Load(record.Items);
    public NpcInstance? Npc { get; set; }
    public PetMode Mode { get; set; } = PetMode.Defence;
    public int TargetNpcId { get; set; } = NoTarget;
    public long LastSatisfactionTicks { get; set; } = DateTime.UtcNow.Ticks;
    public long LastAttackTicks { get; set; }
    public long LastRegenTicks { get; set; } = DateTime.UtcNow.Ticks;
    public Dictionary<int, long> SkillReadyTicks { get; } = [];

    public bool IsSummoned => Npc is { IsAlive: true };

    public void SaveItems() => Record.Items = PetInventory.Save(Items);
}

public enum PetMode : byte
{
    Summoned = 1,
    Died = 2,
    Attack = 3,
    Defence = 4,
    Looting = 8,
    Chat = 9,
}

public static class PetInventory
{
    public static ItemSlot[] Load(byte[]? data)
    {
        var slots = new ItemSlot[Pet.InventorySize];
        for (var i = 0; i < slots.Length; i++)
            slots[i] = new ItemSlot();
        UserSessionBinaryState.LoadSlots(slots, data);
        return slots;
    }

    public static byte[] Save(ItemSlot[] slots) => UserSessionBinaryState.SerializeSlots(slots);
}
