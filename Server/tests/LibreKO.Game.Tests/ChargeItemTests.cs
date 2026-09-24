using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class ChargeItemTests : GameTestBase
{
    private const int SpeedUpPack = 389011000;
    private const byte ChargeKind = 255;
    private const short PackUses = 30;
    private const short PackWeight = 10;
    private const int Slot = InventoryConstants.InventoryStart;

    [Fact]
    public async Task UsingAChargeItemSpendsOneUseAndLeavesOneItem()
    {
        using var provider = Provider();
        var session = Holder(provider, uses: PackUses);

        var consumed = await provider.GetRequiredService<IMagicItemUsageService>()
            .TryConsumeItemAsync(session, SpeedUpPack);

        consumed.Should().BeTrue();
        session.Inventory[Slot].Durability.Should().Be(PackUses - 1);
        session.Inventory[Slot].Count.Should().Be(1, "the uses live in the durability, not in the stack");
    }

    [Fact]
    public async Task TheLastUseRemovesTheItem()
    {
        using var provider = Provider();
        var session = Holder(provider, uses: 1);

        await provider.GetRequiredService<IMagicItemUsageService>().TryConsumeItemAsync(session, SpeedUpPack);

        session.Inventory[Slot].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void AChargeItemWeighsOneItemWhateverItsUses()
    {
        using var provider = Provider();
        var session = Holder(provider, uses: PackUses);
        session.Inventory[Slot].Count = 29;

        session.RecalculateStats(CreateBasicCoefficient(101), provider.GetRequiredService<IGameDataService>());

        session.Stats.ItemWeight.Should().Be(PackWeight, "a pack of 30 uses is one item on the scale");
    }

    [Fact]
    public async Task TheGmPanelGrantsAChargeItemAsOneItemWithAllItsUses()
    {
        using var provider = Provider();
        var session = Holder(provider, uses: 0);
        session.Inventory[Slot].Clear();
        session.IsGM = true;

        var packet = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        packet.WriteByte(4);
        packet.WriteInt(SpeedUpPack);
        packet.WriteShort(5);
        await provider.GetRequiredService<IAdminPanelPacketCoordinator>().HandleAsync(session.Client, packet);

        session.Inventory[Slot].ItemId.Should().Be(SpeedUpPack);
        session.Inventory[Slot].Count.Should().Be(1);
        session.Inventory[Slot].Durability.Should().Be(PackUses);
    }

    private static ServiceProvider Provider() =>
        CreateProvider(_ => { }, gameData =>
        {
            gameData.GetItem(SpeedUpPack).Returns(new ItemData
            {
                Num = SpeedUpPack,
                Name = "Speed-Up Potion",
                Kind = ChargeKind,
                Countable = 0,
                Duration = PackUses,
                Weight = PackWeight,
            });
            gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
        });

    private static UserSession Holder(ServiceProvider provider, short uses)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 610, accountId: 620);
        session.Name = "Holder";
        session.Class = 101;
        session.Level = 30;
        session.Hp = 100;
        session.MaxHp = 100;
        session.Inventory[Slot].ItemId = SpeedUpPack;
        session.Inventory[Slot].Count = 1;
        session.Inventory[Slot].Durability = uses;
        return session;
    }
}
