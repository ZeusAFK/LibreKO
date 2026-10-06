using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class PlayerRigTests
{
    [Theory]
    [InlineData(PlayerRig.KarusKurian)]
    [InlineData(PlayerRig.ElMoradKurian)]
    public void TheKurianRacesAreKurian(int race) => Assert.True(PlayerRig.IsKurian(race));

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(11)]
    [InlineData(13)]
    public void TheOtherRacesAreNot(int race) => Assert.False(PlayerRig.IsKurian(race));

    [Theory]
    [InlineData(PlayerRig.KarusKurian, PlayerRig.KarusBase)]
    [InlineData(PlayerRig.ElMoradKurian, PlayerRig.ElMoradBase)]
    [InlineData(3, 3)]
    [InlineData(12, 12)]
    public void AStandardLookIsWornAsTheNationsBaseRace(int race, int expected) =>
        Assert.Equal(expected, PlayerRig.StandardRace(race));

    [Fact]
    public void ALookWithTheSameBonesInTheSameOrderMatches() =>
        Assert.True(PlayerRig.SameBones(new[] { "Hips", "Chest", "Chest2" }, new[] { "Hips", "Chest", "Chest2" }));

    [Fact]
    public void ADifferentBoneOrderDoesNotMatch() =>
        Assert.False(PlayerRig.SameBones(new[] { "Hips", "Chest", "Chest2" }, new[] { "Hips", "Chest2", "Chest" }));

    [Fact]
    public void ADifferentBoneCountDoesNotMatch() =>
        Assert.False(PlayerRig.SameBones(new[] { "Hips", "Chest" }, new[] { "Hips", "Chest", "Chest2" }));

    [Fact]
    public void AnEmptyRigNeverMatches() =>
        Assert.False(PlayerRig.SameBones(System.Array.Empty<string>(), System.Array.Empty<string>()));
}
