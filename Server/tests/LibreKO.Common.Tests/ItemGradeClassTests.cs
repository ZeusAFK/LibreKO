using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Common.Tests;

public class ItemGradeClassTests
{
    private const int PlusSixItem = 262005006;
    private const int PlusSevenItem = 262005007;
    private const int UpgradeExtension = 17;
    private const int PlusSevenExtensionId = 7;
    private const int GradeStep = 1;
    private const short LowItemClass = 1;
    private const short MiddleItemClass = 2;

    private static readonly IReadOnlyDictionary<int, int> GradeBonuses = new Dictionary<int, int>
    {
        [ItemGradeBonusData.KeyOf(UpgradeExtension, PlusSevenExtensionId)] = GradeStep,
    };

    [Theory]
    [InlineData(PlusSixItem, LowItemClass)]
    [InlineData(PlusSevenItem, MiddleItemClass)]
    public void TheGradeClassAddsTheBonusOfTheItemsExtension(int itemId, short gradeClass)
    {
        var item = new ItemData { Num = itemId, ItemClass = LowItemClass, Extension = UpgradeExtension };

        item.GradeClass(GradeBonuses).Should().Be(gradeClass);
    }
}
