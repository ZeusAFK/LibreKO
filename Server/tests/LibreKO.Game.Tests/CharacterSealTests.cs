using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class CharacterSealTests : GameTestBase
{
    private const int AccountA = 1;
    private const int AccountB = 2;
    private const int Scroll = 800111000;
    private const byte BagPos = 3;
    private const string Pin = "1234";

    private static ServiceProvider Provider() => CreateProvider(db =>
    {
        db.Accounts.Add(new Account { Id = AccountA, Login = "a", Password = "x", Nation = AccountNation.Karus });
        db.Accounts.Add(new Account { Id = AccountB, Login = "b", Password = "x", Nation = AccountNation.Karus });
        db.Characters.Add(new Character { Id = 10, AccountId = AccountA, Slot = 0, Name = "Zeus", Race = 1, Class = 101, Level = 60, MapId = 21 });
        db.Characters.Add(new Character { Id = 11, AccountId = AccountA, Slot = 1, Name = "Velkan", Race = 1, Class = 105, Level = 70, MapId = 21 });
    }, gameData =>
    {
        gameData.GetItem(Scroll).Returns(new ItemData { Num = Scroll, Kind = CharacterSealService.ScrollKind });
        gameData.GetItem(InventoryConstants.CypherRing).Returns(new ItemData { Num = InventoryConstants.CypherRing, Kind = CharacterSealService.RingKind, Duration = 1 });
    });

    private static UserSession Session(ServiceProvider provider, int characterId, int accountId, string name)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId, accountId);
        session.Name = name;
        session.Nation = AccountNation.Karus;
        session.VipPassword = Pin;
        for (int i = 0; i < session.Inventory.Length; i++) session.Inventory[i] = new ItemSlot();
        return session;
    }

    private static Packet SealRequest(string name, string code)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        packet.WriteByte(CharacterSealPacketWriter.SealCharacterSub);
        packet.WriteInt(-1);
        packet.WriteByte(BagPos);
        packet.WriteInt(Scroll);
        packet.WriteString(name);
        packet.WriteString(code);
        packet.ResetOffset();
        return packet;
    }

    private static Packet UnsealRequest(int ringId, byte slot)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        packet.WriteByte(CharacterSealPacketWriter.UnsealSub);
        packet.WriteInt(-1);
        packet.WriteByte(BagPos);
        packet.WriteInt(ringId);
        packet.WriteByte(slot);
        packet.ResetOffset();
        return packet;
    }

    private static Packet LastSent(UserSession session)
    {
        var packet = session.Client.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IClient.SendPacket))
            .Select(call => (Packet)call.GetArguments()[0]!)
            .Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_UPGRADE);
        packet.ResetOffset();
        return packet;
    }

    [Fact]
    public async Task SealingTurnsTheScrollIntoARingAndHidesTheCharacterFromSelect()
    {
        using var provider = Provider();
        var session = Session(provider, 10, AccountA, "Zeus");
        session.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId = Scroll;
        session.Inventory[InventoryConstants.InventoryStart + BagPos].Count = 1;

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(session, SealRequest("Velkan", Pin));

        var ring = session.Inventory[InventoryConstants.InventoryStart + BagPos];
        ring.ItemId.Should().Be(InventoryConstants.CypherRing);
        ring.UniqueId.Should().Be(11);
        ring.IsTradable.Should().BeTrue();
        ring.IsPetLink.Should().BeFalse();
        var reply = LastSent(session);
        reply.ReadByte().Should().Be(CharacterSealPacketWriter.SealSub);
        reply.ReadByte().Should().Be(CharacterSealPacketWriter.SealCharacterSub);
        reply.ReadByte().Should().Be(CharacterSealPacketWriter.Succeeded);
        using var scope = provider.CreateScope();
        var listed = await scope.ServiceProvider.GetRequiredService<ICharacterRepository>().GetCharactersByAccount(AccountA);
        listed.Select(c => c.Name).Should().Equal("Zeus");
    }

    [Fact]
    public async Task ARentedScrollSealsNoCharacter()
    {
        using var provider = Provider();
        var session = Session(provider, 10, AccountA, "Zeus");
        var scroll = session.Inventory[InventoryConstants.InventoryStart + BagPos];
        scroll.ItemId = Scroll;
        scroll.Count = 1;
        scroll.ExpireInHours(24, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(session, SealRequest("Velkan", Pin));

        scroll.ItemId.Should().Be(Scroll, "a rented ring would expire and leave its character sealed for good");
        using var scope = provider.CreateScope();
        var listed = await scope.ServiceProvider.GetRequiredService<ICharacterRepository>().GetCharactersByAccount(AccountA);
        listed.Select(c => c.Name).Should().Contain("Velkan");
    }

    [Fact]
    public async Task ASealedCharacterCannotBeSelectedByTheAccountThatSealedIt()
    {
        using var provider = Provider();
        var session = Session(provider, 10, AccountA, "Zeus");
        session.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId = Scroll;
        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(session, SealRequest("Velkan", Pin));

        using var scope = provider.CreateScope();
        var selected = await scope.ServiceProvider.GetRequiredService<IPreGameService>()
            .SelectCharacterAsync(AccountA, "a", "Velkan", 1);

        selected.CharacterId.Should().Be(0);
        selected.Packet.ReadByte().Should().NotBe((byte)SelectCharacterResult.Success);
    }

    [Fact]
    public async Task ASealedCharacterIsNotLoadedIntoTheGame()
    {
        using var provider = Provider();
        var session = Session(provider, 10, AccountA, "Zeus");
        session.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId = Scroll;
        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(session, SealRequest("Velkan", Pin));

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.AccountId.Returns(AccountA);
        client.CharacterId.Returns(11);
        using var scope = provider.CreateScope();

        var loaded = await scope.ServiceProvider.GetRequiredService<IGameSessionInitializer>().InitializeAsync(client);

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task AWrongVaultPasswordIsRefusedWithCode22()
    {
        using var provider = Provider();
        var session = Session(provider, 10, AccountA, "Zeus");
        session.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId = Scroll;

        await provider.GetRequiredService<ICharacterSealService>().HandleAsync(session, SealRequest("Velkan", "9999"));

        var reply = LastSent(session);
        reply.ReadByte();
        reply.ReadByte();
        reply.ReadByte().Should().Be(CharacterSealPacketWriter.Failed);
        reply.ReadByte().Should().Be((byte)ItemSealResult.WrongVaultPassword);
    }

    [Fact]
    public async Task UnsealingMovesTheCharacterIntoAnEmptySlotOfAnotherAccount()
    {
        using var provider = Provider();
        var sealer = Session(provider, 10, AccountA, "Zeus");
        sealer.Inventory[InventoryConstants.InventoryStart + BagPos].ItemId = Scroll;
        var seal = provider.GetRequiredService<ICharacterSealService>();
        await seal.HandleAsync(sealer, SealRequest("Velkan", Pin));

        var buyer = Session(provider, 20, AccountB, "Buyer");
        var ring = buyer.Inventory[InventoryConstants.InventoryStart + BagPos];
        ring.ItemId = InventoryConstants.CypherRing;
        ring.Count = 1;
        ring.UniqueId = 11;

        await seal.HandleAsync(buyer, UnsealRequest(InventoryConstants.CypherRing, 2));

        ring.IsEmpty.Should().BeTrue();
        using var scope = provider.CreateScope();
        var moved = await scope.ServiceProvider.GetRequiredService<ICharacterRepository>().GetById(11);
        moved!.AccountId.Should().Be(AccountB);
        moved.Slot.Should().Be(2);
        moved.Sealed.Should().BeFalse();
    }

    [Fact]
    public void ARingSummaryUsesThePetBlockShape()
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        CharacterSealPacketWriter.WriteSummary(packet, new CharacterSealPacketWriter.RingSummary("Velkan", 105, 70, 2500, 1));
        packet.ResetOffset();

        packet.ReadString().Should().Be("Velkan");
        packet.ReadByte().Should().Be(105);
        packet.ReadByte().Should().Be(70);
        packet.ReadUShort().Should().Be(2500);
        packet.ReadShort().Should().Be(1);
        packet.ReadByte().Should().Be(0);
    }

    [Fact]
    public void ARingOfferedInATradeCarriesItsUniqueIdAndSummary()
    {
        var ring = new CharacterSealPacketWriter.RingSummary("Velkan", 105, 70, 2500, 1, 2);
        var packet = ExchangePacketWriter.ItemOffered(ExchangePacketConstants.ExchangeOtherAdd, InventoryConstants.CypherRing, 1, 1, 11, ring);
        packet.ResetOffset();

        packet.ReadByte().Should().Be(ExchangePacketConstants.ExchangeOtherAdd);
        packet.ReadInt().Should().Be(InventoryConstants.CypherRing);
        packet.ReadInt().Should().Be(1);
        packet.ReadShort().Should().Be(1);
        packet.ReadInt().Should().Be(11);
        packet.ReadString().Should().Be("Velkan");
        packet.ReadByte().Should().Be(105);
        packet.ReadByte().Should().Be(70);
        packet.ReadUShort().Should().Be(2500);
        packet.ReadShort().Should().Be(1);
        packet.ReadByte().Should().Be(2);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void AnOrdinaryTradedItemKeepsAZeroExtension()
    {
        var packet = ExchangePacketWriter.Completed(ExchangePacketConstants.ExchangeDone, 0,
            [new ExchangePacketWriter.TransferredItem(4, Scroll, 1, 1, 11)]);
        packet.ResetOffset();

        packet.ReadByte();
        packet.ReadByte().Should().Be(ExchangePacketWriter.Succeeded);
        packet.ReadInt();
        packet.ReadUShort().Should().Be(1);
        packet.ReadByte().Should().Be(4);
        packet.ReadInt().Should().Be(Scroll);
        packet.ReadUShort();
        packet.ReadShort();
        packet.ReadByte();
        packet.ReadInt().Should().Be(0);
        packet.RemainingBytes.Should().Be(0);
    }
}
