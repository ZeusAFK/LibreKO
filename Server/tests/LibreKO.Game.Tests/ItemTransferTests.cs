using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Game.World;

namespace LibreKO.Game.Tests;

public class ItemTransferTests
{
    private const int Potion = 389010000;
    private const int Familiar = 610001000;
    private const long Expiry = 1_900_000_000;
    private const int PetSerial = 4321;

    private static readonly ItemData Countable = new() { Num = Potion, Countable = 1 };
    private static readonly ItemData Single = new() { Num = Familiar, Countable = 0 };

    private static ItemSlot Stack(int count, ItemFlag flag = ItemFlag.Unsealed, long expiresAt = 0) =>
        new() { ItemId = Potion, Count = (ushort)count, Durability = 1, Flag = (byte)flag, ExpiresAt = expiresAt };

    [Fact]
    public void AStackOfAnotherBindingIsNotJoined()
    {
        var bound = Stack(10, ItemFlag.Bound);
        var loose = Stack(5);

        ItemTransfer.Fits(loose, bound, Countable, 5).Should().BeFalse();
        ItemTransfer.Fits(bound, loose, Countable, 10).Should().BeFalse();
    }

    [Fact]
    public void AStackWithAnotherExpiryIsNotJoined()
    {
        var timed = Stack(3, ItemFlag.Rented, Expiry);
        var permanent = Stack(3);

        ItemTransfer.Fits(timed, permanent, Countable, 3).Should().BeFalse();
    }

    [Fact]
    public void JoiningAMatchingStackAddsTheCountAndKeepsItsState()
    {
        var destination = Stack(10, ItemFlag.Bound);
        var source = Stack(5, ItemFlag.Bound);

        ItemTransfer.Fits(source, destination, Countable, 5).Should().BeTrue();
        ItemTransfer.Move(source, destination, 5);

        destination.Count.Should().Be(15);
        destination.State.Should().Be(ItemFlag.Bound);
        source.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void AnEmptyCellTakesTheFlagExpiryAndLink()
    {
        var destination = new ItemSlot();
        var source = new ItemSlot { ItemId = Familiar, Count = 1, Durability = 1, Flag = (byte)ItemFlag.Rented, ExpiresAt = Expiry, UniqueId = PetSerial };

        ItemTransfer.Fits(source, destination, Single, 1).Should().BeTrue();
        ItemTransfer.Move(source, destination, 1);

        destination.ItemId.Should().Be(Familiar);
        destination.State.Should().Be(ItemFlag.Rented);
        destination.ExpiresAt.Should().Be(Expiry);
        destination.UniqueId.Should().Be(PetSerial);
        source.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void AWholeMoveCarriesTheExpiryAndLink()
    {
        var destination = new ItemSlot();
        var source = new ItemSlot { ItemId = Familiar, Count = 1, Durability = 1, ExpiresAt = Expiry, UniqueId = PetSerial };

        ItemTransfer.Move(source, destination, source.Count);

        destination.ExpiresAt.Should().Be(Expiry);
        destination.UniqueId.Should().Be(PetSerial);
        source.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ALinkedItemNeverJoinsAStack()
    {
        var destination = Stack(1);
        var source = Stack(1);
        source.UniqueId = PetSerial;

        ItemTransfer.Fits(source, destination, Countable, 1).Should().BeFalse();
    }
}
