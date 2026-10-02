using System.Collections.Generic;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class CostumeLookTests
{
    private const int Pauldron = 508051000;
    private const int Helmet = 508053000;
    private const int PauldronResource = 50800101;
    private const int HelmetResource = 50800301;

    private static int ResourceOf(int itemId) => itemId switch
    {
        Pauldron => PauldronResource,
        Helmet => HelmetResource,
        _ => 0,
    };

    private static int[] Gear(int pauldron, int helmet)
    {
        var gear = new int[InventoryConstants.VisualSlotCount];
        gear[InventoryConstants.VisCosPauldron] = pauldron;
        gear[InventoryConstants.VisCosHelmet] = helmet;
        return gear;
    }

    [Fact]
    public void TheStemCarriesTheRaceInTheModelNumber() =>
        Assert.Equal("5_0812_10_1", CostumeLook.ArmorStem(PauldronResource, 12));

    [Fact]
    public void APartIsNamedAfterTheTensAndHundredsOfThousandsOfItsItemId()
    {
        Assert.Equal(47, CostumeLook.PartPrefix(508471000, ItemData.SaleTypeLow));
        Assert.Equal(5, CostumeLook.PartPrefix(508051003, ItemData.SaleTypeLow));
        Assert.Equal(0, CostumeLook.PartPrefix(518001000, ItemData.SaleTypeLow));
    }

    [Fact]
    public void ALowNoRepairItemKeepsThePlainName() =>
        Assert.Equal(0, CostumeLook.PartPrefix(508471000, ItemData.SaleTypeLowNoRepair));

    [Fact]
    public void ThePrefixedStemIsTriedBeforeThePlainOne() =>
        Assert.Equal(new[] { "47_5_0848_10_0", "5_0848_10_0" }, CostumeLook.PartStems(47, 50847100, 1));

    [Fact]
    public void AnUnprefixedPartHasOneStem() =>
        Assert.Equal(new[] { "5_1801_10_0" }, CostumeLook.PartStems(0, 51800100, 1));

    [Fact]
    public void TheOutfitSwapsOnlyThePieceType() =>
        Assert.Equal(50800401, CostumeLook.WithType(PauldronResource, CostumeLook.HandsType));

    [Fact]
    public void APauldronDressesEveryPieceThatExistsAndAHelmetTakesTheHead()
    {
        var grafted = new List<(int Part, int Resource)>();
        var missing = CostumeLook.WithType(PauldronResource, CostumeLook.FeetType);
        var claimed = CostumeLook.Dress(Gear(Pauldron, Helmet), helmetHidden: false, ResourceOf, (part, resource, _) =>
        {
            if (resource == missing) return false;
            grafted.Add((part, resource));
            return true;
        });

        Assert.Equal(new HashSet<int> { CostumeLook.UpperPart, CostumeLook.LowerPart, CostumeLook.HandsPart, CostumeLook.HeadPart }, claimed);
        Assert.Contains((CostumeLook.HeadPart, HelmetResource), grafted);
    }

    [Fact]
    public void AnOutfitWhoseUpperPieceIsMissingClaimsNothing()
    {
        var claimed = CostumeLook.Dress(Gear(Pauldron, 0), helmetHidden: false, ResourceOf, (_, _, _) => false);
        Assert.Empty(claimed);
    }

    [Fact]
    public void AHiddenHelmetLeavesTheHeadToTheHair()
    {
        var claimed = CostumeLook.Dress(Gear(0, Helmet), helmetHidden: true, ResourceOf, (_, _, _) => true);
        Assert.DoesNotContain(CostumeLook.HeadPart, claimed);
    }
}
