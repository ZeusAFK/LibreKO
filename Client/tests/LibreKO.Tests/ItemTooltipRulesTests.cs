using System.Collections.Generic;
using System.Linq;
using Godot;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class ItemTooltipRulesTests
{
    private const int PlainItem = 156211000;
    private const int MiddleClass = 2;
    private const int HighClassReverse = 4;
    private const int UniqueHigh = 33;
    private const int ReverseUniqueClass = 35;
    private const int UnknownUniqueClass = 99;
    private const int SwordKind = 22;
    private const int DragonSet = 111;
    private const int QuestRace = 20;

    [Fact]
    public void UpgradeItemsNameTheirClass() =>
        Assert.Equal(new ItemTooltipRules.GradeLine(4563, ItemTooltipRules.GradeTint.Upgrade),
            ItemTooltipRules.Grade(ItemData.Rarity.Upgrade, MiddleClass, PlainItem, 2));

    [Fact]
    public void HighClassTurnsReverseChangeableOnlyForTheMatchingExtRow()
    {
        Assert.Equal(4582, ItemTooltipRules.Grade(ItemData.Rarity.Upgrade, HighClassReverse, PlainItem, 11)!.Value.TextId);
        Assert.Equal(4564, ItemTooltipRules.Grade(ItemData.Rarity.Upgrade, HighClassReverse, PlainItem, 1011)!.Value.TextId);
        Assert.Equal(4564, ItemTooltipRules.Grade(ItemData.Rarity.Upgrade, HighClassReverse, PlainItem, 8)!.Value.TextId);
    }

    [Fact]
    public void UniqueGradesFallBackToPlainUnique()
    {
        Assert.Equal(30312, ItemTooltipRules.Grade(ItemData.Rarity.Unique, UniqueHigh, PlainItem, 0)!.Value.TextId);
        Assert.Equal(4585, ItemTooltipRules.Grade(ItemData.Rarity.Unique, UnknownUniqueClass, PlainItem, 0)!.Value.TextId);
        Assert.Equal(ItemTooltipRules.GradeTint.ReverseUnique,
            ItemTooltipRules.Grade(ItemData.Rarity.ReverseUnique, ReverseUniqueClass, PlainItem, 0)!.Value.Tint);
    }

    [Fact]
    public void RegularItemsHaveNoGradeLine() =>
        Assert.Null(ItemTooltipRules.Grade(ItemData.Rarity.Regular, MiddleClass, PlainItem, 0));

    [Fact]
    public void GradeTextSplitsAtTheBracket() =>
        Assert.Equal(new[] { "Item Grade : Unique", "(Reverse Changeable Item)" },
            ItemTooltipRules.SplitGradeText("Item Grade : Unique (Reverse Changeable Item)").ToArray());

    [Fact]
    public void BoundItemsShowTheBindNoteAndTheStoneCount()
    {
        var notes = ItemTooltipRules.Notes((byte)ItemFlag.Bound, 0).ToList();
        Assert.Equal(new[] { 19016, 4613 }, notes.Select(n => n.TextId));
        Assert.True(notes[1].WithStones);
    }

    [Fact]
    public void SealedItemsShowTheSealNote() =>
        Assert.Equal(new[] { 15117 }, ItemTooltipRules.Notes((byte)ItemFlag.Sealed, 0).Select(n => n.TextId));

    [Fact]
    public void OneTimeTradeItemsSayWhetherTheTradeIsUsed()
    {
        Assert.Equal(new[] { 15123 }, ItemTooltipRules.Notes(0, ItemData.Race.OneTimeTrade).Select(n => n.TextId));
        Assert.Equal(new[] { 15122 }, ItemTooltipRules.Notes(16, ItemData.Race.OneTimeTrade).Select(n => n.TextId));
    }

    [Theory]
    [InlineData(900_100_000, 0, 4580)]
    [InlineData(900_100_000, 70, 45020)]
    [InlineData(PlainItem, QuestRace, 4581)]
    [InlineData(PlainItem, 69, 4597)]
    [InlineData(PlainItem, ItemData.Race.OneTimeTrade, 4600)]
    [InlineData(PlainItem, 0, 0)]
    public void TradeNotesFollowTheRace(int itemId, int race, int text) =>
        Assert.Equal(text, ItemTooltipRules.TradeNote(itemId, race));

    private static System.Func<int, int?> Worn(Dictionary<int, int> kinds) =>
        slot => kinds.TryGetValue(slot, out int kind) ? kind : null;

    [Fact]
    public void OneHandedComparesWithTheRightHandFirst()
    {
        var worn = Worn(new() { [InventoryConstants.RightHand] = SwordKind, [InventoryConstants.LeftHand] = SwordKind });
        Assert.Equal(InventoryConstants.RightHand, ItemTooltipRules.CompareSlot(0, worn));
    }

    [Fact]
    public void OneHandedSkipsALeftShield()
    {
        var worn = Worn(new() { [InventoryConstants.LeftHand] = ItemData.Kind.Shield });
        Assert.Equal(ItemTooltipRules.NoWornSlot, ItemTooltipRules.CompareSlot(0, worn));
    }

    [Fact]
    public void ShieldsCompareOnlyWithAShield()
    {
        Assert.Equal(InventoryConstants.LeftHand, ItemTooltipRules.CompareSlot(2,
            Worn(new() { [InventoryConstants.LeftHand] = ItemData.Kind.Shield })));
        Assert.Equal(ItemTooltipRules.NoWornSlot, ItemTooltipRules.CompareSlot(2,
            Worn(new() { [InventoryConstants.LeftHand] = SwordKind })));
    }

    [Fact]
    public void RingsFallBackToTheLeftRing() =>
        Assert.Equal(InventoryConstants.LeftRing, ItemTooltipRules.CompareSlot(12,
            Worn(new() { [InventoryConstants.LeftRing] = 93 })));

    [Fact]
    public void NothingWornMeansNoCompare() =>
        Assert.Equal(ItemTooltipRules.NoWornSlot, ItemTooltipRules.CompareSlot(7, Worn(new())));

    [Fact]
    public void FullDragonSetKeyMatchesTheSetTable() =>
        Assert.Equal(1_116_674, ItemTooltipRules.SetKey(DragonSet, new[]
        {
            InventoryConstants.Head, InventoryConstants.Breast, InventoryConstants.Leg,
            InventoryConstants.Glove, InventoryConstants.Foot,
        }));

    [Fact]
    public void SetRacesAreHundredOneToHundredNinetyNine()
    {
        Assert.True(ItemTooltipRules.IsSetRace(DragonSet));
        Assert.False(ItemTooltipRules.IsSetRace(QuestRace));
        Assert.False(ItemTooltipRules.IsSetRace(200));
    }

    private static readonly Vector2 Screen = new(1600, 900);
    private const float Gap = 16f, Margin = 8f;

    [Fact]
    public void TooltipSitsBelowRightOfTheCursor() =>
        Assert.Equal(new Vector2(116, 116), TooltipPlacement.AroundCursor(new Vector2(100, 100), new Vector2(200, 300), Screen, Gap, Margin));

    [Fact]
    public void TooltipFlipsLeftAndUpNearTheEdges() =>
        Assert.Equal(new Vector2(1284, 484), TooltipPlacement.AroundCursor(new Vector2(1500, 800), new Vector2(200, 300), Screen, Gap, Margin));

    [Fact]
    public void ComparePanelGoesLeftWhenTheRightIsFull() =>
        Assert.Equal(new Vector2(1096, 100), TooltipPlacement.BesidePanel(new Vector2(1300, 100), new Vector2(250, 300),
            new Vector2(200, 300), Screen, 4f, Margin));
}
