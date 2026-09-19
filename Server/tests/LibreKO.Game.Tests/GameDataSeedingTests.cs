using FluentAssertions;
using LibreKO.Common.Infrastructure.Persistence.Seed;
using LibreKO.Common.Infrastructure.Persistence.Seed.Entities;
using System.Collections;
using System.Reflection;
using System.Text.Json;
using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Game.Tests;

public class GameDataSeedingTests
{
    [Fact]
    public void AllGameDataSeeds_DeserializeSuccessfully()
    {
        var seedTypes = typeof(LevelUpSeed).Assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsClass: true }
                && type.Namespace == typeof(LevelUpSeed).Namespace
                && type.Name.EndsWith("Seed", StringComparison.Ordinal)
                && type.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(type => type.Name)
            .ToArray();

        seedTypes.Should().NotBeEmpty();

        foreach (var seedType in seedTypes)
        {
            var seed = Activator.CreateInstance(seedType);
            seed.Should().NotBeNull();

            var getSeedData = seedType.GetMethod(nameof(IEntitySeed<object>.GetSeedData), BindingFlags.Instance | BindingFlags.Public);
            getSeedData.Should().NotBeNull($"seed type {seedType.Name} must expose GetSeedData()");

            var data = getSeedData!.Invoke(seed!, null);
            data.Should().BeAssignableTo<IEnumerable>();

            var enumerable = (IEnumerable)data!;
            enumerable.Cast<object>().Should().NotBeNull($"seed type {seedType.Name} should deserialize its JSON payload");
        }
    }

    [Fact]
    public void HardenedSeeds_EmitUniqueKeys()
    {
        var levelRows = new LevelUpSeed().GetSeedData().ToList();
        levelRows.Select(row => row.Level).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void AchievementSeed_CompletionRowsNameWhatTheyRequire()
    {
        var rows = new AchievementSeed().GetSeedData().ToDictionary(row => row.Id);
        var completion = rows.Values
            .Where(row => row.ConditionTable == AchievementConditionTable.Completion)
            .ToList();

        completion.Should().HaveCount(71);

        foreach (var row in completion)
        {
            row.RequiredIds.Should().NotBeEmpty($"completion achievement {row.Id} must name a requirement");
            row.Target.Should().Be(row.RequiredIds.Length);

            if (row.CompletionKind == CompletionAchievementKind.Achievements)
                row.RequiredIds.Should().OnlyContain(id => rows.ContainsKey(id));
        }
    }

    [Fact]
    public void AchievementSeed_EveryTrackedRowCarriesATarget()
    {
        var rows = new AchievementSeed().GetSeedData().ToList();

        var untracked = rows
            .Where(row => row.ConditionTable is AchievementConditionTable.Completion or AchievementConditionTable.Normal)
            .Where(row => row.Target <= 0)
            .ToList();

        untracked.Should().BeEmpty();
    }

    [Fact]
    public void MagicType1Seed_CarriesPerSkillAdditionalDamage()
    {
        var rows = new MagicType1Seed().GetSeedData().ToList();

        rows.Select(row => row.AddDamage).Distinct().Should().HaveCountGreaterThan(5,
            "the client's copy of this table flattens the column to a single placeholder, so a "
            + "handful of distinct values is the signature of that copy leaking back into the seed");

        rows.Single(row => row.Id == CryEchoSkillId).AddDamage.Should().Be(CryEchoAdditionalDamage,
            "the shipped skill description promises this much additional damage");
    }

    [Fact]
    public void MagicType6Seed_NationIsANationCode()
    {
        var rows = new MagicType6Seed().GetSeedData().ToList();

        rows.Select(row => row.Nation).Should().OnlyContain(nation => nation <= MaxNationCode,
            "anything above this came from a column that is not the nation");
    }

    private const int CryEchoSkillId = 106782;
    private const short CryEchoAdditionalDamage = 200;
    private const byte MaxNationCode = 2;

    [Fact]
    public void ItemExchangeSeed_SecondJobChangeOrigins_MatchTrackedQuestScripts()
    {
        var exchanges = new ItemExchangeSeed().GetSeedData().ToDictionary(row => row.Index);

        exchanges[461].GetOriginItems().Take(4).Should().Equal(
            (810095000, 1),
            (810090000, 1),
            (810094000, 1),
            (0, 0));

        exchanges[462].GetOriginItems().Take(4).Should().Equal(
            (810095000, 1),
            (810092000, 1),
            (810093000, 1),
            (0, 0));

        exchanges[463].GetOriginItems().Take(4).Should().Equal(
            (810095000, 1),
            (810091000, 1),
            (810092000, 1),
            (0, 0));

        exchanges[464].GetOriginItems().Take(4).Should().Equal(
            (810095000, 1),
            (810091000, 1),
            (810093000, 1),
            (0, 0));
    }
}
