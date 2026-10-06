using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class FortuneTests
{
    private const string Deck = """
        {
         "cards": {"0": "card00.png", "19": "card19.png"},
         "frames": {"spin": ["spin_00.png", "spin_01.png"], "final": ["final_00.png"]},
         "readings": [
          {"id": 1, "category": 0, "level": 1, "title": "Monster's shot", "lines": ["Watch your back!!", "The monster is staring at you", ""], "weight": 100},
          {"id": 99, "category": 19, "level": 4, "title": "Treasure acquisition", "lines": ["Oh my...", "This kind of fortune rarely happens", "You'll be extremely lucky today"], "weight": 10}
         ]
        }
        """;

    [Fact]
    public void TheDeckIsRead()
    {
        var deck = Fortune.Parse(Deck);

        Assert.Equal(2, deck.Readings.Length);
        Assert.Equal("card19.png", deck.CardFor(19));
        Assert.Null(deck.CardFor(7));
        Assert.Equal(new[] { "spin_00.png", "spin_01.png" }, deck.FramesOf(Fortune.SpinFrames));
        Assert.Equal("You'll be extremely lucky today", deck.Readings[1].Lines[2]);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 99)]
    [InlineData(109, 99)]
    public void TheDrawFollowsTheWeights(int rolled, int expected)
    {
        var deck = Fortune.Parse(Deck);
        int asked = -1;

        var reading = Fortune.Draw(deck.Readings, total => { asked = total; return rolled; });

        Assert.Equal(110, asked);
        Assert.Equal(expected, reading!.Id);
    }

    [Fact]
    public void AnEmptyDeckDrawsNothing()
    {
        Assert.Null(Fortune.Draw(FortuneDeck.Empty.Readings, _ => 0));
    }

    [Fact]
    public void EachCardPlaysItsOwnEffect()
    {
        Assert.Equal(491038, Fortune.EffectSkill(0));
        Assert.Equal(491057, Fortune.EffectSkill(19));
    }
}
