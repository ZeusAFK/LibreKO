using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Game.Protocol;
using Xunit;

namespace LibreKO.Game.Tests;

public class UpgradeAnnouncementTests
{
    private const byte Earring = 91;
    private const byte Sword = 1;

    [Theory]
    [InlineData(Sword, ItemRarity.Upgrade, 7, false)]
    [InlineData(Sword, ItemRarity.Upgrade, 8, true)]
    [InlineData(Sword, ItemRarity.Upgrade, 10, true)]
    [InlineData(Sword, ItemRarity.Reverse, 11, true)]
    [InlineData(Sword, ItemRarity.ReverseUnique, 0, true)]
    [InlineData(Earring, ItemRarity.Upgrade, 1, false)]
    [InlineData(Earring, ItemRarity.Upgrade, 2, true)]
    public void UpgradesAreAnnouncedFromPlusEightAccessoriesFromPlusTwoAndEveryReverseItem(byte kind, byte rarity, int level, bool announced) =>
        UpgradeAnnouncements.Announced(new ItemData { Kind = kind, Rarity = rarity, UpgradeLevel = level }).Should().Be(announced);
}
