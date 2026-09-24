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

    private static List<NpcPosData> Positions([CallerFilePath] string source = "")
    {
        var path = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Seed", "Data", "NpcPositions.json"));
        return JsonSerializer.Deserialize<List<NpcPosData>>(File.ReadAllText(path))!;
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
}
