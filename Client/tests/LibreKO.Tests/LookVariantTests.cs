using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class LookVariantTests
{
    private const int Variants = 3;
    private const int Last = Variants - 1;
    private const int Single = 1;
    private const int Missing = 0;
    private const int SavedOutOfRange = 5;
    private const int Forward = 1;
    private const int Back = -1;

    [Theory]
    [InlineData(0, Forward, 1)]
    [InlineData(Last, Forward, 0)]
    [InlineData(0, Back, Last)]
    [InlineData(SavedOutOfRange, Back, 1)]
    public void StepsWrapWithinTheRaceVariants(int value, int direction, int expected) =>
        Assert.Equal(expected, LookVariant.Step(value, direction, Variants));

    [Theory]
    [InlineData(Single)]
    [InlineData(Missing)]
    public void AValueWithNothingToStepToStaysPut(int count)
    {
        Assert.False(LookVariant.CanStep(count));
        Assert.Equal(SavedOutOfRange, LookVariant.Step(SavedOutOfRange, Forward, count));
    }

    [Fact]
    public void ClampKeepsTheValueInsideTheVariants() =>
        Assert.Equal(Last, LookVariant.Clamp(SavedOutOfRange, Variants));

    [Fact]
    public void MissingVariantsDoNotOverwriteTheSavedValue() =>
        Assert.Equal(SavedOutOfRange, LookVariant.Clamp(SavedOutOfRange, Missing));
}
