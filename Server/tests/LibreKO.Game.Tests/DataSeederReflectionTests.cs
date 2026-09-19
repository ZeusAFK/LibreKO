using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using Xunit;

namespace LibreKO.Game.Tests;

public class DataSeederReflectionTests
{
    private static PropertyInfo[] SeederWritableProperties(Type entity) =>
        entity.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToArray();

    [Theory]
    [InlineData(typeof(AchievementData), nameof(AchievementData.NpcIds))]
    [InlineData(typeof(AchievementTitleData), nameof(AchievementTitleData.HasBonus))]
    public void TheSeederFilterExcludesComputedProperties(Type entity, string computed)
    {
        entity.GetProperty(computed)!.CanWrite.Should().BeFalse();
        SeederWritableProperties(entity).Should().NotContain(p => p.Name == computed);
    }

    [Theory]
    [InlineData(typeof(AchievementData))]
    [InlineData(typeof(AchievementTitleData))]
    [InlineData(typeof(AttendanceRewardData))]
    public void EverySeederWritablePropertyCanActuallyBeSet(Type entity)
    {
        var instance = Activator.CreateInstance(entity)!;

        foreach (var property in SeederWritableProperties(entity))
        {
            var value = property.GetValue(instance);
            property.Invoking(p => p.SetValue(instance, value)).Should().NotThrow(
                $"the seeder assigns {entity.Name}.{property.Name} by reflection");
        }
    }
}
