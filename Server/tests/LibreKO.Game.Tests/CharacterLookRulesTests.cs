using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Game.World;
using Xunit;

namespace LibreKO.Game.Tests;

public class CharacterLookRulesTests
{
    private const byte FirstFace = 0;
    private const int FirstStyle = 0;
    private const int Colour = 0x5A_38_20;
    private const int UnknownRace = 5;

    private static int Hair(int style) => (style << CharacterLookRules.HairStyleShift) | Colour;

    [Theory]
    [InlineData(CharacterRace.KarusArchTuarek, 4, 4)]
    [InlineData(CharacterRace.KarusTuarek, 4, 4)]
    [InlineData(CharacterRace.KarusWrinkleTuarek, 8, 4)]
    [InlineData(CharacterRace.KarusPuriTuarek, 8, 8)]
    [InlineData(CharacterRace.ElMoradBarbarian, 8, 7)]
    [InlineData(CharacterRace.ElMoradMale, 8, 7)]
    [InlineData(CharacterRace.ElMoradFemale, 8, 7)]
    public void EachRaceAcceptsItsOwnFacesAndHairStyles(CharacterRace race, int faces, int styles)
    {
        var lastFace = (byte)(faces - 1);
        var lastStyle = styles - 1;

        CharacterLookRules.Allows((byte)race, FirstFace, Hair(FirstStyle), Hair(FirstStyle)).Should().BeTrue();
        CharacterLookRules.Allows((byte)race, lastFace, Hair(lastStyle), Hair(FirstStyle)).Should().BeTrue();
        CharacterLookRules.Allows((byte)race, (byte)faces, Hair(FirstStyle), Hair(FirstStyle)).Should().BeFalse();
        CharacterLookRules.Allows((byte)race, FirstFace, Hair(styles), Hair(FirstStyle)).Should().BeFalse();
    }

    [Theory]
    [InlineData(CharacterRace.KarusKurian)]
    [InlineData(CharacterRace.ElMoradPorutu)]
    public void ARaceWithoutHairStylesKeepsItsStyleAndOnlyFace(CharacterRace race)
    {
        const int kept = 3;

        CharacterLookRules.Allows((byte)race, FirstFace, Hair(kept), Hair(kept)).Should().BeTrue();
        CharacterLookRules.Allows((byte)race, FirstFace, Hair(FirstStyle), Hair(kept)).Should().BeFalse();
        CharacterLookRules.Allows((byte)race, FirstFace + 1, Hair(kept), Hair(kept)).Should().BeFalse();
    }

    [Fact]
    public void AnUnknownRaceAcceptsNoLook() =>
        CharacterLookRules.Allows(UnknownRace, FirstFace, Hair(FirstStyle), Hair(FirstStyle)).Should().BeFalse();
}
