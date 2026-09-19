using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Game.World;

namespace LibreKO.Game.Tests;

public class InventoryPersistenceTests
{
    private const int SealedBoots = 700009000;
    private const int PlainSword = 700001000;

    [Fact]
    public void ASealSurvivesBeingSavedAndLoaded()
    {
        var saved = NewSlots(4);
        saved[0].ItemId = SealedBoots;
        saved[0].Count = 1;
        saved[0].Durability = 300;
        saved[0].Flag = (byte)ItemFlag.Sealed;
        saved[1].ItemId = PlainSword;
        saved[1].Count = 1;

        var loaded = NewSlots(4);
        UserSessionBinaryState.LoadSlots(loaded, UserSessionBinaryState.SerializeSlots(saved));

        loaded[0].ItemId.Should().Be(SealedBoots);
        loaded[0].Durability.Should().Be(300);
        loaded[0].State.Should().Be(ItemFlag.Sealed);
        loaded[0].IsTradable.Should().BeFalse();
        loaded[1].State.Should().Be(ItemFlag.Unsealed);
        loaded[1].IsTradable.Should().BeTrue();
    }

    [Fact]
    public void ASaveFromBeforeFlagsWereKeptStillLoads()
    {
        var slots = NewSlots(3);
        var legacy = new byte[slots.Length * UserSessionBinaryState.BytesPerItem];
        BitConverter.TryWriteBytes(legacy.AsSpan(0), PlainSword);
        BitConverter.TryWriteBytes(legacy.AsSpan(4), (short)150);
        BitConverter.TryWriteBytes(legacy.AsSpan(6), (ushort)2);

        UserSessionBinaryState.LoadSlots(slots, legacy);

        slots[0].ItemId.Should().Be(PlainSword);
        slots[0].Durability.Should().Be(150);
        slots[0].Count.Should().Be(2);
        slots[0].State.Should().Be(ItemFlag.Unsealed);
    }

    [Fact]
    public void TheCharacterRecordCarriesTheSealToTheClient()
    {
        var slots = NewSlots(InventoryConstants.InventoryTotal);
        slots[0].ItemId = SealedBoots;
        slots[0].Count = 1;
        slots[0].Flag = (byte)ItemFlag.Sealed;

        var saved = UserSessionBinaryState.SerializeSlots(slots);
        var flagOffset = InventoryConstants.InventoryTotal * UserSessionBinaryState.BytesPerItem;

        saved[flagOffset].Should().Be((byte)ItemFlag.Sealed,
            "the flag block is what the character record reads the seal back out of");

        var reloaded = NewSlots(InventoryConstants.InventoryTotal);
        UserSessionBinaryState.LoadSlots(reloaded, saved);
        reloaded[0].State.Should().Be(ItemFlag.Sealed);
    }

    private static ItemSlot[] NewSlots(int count)
    {
        var slots = new ItemSlot[count];
        for (var i = 0; i < count; i++)
            slots[i] = new ItemSlot();
        return slots;
    }
}
