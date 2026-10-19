using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class CreateCharacterTextsTests
{
    [Theory]
    [InlineData(CreateCharacterResult.SlotFull, 1911)]
    [InlineData(CreateCharacterResult.NationRaceMismatch, 1907)]
    [InlineData(CreateCharacterResult.NameAlreadyExists, 1912)]
    [InlineData(CreateCharacterResult.PointsRemaining, 1913)]
    [InlineData(CreateCharacterResult.InvalidCharacters, 7654)]
    public void EachRefusalUsesTheRetailText(CreateCharacterResult code, int text) =>
        Assert.Equal(text, CreateCharacterTexts.TextFor(code));

    [Theory]
    [InlineData(CreateCharacterResult.InvalidStats)]
    [InlineData((CreateCharacterResult)99)]
    public void AnUnknownRefusalSaysUnknownError(CreateCharacterResult code) =>
        Assert.Equal(CreateCharacterTexts.UnknownText, CreateCharacterTexts.TextFor(code));
}
