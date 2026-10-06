using System.Linq;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class ItemCombineTests
{
    private const int BlessedStone = 389227000;
    private const int Dredium = 389235000;
    private const int OldBelt = 345000808;

    private const string Table = """
        {
         "categories": [
          {"key": 1, "name": "Darkness (Shojin)", "visibility": 1},
          {"key": 6, "name": "Chocolatier", "visibility": 0},
          {"key": 23, "name": "Knight Royale", "visibility": 2}
         ],
         "recipes": [
          {"id": 2, "npc": 19073, "category": 1, "name": "Wirinom", "listed": true, "materials": [{"item": 389224000, "count": 10}], "result": 127410284,
           "successFx": 31033, "successMotion": 106, "successText": "Praise", "failureFx": 31034, "failureMotion": 104, "failureText": "Sorry"},
          {"id": 1, "npc": 19073, "category": 1, "name": "Raum", "listed": true, "materials": [{"item": 389227000, "count": 10}, {"item": 389235000, "count": 50}], "result": 128410901,
           "successFx": 31033, "successMotion": 105, "successText": "Praise", "failureFx": 31034, "failureMotion": 104, "failureText": "Sorry"},
          {"id": 30, "npc": 19073, "category": 1, "name": "Hidden", "listed": false, "materials": [], "result": 128410901,
           "successFx": 0, "successMotion": 0, "successText": "", "failureFx": 0, "failureMotion": 0, "failureText": ""}
         ]
        }
        """;

    private static CombineBook Book => ItemCombine.Parse(Table);

    [Fact]
    public void TheBookReadsEveryColumn()
    {
        var raum = Book.Row(1)!;
        Assert.Equal("Raum", raum.Name);
        Assert.Equal(2, raum.Materials.Length);
        Assert.Equal(50, raum.Materials[1].Count);
        Assert.Equal(31034, raum.FailureFx);
        Assert.Equal("Praise", raum.SuccessText);
    }

    [Fact]
    public void OnlyShownCategoriesAndListedRecipesAppear()
    {
        Assert.Equal(["Darkness (Shojin)"], Book.Shown.Select(c => c.Name));
        Assert.Equal([1, 2], Book.Listed(1).Select(r => r.Id));
    }

    [Fact]
    public void MaterialsTravelByItemIdThenInTheOrderTheyWerePlaced()
    {
        var placed = new[]
        {
            new CombineEntry(OldBelt, 1, 4),
            new CombineEntry(Dredium, 50, 1),
            new CombineEntry(OldBelt, 1, 2),
            new CombineEntry(BlessedStone, 10, 0),
            new CombineEntry(OldBelt, 1, 3),
        };

        var wire = ItemCombine.WireOrder(placed);

        Assert.Equal([4, 2, 3, 0, 1], wire.Select(e => e.BagSlot));
        Assert.Equal("345000808001345000808001345000808001389227000010389235000050",
            ItemCombine.MaterialText(wire));
    }

    [Fact]
    public void EachMaterialIsTwelveDigits()
    {
        Assert.Equal("389227000010", ItemCombine.MaterialText([new CombineEntry(BlessedStone, 10, 0)]));
        Assert.Equal("389227000999", ItemCombine.MaterialText([new CombineEntry(BlessedStone, 5000, 0)]));
    }

    [Fact]
    public void ARecipeWithAnUnknownMaterialCannotBeCrafted()
    {
        var raum = Book.Row(1)!;
        Assert.True(ItemCombine.Craftable(raum, _ => true));
        Assert.False(ItemCombine.Craftable(raum, item => item != Dredium));
    }

    [Fact]
    public void TheRequestNamesTheNearestNpcWithTheDialogsTemplate()
    {
        var nearby = new[] { (Id: 9001, Template: 19073, Distance: 30f), (Id: 9002, Template: 0, Distance: 1f), (Id: 9003, Template: 19073, Distance: 4f) };

        Assert.Equal(9003, ItemCombine.Craftsman(nearby, 19073));
        Assert.Equal(ItemCombine.NoCraftsman, ItemCombine.Craftsman(nearby, 31402));
    }

    [Fact]
    public void TheIntroKeepsItsParagraphsButNotItsLineBreaks() =>
        Assert.Equal("Pathos's epic... With God\n\nBut time", ItemCombine.Paragraphs("Pathos's epic... \n With God\n\nBut time"));

    [Theory]
    [InlineData(4.2, 4)]
    [InlineData(1.5, 1)]
    [InlineData(1.01, 1)]
    public void TheCooldownCountsDownToOneSecond(double remaining, int shown) =>
        Assert.Equal(shown, ItemCombine.SecondsLeft(remaining));

    [Fact]
    public void OnlySuccessAndFailureConsumeTheMaterials()
    {
        Assert.True(ItemCombine.IsDone(ItemCombine.Succeeded));
        Assert.True(ItemCombine.IsDone(ItemCombine.Failed));
        Assert.False(ItemCombine.IsDone(3));
        Assert.False(ItemCombine.IsDone(0));
    }
}
