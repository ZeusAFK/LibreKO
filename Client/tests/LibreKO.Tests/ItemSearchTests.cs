using System.Linq;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class ItemSearchTests
{
    private const int StaffSet = 10;
    private const int WoodStaff = 180110000;
    private const int OakStaff = 180120000;

    private static ItemData.Ext Variant(int id, int linked = 0) => new() { Cat = StaffSet, Id = id, Linked = linked };

    [Fact]
    public void AnItemOffersItsOwnVariantsAndTheGenericOnes()
    {
        var staff = new ItemData.Item { Id = WoodStaff, Cat = StaffSet };
        var exts = new[] { Variant(1), Variant(10), Variant(405, WoodStaff), Variant(406, OakStaff), Variant(1001) };

        var offered = ItemSearch.VariantsOf(staff, exts).Select(ext => ItemData.VariantId(staff, ext));

        Assert.Equal(new[] { WoodStaff + 1, WoodStaff + 10, WoodStaff + 405 }, offered);
    }
}
