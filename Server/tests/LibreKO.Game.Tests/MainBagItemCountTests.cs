using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class MainBagItemCountTests : GameTestBase
{
    private const int Arrow = 391010000;
    private const byte ArrowKind = 120;
    private const int MainBagSlot = InventoryConstants.InventoryStart;
    private const int MagicBagSlot = InventoryConstants.MagicBagStart;

    [Fact]
    public void ArrowsInAMagicBagDoNotCount()
    {
        using var provider = Provider();
        var session = Archer(provider);
        session.Inventory[MagicBagSlot].ItemId = Arrow;
        session.Inventory[MagicBagSlot].Count = 50;

        var usage = provider.GetRequiredService<IMagicItemUsageService>();

        usage.CanUseItem(session, Arrow).Should().BeFalse();
        usage.HasArrows(session).Should().BeFalse();
    }

    [Fact]
    public async Task ArrowsAreTakenFromTheMainBagAndTheMagicBagIsLeftAlone()
    {
        using var provider = Provider();
        var session = Archer(provider);
        session.Inventory[MainBagSlot].ItemId = Arrow;
        session.Inventory[MainBagSlot].Count = 2;
        session.Inventory[MagicBagSlot].ItemId = Arrow;
        session.Inventory[MagicBagSlot].Count = 50;

        var usage = provider.GetRequiredService<IMagicItemUsageService>();

        (await usage.TryConsumeArrowAsync(session, 3)).Should().BeFalse();
        (await usage.TryConsumeItemAsync(session, Arrow, 2)).Should().BeTrue();
        session.Inventory[MainBagSlot].IsEmpty.Should().BeTrue();
        session.Inventory[MagicBagSlot].Count.Should().Be(50);
    }

    private static ServiceProvider Provider() =>
        CreateProvider(_ => { }, gameData =>
        {
            gameData.GetItem(Arrow).Returns(new ItemData
            {
                Num = Arrow,
                Name = "Arrow",
                Kind = ArrowKind,
                Countable = 1,
            });
            gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
        });

    private static UserSession Archer(ServiceProvider provider)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId: 611, accountId: 621);
        session.Name = "Archer";
        session.Class = 101;
        session.Level = 30;
        return session;
    }
}
