using Godot;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class CharacterClassCatalogTests
{
    [Theory]
    [InlineData(101, 1, new[] { 1 })]       // Karus Warrior -> Ark Tuarek
    [InlineData(102, 1, new[] { 2 })]       // Karus Rogue -> Tuarek
    [InlineData(103, 1, new[] { 3, 4 })]    // Karus Mage -> Wrinkle, Pury Tuarek
    [InlineData(104, 1, new[] { 2, 4 })]    // Karus Priest -> Tuarek, Pury Tuarek
    [InlineData(113, 1, new[] { 6 })]       // Karus Kurian -> Kurian
    [InlineData(201, 2, new[] { 11, 12, 13 })] // El Morad Warrior -> Barbarian, Man, Woman
    [InlineData(202, 2, new[] { 12, 13 })] // El Morad Rogue -> Man, Woman
    [InlineData(203, 2, new[] { 12, 13 })] // El Morad Mage -> Man, Woman
    [InlineData(204, 2, new[] { 12, 13 })] // El Morad Priest -> Man, Woman
    [InlineData(213, 2, new[] { 14 })]     // El Morad Porutu -> Porutu
    public void ValidRacesForClass_ReturnsExpectedRaces(int classCode, int nation, int[] expected)
    {
        var races = CharacterClassCatalog.ValidRacesForClass(classCode, nation);
        Assert.Equal(expected, races);
    }

    [Theory]
    [InlineData(6, true)]
    [InlineData(14, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(11, false)]
    [InlineData(12, false)]
    [InlineData(13, false)]
    public void IsBeastRace_IdentifiesKurianAndPorutu(int race, bool expectedBeast)
    {
        Assert.Equal(expectedBeast, CharacterClassCatalog.IsBeastRace(race));
    }

    [Theory]
    [InlineData(1, "Male")]
    [InlineData(2, "Male")]
    [InlineData(3, "Male")]
    [InlineData(4, "Female")]
    [InlineData(6, "Beast")]
    [InlineData(11, "Male")]
    [InlineData(12, "Male")]
    [InlineData(13, "Female")]
    [InlineData(14, "Beast")]
    public void RaceGender_ReturnsAccurateGenderString(int race, string expected)
    {
        Assert.Equal(expected, StarterStats.RaceGender(race));
    }

    [Fact]
    public void PorutuAndKurian_DistinctNamesAndClasses()
    {
        Assert.Equal("Porutu", StarterStats.RaceName(14));
        Assert.Equal("Kurian", StarterStats.RaceName(6));

        Assert.Equal("Porutu", CharacterClassCatalog.DisplayName(213));
        Assert.Equal("Kurian", CharacterClassCatalog.DisplayName(113));

        Assert.Equal("Porutu", CharacterClassCatalog.SpecializationName(213));
        Assert.Equal("Novice Porutu", CharacterClassCatalog.SpecializationName(214));
        Assert.Equal("Master Porutu", CharacterClassCatalog.SpecializationName(215));

        Assert.Equal("Kurian", CharacterClassCatalog.SpecializationName(113));
        Assert.Equal("Novice Kurian", CharacterClassCatalog.SpecializationName(114));
        Assert.Equal("Master Kurian", CharacterClassCatalog.SpecializationName(115));
    }

    [Fact]
    public void HairCode_PackAndUnpack()
    {
        var col = new Color(0.8f, 0.4f, 0.2f);
        int packed = HairCode.Pack(3, col);
        Assert.Equal(3, HairCode.StyleOf(packed));

        var unpackedCol = HairCode.ColourOf(packed);
        Assert.InRange(unpackedCol.R, 0.79f, 0.81f);
        Assert.InRange(unpackedCol.G, 0.39f, 0.41f);
        Assert.InRange(unpackedCol.B, 0.19f, 0.21f);
    }

    [Theory]
    [InlineData(6, new[] { 113, 114, 115 })] // Kurian
    [InlineData(14, new[] { 213, 214, 215 })] // Porutu
    [InlineData(1, new[] { 101, 105, 106 })]  // Ark Tuarek
    [InlineData(11, new[] { 201, 205, 206 })] // Barbarian
    public void ValidClassesForRace_ReturnsExpectedClasses(int race, int[] expected)
    {
        Assert.Equal(expected, CharacterClassCatalog.ValidClassesForRace(race));
    }

    [Theory]
    [InlineData(1, 1)]  // Ark Tuarek -> Karus
    [InlineData(6, 1)]  // Kurian -> Karus
    [InlineData(11, 2)] // Barbarian -> El Morad
    [InlineData(14, 2)] // Porutu -> El Morad
    public void NationForRace_ReturnsExpectedNation(int race, int expectedNation)
    {
        Assert.Equal(expectedNation, CharacterClassCatalog.NationForRace(race));
    }
}
