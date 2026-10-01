using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Game.Tests;

public class NpcSpawnSeedTests
{
    private const short Delos = 30;
    private const int SiegeWeaponElrond = 523;
    private const int StandingNpc = 100;
    private const int PotionMerchantUronia = 506;
    private const int UroniaX = 549;
    private const int UroniaZ = 222;
    private const short Moradon = 21;
    private const int LunarLadyMagpie = 31506;
    private const int WildGrass = 9856;
    private const int GiantGolem = 2452;
    private const short Karus = 1;
    private const short ElMorad = 2;
    private const short KarusEslant = 11;
    private const short ElMoradEslant = 12;

    private static List<NpcPosData> Positions([CallerFilePath] string source = "")
    {
        var directory = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Seed", "Data"));
        return Directory.GetFiles(directory, "NpcPositions.zone*.json")
            .Order(StringComparer.OrdinalIgnoreCase)
            .SelectMany(path => JsonSerializer.Deserialize<List<NpcPosData>>(File.ReadAllText(path))!)
            .ToList();
    }

    [Fact]
    public void ElrondStandsAtHisPostInsteadOfInsideTheWall()
    {
        var elrond = Positions().Single(p => p.ZoneId == Delos && p.NpcId == SiegeWeaponElrond);

        elrond.ActType.Should().Be(StandingNpc);
        elrond.SpawnRange.Should().BeLessThanOrEqualTo(1, "a standing NPC keeps to the spot it is placed on");
    }

    [Fact]
    public void DelosSellsPotionsFromUroniaNotFromTheMonsterSharingHerId()
    {
        var stall = Positions().Single(p => p.ZoneId == Delos && p.LeftX == UroniaX && p.TopZ == UroniaZ);

        stall.NpcId.Should().Be(PotionMerchantUronia);
        stall.ActType.Should().BeGreaterThanOrEqualTo(NpcPosData.NpcSpawnActTypeBase, "506 is both Uronia and the monster Lobo; this spot is hers");
    }

    [Fact]
    public void MagpieStandsInMoradonBesideTheWildGrassHerQuestHunts()
    {
        var moradon = Positions().Where(p => p.ZoneId == Moradon).ToList();
        var magpie = moradon.Single(p => p.NpcId == LunarLadyMagpie);

        magpie.ActType.Should().Be(StandingNpc);
        moradon.Should().Contain(p => p.NpcId == WildGrass, "quest 1745 hunts forty of them");
        moradon.Should().NotContain(p => p.NpcId != LunarLadyMagpie && p.LeftX == magpie.LeftX && p.TopZ == magpie.TopZ,
            "nobody else stands on her spot");
    }

    [Theory]
    [InlineData(Karus)]
    [InlineData(ElMorad)]
    [InlineData(KarusEslant)]
    [InlineData(ElMoradEslant)]
    public void TheGiantGolemsTheFestivalHuntsStandInBothNationsLands(short zone) =>
        Positions().Should().Contain(p => p.ZoneId == zone && p.NpcId == GiantGolem,
            "quest 1664 and the other golem hunts count 2452, and each nation hunts in its own homeland or Eslant");

    [Theory]
    [InlineData(23400)]
    [InlineData(32756)]
    [InlineData(29999)]
    [InlineData(23500)]
    public void NpcsTheRetailClientCannotNameAreNotPlaced(int npcId) =>
        Positions().Should().NotContain(p => p.NpcId == npcId, "the game client's own NPC table has no entry for it, so it would show as NPC #id");
}
