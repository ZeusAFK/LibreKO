using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class TextWrapTests
{
    private const string Description = "Increases all stats of party members by 15 for 60 seconds and enables Cry Echo";

    [Fact]
    public void ALongDescriptionIsBrokenIntoShortLines()
    {
        var wrapped = TextWrap.Wrap(Description, 20);
        foreach (var line in wrapped.Split('\n'))
            Assert.True(line.Length <= 20, line);
        Assert.Equal(Description, wrapped.Replace('\n', ' '));
    }

    [Fact]
    public void ExistingLineBreaksAreKept()
    {
        Assert.Equal("First\nSecond", TextWrap.Wrap("First\nSecond", 40));
    }
}
