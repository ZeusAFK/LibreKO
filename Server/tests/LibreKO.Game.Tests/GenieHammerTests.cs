using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class GenieHammerTests
{
    [Theory]
    [InlineData(true, 10, 30, true)]
    [InlineData(true, 10, 1, true)]
    [InlineData(true, 11, 30, false)]
    [InlineData(false, 0, 30, false)]
    [InlineData(true, 0, 0, false)]
    public async Task RepairRequiresActiveGenieWornGearAndACharge(bool active, short durability, short charges, bool expected)
    {
        var client = Substitute.For<IClient>();
        var session = new UserSession(client, 1, 1) { Hp = 100, Level = 60, GenieActive = active };
        session.GenieTime.Load(7200);
        var data = Substitute.For<IGameDataService>();
        data.GetItem(100).Returns(new ItemData { Num = 100, Duration = 100 });
        data.GetItem(101).Returns(new ItemData { Num = 101, Duration = 100 });
        data.GetItem(810227000).Returns(new ItemData { Num = 810227000, Duration = 30, ReqLevelMax = 100 });
        session.Inventory[6].ItemId = 100;
        session.Inventory[6].Durability = durability;
        session.Inventory[7].ItemId = 101;
        session.Inventory[7].Durability = 75;
        var hammer = session.Inventory[InventoryConstants.InventoryStart];
        hammer.ItemId = 810227000; hammer.Count = 1; hammer.Durability = charges;
        var service = new GenieHammerService(data, Substitute.For<IUserNotificationService>());
        Assert.Equal(expected, await service.UseAsync(session, 10));
        Assert.Equal(expected ? 100 : durability, session.Inventory[6].Durability);
        Assert.Equal(expected ? 100 : 75, session.Inventory[7].Durability);
        Assert.Equal(expected ? charges - 1 : charges, hammer.Durability);
        if (expected)
        {
            Assert.False(await service.UseAsync(session, 10));
            Assert.Equal(charges - 1, hammer.Durability);
        }
    }
}
