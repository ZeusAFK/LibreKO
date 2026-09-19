using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using Xunit;

namespace LibreKO.Game.Tests;

public class CospreWireOrderTests
{
    private const int UndefinedCospreWirePosition = 6;

    [Fact]
    public void VisualSlotsCospreTailFollowsTheCospreWireOrder()
    {
        var cospreTail = VisualCospreTail();
        var expected = new int[CospreWireOrderLength];
        for (var index = 0; index < expected.Length; index++)
            expected[index] = InventoryConstants.CospreStart + InventoryConstants.CospreWirePositions[index];

        cospreTail.Should().Equal(expected);
    }

    [Fact]
    public void CospreWirePositionsSkipOnlyTheUndefinedSlot()
    {
        InventoryConstants.CospreWirePositions.Should()
            .BeInAscendingOrder()
            .And.NotContain(UndefinedCospreWirePosition);
    }

    [Fact]
    public void EmblemIsTransmittedBeforeFairy()
    {
        var positions = InventoryConstants.CospreWirePositions;
        var emblem = System.Array.IndexOf(positions, InventoryConstants.CosPosEmblem);
        var fairy = System.Array.IndexOf(positions, InventoryConstants.CosPosFairy);

        emblem.Should().BeLessThan(fairy);
    }

    private static int CospreWireOrderLength => InventoryConstants.CospreWirePositions.Length;

    private static int[] VisualCospreTail()
    {
        var slots = InventoryConstants.VisualSlots;
        var tail = new int[CospreWireOrderLength];
        System.Array.Copy(slots, slots.Length - tail.Length, tail, 0, tail.Length);
        return tail;
    }
}
