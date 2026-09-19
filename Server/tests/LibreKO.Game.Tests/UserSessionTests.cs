using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class UserSessionTests
{
    [Fact]
    public void RecalculateStats_AppliesWeaponEnchantBuffToTotalHit()
    {
        const int weaponItemId = 180000001;

        var gameData = Substitute.For<IGameDataService>();
        gameData.GetItem(weaponItemId).Returns(new ItemData
        {
            Num = weaponItemId,
            Kind = 21,
            Slot = 1,
            Damage = 80,
            Duration = 100
        });

        var coefficient = new CoefficientData
        {
            ClassId = 101,
            ShortSword = 1,
            Sword = 1,
            Axe = 1,
            Club = 1,
            Spear = 1,
            Staff = 1,
            Bow = 1,
            Hp = 1,
            Mp = 1,
            Ac = 1,
            Hitrate = 1,
            Evasionrate = 1
        };

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = new UserSession(client, 1, 1)
        {
            Class = 101,
            Level = 20,
            Strength = 60,
            Stamina = 50,
            Dexterity = 50,
            Intelligence = 50
        };
        session.Inventory[InventoryConstants.RightHand].ItemId = weaponItemId;
        session.Inventory[InventoryConstants.RightHand].Durability = 100;
        session.Inventory[InventoryConstants.RightHand].Count = 1;

        session.RecalculateStats(coefficient, gameData);
        var baselineHit = session.Stats.TotalHit;

        session.ActiveBuffs[500049] = new ActiveBuff
        {
            MagicId = 500049,
            CasterId = session.CharacterId,
            BuffType = LibreKO.Common.Enums.BuffType.WeaponDamage,
            BonusAttack = 5,
            ExpireTicks = DateTime.UtcNow.AddMinutes(30).Ticks
        };

        session.RecalculateStats(coefficient, gameData);

        session.Stats.TotalHit.Should().BeGreaterThan(baselineHit);
    }

    [Fact]
    public void SerializeSavedMagic_SkipsExpiredBuffs()
    {
        var gameData = Substitute.For<IGameDataService>();

        var sourceClient = Substitute.For<LibreKO.Common.Infrastructure.Network.IClient>();
        sourceClient.Id.Returns(Guid.NewGuid());
        var source = new UserSession(sourceClient, 1, 1);
        source.ActiveBuffs[100] = new ActiveBuff
        {
            MagicId = 100,
            CasterId = 5,
            BuffType = 0,
            ExpireTicks = DateTime.UtcNow.AddMinutes(1).Ticks
        };
        source.ActiveBuffs[200] = new ActiveBuff
        {
            MagicId = 200,
            CasterId = 5,
            BuffType = 0,
            ExpireTicks = DateTime.UtcNow.AddMinutes(-1).Ticks
        };

        var serialized = source.SerializeSavedMagic();

        var targetClient = Substitute.For<LibreKO.Common.Infrastructure.Network.IClient>();
        targetClient.Id.Returns(Guid.NewGuid());
        var target = new UserSession(targetClient, 2, 2);
        target.LoadSavedMagic(serialized, gameData);

        target.ActiveBuffs.Should().ContainKey(100);
        target.ActiveBuffs.Should().NotContainKey(200);
    }
}
