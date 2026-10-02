using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class RankerStatueTests
{
    private const int Blade = 110010000;
    private const int Shield = 410010000;
    private const int PlateArmor = 206001000;

    [Theory]
    [InlineData(81, false)]
    [InlineData(82, true)]
    [InlineData(84, true)]
    [InlineData(87, true)]
    [InlineData(88, false)]
    public void OnlyTheSixRankerTypesAreStatues(int npcType, bool statue) =>
        Assert.Equal(statue, RankerStatue.Is(npcType));

    [Fact]
    public void EmptyArmourSlotsWearTheClassStarterSet()
    {
        var gear = RankerStatue.Dress(106, 0, PlateArmor, 0, 0, 0, Blade, Shield);

        Assert.Equal(507003000, gear[InventoryConstants.VisHead]);
        Assert.Equal(PlateArmor, gear[InventoryConstants.VisBreast]);
        Assert.Equal(507002000, gear[InventoryConstants.VisLeg]);
        Assert.Equal(507004000, gear[InventoryConstants.VisGlove]);
        Assert.Equal(507005000, gear[InventoryConstants.VisFoot]);
        Assert.Equal(Blade, gear[InventoryConstants.VisRightHand]);
        Assert.Equal(Shield, gear[InventoryConstants.VisLeftHand]);
    }

    [Theory]
    [InlineData(207, 557001000)]
    [InlineData(110, 567001000)]
    [InlineData(4, 597001000)]
    public void EachClassFamilyHasItsOwnStarterSet(int cls, int upper) =>
        Assert.Equal(upper, RankerStatue.Dress(cls, 0, 0, 0, 0, 0, 0, 0)[InventoryConstants.VisBreast]);

    [Fact]
    public void AKurianStatueKeepsItsBareBodyButHoldsItsWeapons()
    {
        var gear = RankerStatue.Dress(214, 1, PlateArmor, 3, 4, 5, Blade, 0);

        Assert.Equal(0, gear[InventoryConstants.VisHead]);
        Assert.Equal(0, gear[InventoryConstants.VisBreast]);
        Assert.Equal(0, gear[InventoryConstants.VisFoot]);
        Assert.Equal(Blade, gear[InventoryConstants.VisRightHand]);
    }
}
