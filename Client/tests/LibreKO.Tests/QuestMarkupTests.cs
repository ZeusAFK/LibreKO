using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class QuestMarkupTests
{
    [Theory]
    [InlineData("<font color=@#006666@>Scroll</font>", "[color=#006666]Scroll[/color]")]
    [InlineData("<FONT color=\"#aAbBcC\">Scroll</FONT>", "[color=#aAbBcC]Scroll[/color]")]
    [InlineData("<font color=invalid>Scroll</font>", "Scroll")]
    [InlineData("<font color=#112233>Scroll", "[color=#112233]Scroll[/color]")]
    [InlineData("[Warrior] Jed", "[lb]Warrior] Jed")]
    public void ConvertsOnlySupportedRetailFormatting(string source, string expected)
    {
        Assert.Equal(expected, QuestMarkup.Rich(source, "Alex"));
    }

    [Fact]
    public void ResolvesTheCurrentCharacterWithoutInterpretingTheirNameAsMarkup()
    {
        Assert.Equal("Hello Alex", QuestMarkup.Rich("Hello <selfname>", "Alex"));
        Assert.Equal("Hello Robin", QuestMarkup.Rich("Hello <selfname>", "Robin"));
        Assert.Equal("Hello [lb]color=red]X", QuestMarkup.Rich("Hello <selfname>", "[color=red]X"));
        Assert.Equal("<font color=#123456>X</font>", QuestMarkup.Rich("<selfname>", "<font color=#123456>X</font>"));
    }

    [Fact]
    public void NestedAndInvalidTagsRemainBalanced()
    {
        Assert.Equal("[color=#112233]a[color=#445566]b[/color]c[/color]",
            QuestMarkup.Rich("<font color=#112233>a<font color=#445566>b</font>c</font>", "Alex"));
        Assert.Equal("[color=#112233]abc[/color]",
            QuestMarkup.Rich("<font color=#112233>a<font color=no>b</font>c</font></font>", "Alex"));
    }

    [Fact]
    public void PlainLabelsKeepTextAndResolveNamesWithoutExposingTags()
    {
        Assert.Equal("Hi Alex\n[Warrior] Jed", QuestMarkup.Plain("Hi <font color=@#006666@><selfname></font>|[Warrior] Jed", "Alex"));
    }
}
