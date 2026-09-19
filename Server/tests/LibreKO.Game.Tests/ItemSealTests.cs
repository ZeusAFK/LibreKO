using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class ItemSealTests : GameTestBase
{
    private const int KrowazBoots = 208101000;
    private const int SealStone = 810890000;
    private const string GoodCode = "12345678";

    [Fact]
    public async Task EveryAnswerCarriesTheItemAndSlotItWasAskedAbout()
    {
        var (provider, session, sent) = Open(bound: 0);
        using var _ = provider;

        await Seal(provider, session, ItemSealType.Seal, KrowazBoots, 0, string.Empty);

        var reply = Read(sent);
        reply.Type.Should().Be(ItemSealType.Seal);
        reply.ItemId.Should().Be(KrowazBoots);
        reply.Position.Should().Be(0);
    }

    [Fact]
    public async Task SealingRefusesUntilTheAccountHasACode()
    {
        var (provider, session, sent) = Open(bound: 0);
        using var _ = provider;
        session.Money = 5_000_000;

        await Seal(provider, session, ItemSealType.Seal, KrowazBoots, 0, GoodCode);

        Read(sent).Result.Should().Be(ItemSealResult.NoCodeSet);
        session.Inventory[InventoryConstants.InventoryStart].State.Should().Be(ItemFlag.Unsealed);
    }

    [Fact]
    public async Task TheWrongCodeIsToldApartFromAWrongItem()
    {
        var (provider, session, sent) = Open(bound: 0);
        using var _ = provider;
        session.Money = 5_000_000;
        session.SealCode = GoodCode;

        await Seal(provider, session, ItemSealType.Seal, KrowazBoots, 0, "87654321");

        Read(sent).Result.Should().Be(ItemSealResult.WrongCode);
        session.Inventory[InventoryConstants.InventoryStart].State.Should().Be(ItemFlag.Unsealed);
    }

    [Fact]
    public async Task TheRightCodeSealsTheItemAndChargesTheFee()
    {
        var (provider, session, sent) = Open(bound: 0);
        using var _ = provider;
        session.Money = 5_000_000;
        session.SealCode = GoodCode;

        await Seal(provider, session, ItemSealType.Seal, KrowazBoots, 0, GoodCode);

        Read(sent).Result.Should().Be(ItemSealResult.Succeeded);
        session.Inventory[InventoryConstants.InventoryStart].State.Should().Be(ItemFlag.Sealed);
        session.Money.Should().Be(4_000_000);
    }

    [Fact]
    public async Task AnEmptyPurseIsToldApartFromAWrongCode()
    {
        var (provider, session, sent) = Open(bound: 0);
        using var _ = provider;
        session.Money = 10;
        session.SealCode = GoodCode;

        await Seal(provider, session, ItemSealType.Seal, KrowazBoots, 0, GoodCode);

        Read(sent).Result.Should().Be(ItemSealResult.NeedCoins);
    }

    [Fact]
    public async Task BindingAKrowazItemAsksForNoCodeAndNoFee()
    {
        var (provider, session, sent) = Open(bound: 10);
        using var _ = provider;
        session.Money = 0;

        await Seal(provider, session, ItemSealType.Bind, KrowazBoots, 0, string.Empty);

        Read(sent).Result.Should().Be(ItemSealResult.Succeeded);
        session.Inventory[InventoryConstants.InventoryStart].State.Should().Be(ItemFlag.Bound);
        session.Money.Should().Be(0);
    }

    [Fact]
    public async Task BindingAnAlreadyBoundItemIsRefused()
    {
        var (provider, session, sent) = Open(bound: 10);
        using var _ = provider;
        session.Inventory[InventoryConstants.InventoryStart].Flag = (byte)ItemFlag.Bound;

        await Seal(provider, session, ItemSealType.Bind, KrowazBoots, 0, string.Empty);

        Read(sent).Result.Should().Be(ItemSealResult.Failed);
    }

    [Fact]
    public async Task UnbindingWithoutTheStonesSaysWhichHalfIsMissing()
    {
        var (provider, session, sent) = Open(bound: 10);
        using var _ = provider;
        session.Inventory[InventoryConstants.InventoryStart].Flag = (byte)ItemFlag.Bound;
        Give(session, 1, SealStone, 4);

        await Seal(provider, session, ItemSealType.Unbind, KrowazBoots, 0, string.Empty);

        Read(sent).Result.Should().Be(ItemSealResult.MissingMaterial);
        session.Inventory[InventoryConstants.InventoryStart].State.Should().Be(ItemFlag.Bound);
        session.Inventory[InventoryConstants.InventoryStart + 1].Count.Should().Be(4);
    }

    [Fact]
    public async Task UnbindingSpendsExactlyTheStonesTheItemCosts()
    {
        var (provider, session, sent) = Open(bound: 10);
        using var _ = provider;
        session.Inventory[InventoryConstants.InventoryStart].Flag = (byte)ItemFlag.Bound;
        Give(session, 1, SealStone, 6);
        Give(session, 2, SealStone, 6);

        await Seal(provider, session, ItemSealType.Unbind, KrowazBoots, 0, string.Empty);

        Read(sent).Result.Should().Be(ItemSealResult.Succeeded);
        session.Inventory[InventoryConstants.InventoryStart].State.Should().Be(ItemFlag.NotBound);
        session.Inventory[InventoryConstants.InventoryStart + 1].ItemId.Should().Be(0);
        session.Inventory[InventoryConstants.InventoryStart + 2].Count.Should().Be(2);
    }

    [Fact]
    public async Task AnItemThatCostsNoStonesUnbindsForFree()
    {
        var (provider, session, sent) = Open(bound: 0);
        using var _ = provider;
        session.Inventory[InventoryConstants.InventoryStart].Flag = (byte)ItemFlag.Bound;

        await Seal(provider, session, ItemSealType.Unbind, KrowazBoots, 0, string.Empty);

        Read(sent).Result.Should().Be(ItemSealResult.Succeeded);
        session.Inventory[InventoryConstants.InventoryStart].State.Should().Be(ItemFlag.NotBound);
    }

    [Fact]
    public async Task ASealRequestIsRefusedWhileTradeIsOpen()
    {
        var (provider, session, sent) = Open(bound: 10);
        using var _ = provider;
        session.Trade.ExchangeUser = 999;

        await Seal(provider, session, ItemSealType.Bind, KrowazBoots, 0, string.Empty);

        Read(sent).Result.Should().Be(ItemSealResult.Failed);
    }

    [Fact]
    public async Task ABoundItemMayNotBeTradedNorStalledNorMailed()
    {
        var slot = new ItemSlot { ItemId = KrowazBoots, Count = 1, Flag = (byte)ItemFlag.Bound };
        slot.IsTradable.Should().BeFalse();

        slot.Flag = (byte)ItemFlag.NotBound;
        slot.IsTradable.Should().BeTrue();
    }

    private static void Give(UserSession session, int position, int itemId, ushort count)
    {
        var slot = session.Inventory[InventoryConstants.InventoryStart + position];
        slot.ItemId = itemId;
        slot.Count = count;
    }

    private (ServiceProvider Provider, UserSession Session, List<Packet> Sent) Open(short bound)
    {
        var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(KrowazBoots).Returns(new ItemData
                {
                    Num = KrowazBoots,
                    Kind = 11,
                    Duration = 100,
                    Bound = bound,
                });
                gameData.GetItem(SealStone).Returns(new ItemData { Num = SealStone, Countable = 1 });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = provider.GetRequiredService<SessionManager>()
            .CreateSession(client, characterId: 700, accountId: 710);
        session.Name = "Smith";

        var boots = session.Inventory[InventoryConstants.InventoryStart];
        boots.ItemId = KrowazBoots;
        boots.Count = 1;
        boots.Durability = 100;

        return (provider, session, sent);
    }

    private static async Task Seal(
        ServiceProvider provider,
        UserSession session,
        ItemSealType sealType,
        int itemId,
        byte position,
        string code)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        packet.WriteByte((byte)ItemUpgradeSubOpcode.ItemSeal);
        packet.WriteByte((byte)sealType);
        packet.WriteInt(-1);
        packet.WriteInt(itemId);
        packet.WriteByte(position);
        packet.WriteString(code);

        await provider.GetRequiredService<IItemUpgradeService>()
            .HandleUpgradeAsync(session.Client, packet);
    }

    private static (ItemSealType Type, ItemSealResult Result, int ItemId, byte Position) Read(List<Packet> sent)
    {
        var packet = sent.Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_UPGRADE);
        packet.ResetOffset();
        packet.ReadByte().Should().Be((byte)ItemUpgradeSubOpcode.ItemSeal);
        return ((ItemSealType)packet.ReadByte(), (ItemSealResult)packet.ReadByte(),
            packet.ReadInt(), packet.ReadByte());
    }
}
