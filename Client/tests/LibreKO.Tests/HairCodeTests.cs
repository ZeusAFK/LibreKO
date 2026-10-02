using Godot;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class HairCodeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(7)]
    public void EveryBakedStyleSurvivesPacking(int style)
    {
        Assert.Equal(style, HairCode.StyleOf(HairCode.Pack(style, new Color(0.5f, 0.25f, 0.75f))));
    }
}
