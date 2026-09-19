using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Persistence.Seed.Entities;
using Xunit;

namespace LibreKO.Game.Tests;

public class NpcSeedTests
{
    private const int MaxSizeSpreadWithinAFamily = 3;
    private const int AncientProtoId = 5301;
    private const int AncientTwinProtoId = 8855;
    private const short KarusEslantZone = 11;
    private const short ElMoradEslantZone = 12;
    private const int EslantGuardPosts = 8;

    private static List<NpcData> Protos() => new NpcSeed().GetSeedData().ToList();

    private static HashSet<int> SpawnedMonsterIds() => new NpcPosSeed().GetSeedData()
        .Where(pos => pos.ActType < NpcPosData.NpcSpawnActTypeBase)
        .Select(pos => (int)pos.NpcId)
        .ToHashSet();

    [Fact]
    public void NoSpawnedMonsterTowersOverTheRestOfItsOwnFamily()
    {
        var spawned = SpawnedMonsterIds();
        var offenders = Protos()
            .Where(proto => proto.IsMonster && spawned.Contains(proto.Id) && proto.Size > 0)
            .GroupBy(proto => (proto.Name, proto.ModelId))
            .Where(family => family.Count() > 1)
            .Select(family => new
            {
                family.Key,
                Biggest = family.MaxBy(proto => proto.Size)!,
                Smallest = family.MinBy(proto => proto.Size)!,
            })
            .Where(family => family.Biggest.Size > family.Smallest.Size * MaxSizeSpreadWithinAFamily)
            .Select(family =>
                $"{family.Key.Name} on model {family.Key.ModelId}: #{family.Biggest.Id} is size "
                + $"{family.Biggest.Size} against #{family.Smallest.Id} at {family.Smallest.Size}")
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Theory]
    [InlineData(KarusEslantZone, (byte)EntityNation.Karus, (byte)EntityNation.ElMorad)]
    [InlineData(ElMoradEslantZone, (byte)EntityNation.ElMorad, (byte)EntityNation.Karus)]
    public void EachEslantServesItsOwnNation(short zoneId, byte own, byte enemy)
    {
        var protos = Protos().Where(proto => !proto.IsMonster)
            .ToDictionary(proto => proto.Id, proto => proto);
        var npcs = new NpcPosSeed().GetSeedData()
            .Where(pos => pos.ZoneId == zoneId && pos.ActType >= NpcPosData.NpcSpawnActTypeBase)
            .Where(pos => protos.ContainsKey(pos.NpcId))
            .ToList();

        npcs.Should().NotBeEmpty("an empty zone is served its neighbour's NPCs by the shared map");

        npcs.Where(pos => protos[pos.NpcId].Group == enemy)
            .Select(pos => $"{pos.NpcId} {protos[pos.NpcId].Name} at ({pos.LeftX}, {pos.TopZ})")
            .Should().BeEmpty();

        npcs.Count(pos => protos[pos.NpcId].NpcType is >= NpcData.TypeGuard and <= NpcData.TypeWarGuard)
            .Should().Be(EslantGuardPosts);
        npcs.Where(pos => protos[pos.NpcId].NpcType is >= NpcData.TypeGuard and <= NpcData.TypeWarGuard)
            .Should().OnlyContain(pos => protos[pos.NpcId].Group == own);
    }

    [Fact]
    public void TheTwoAncientsAreTheSameCreature()
    {
        var protos = Protos();
        var ancient = protos.Single(proto => proto.Id == AncientProtoId && proto.IsMonster);
        var twin = protos.Single(proto => proto.Id == AncientTwinProtoId && proto.IsMonster);

        ancient.Name.Should().Be(twin.Name);
        ancient.ModelId.Should().Be(twin.ModelId);
        ancient.Size.Should().Be(twin.Size);
        ancient.Level.Should().Be(twin.Level);
        ancient.Hp.Should().Be(twin.Hp);
        ancient.NpcType.Should().Be(twin.NpcType);
    }
}
