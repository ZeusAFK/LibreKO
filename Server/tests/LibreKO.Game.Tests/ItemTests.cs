using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class ItemTests : GameTestBase
{
    [Fact]
    public async Task ItemPacketCoordinator_HandleMoveAsync_RejectsInvalidEquipmentSlot()
    {
        const int itemId = 600100;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Slot = 5,
                    Kind = 11,
                    Duration = 20
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 400, accountId: 410);
        session.Class = 101;
        session.Inventory[InventoryConstants.InventoryStart].ItemId = itemId;
        session.Inventory[InventoryConstants.InventoryStart].Durability = 20;
        session.Inventory[InventoryConstants.InventoryStart].Count = 1;

        var packet = new Packet(GameOpcodes.GS_ITEM_MOVE);
        packet.WriteByte(1);
        packet.WriteByte((byte)ItemMoveDirection.InventoryToSlot);
        packet.WriteInt(itemId);
        packet.WriteByte(0);
        packet.WriteByte((byte)InventoryConstants.RightHand);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleMoveAsync(client, packet);

        session.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(itemId);
        session.Inventory[InventoryConstants.RightHand].ItemId.Should().Be(0);

        var itemMovePacket = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_MOVE);
        itemMovePacket.ResetOffset();
        itemMovePacket.ReadByte().Should().Be(1);
        itemMovePacket.ReadByte().Should().Be(0);
    }

    [Fact]
    public async Task ItemPacketCoordinator_HandleMoveAsync_RemovesEquippedItemOpBuffOnUnequip()
    {
        const int itemId = 700100;
        const int skillId = 900001;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Slot = 1,
                    Kind = 21,
                    Duration = 30
                });
                gameData.GetItemOps(itemId).Returns(
                [
                    new ItemOpData
                    {
                        ItemId = itemId,
                        TriggerType = 3,
                        SkillId = skillId,
                        TriggerRate = 100
                    }
                ]);
                gameData.GetMagic(skillId).Returns(new MagicData
                {
                    Id = skillId,
                    Etc = 1
                });
                gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data>
                {
                    [1] = new()
                    {
                        Id = 1,
                        BuffType = (byte)BuffType.Damage,
                        Attack = 50,
                        Duration = 60
                    }
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 401, accountId: 411);
        session.Class = 101;
        session.Level = 20;
        session.Strength = 50;
        session.Stamina = 50;
        session.Dexterity = 50;
        session.Intelligence = 50;
        session.Magic = 50;
        session.ZoneId = 1;
        session.X = 10;
        session.Z = 10;
        session.Inventory[InventoryConstants.InventoryStart].ItemId = itemId;
        session.Inventory[InventoryConstants.InventoryStart].Durability = 30;
        session.Inventory[InventoryConstants.InventoryStart].Count = 1;
        sessionManager.Regions.AddToRegion(session);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();

        var equipPacket = new Packet(GameOpcodes.GS_ITEM_MOVE);
        equipPacket.WriteByte(1);
        equipPacket.WriteByte((byte)ItemMoveDirection.InventoryToSlot);
        equipPacket.WriteInt(itemId);
        equipPacket.WriteByte(0);
        equipPacket.WriteByte((byte)InventoryConstants.RightHand);
        await coordinator.HandleMoveAsync(client, equipPacket);

        session.ActiveBuffs.Should().ContainKey(skillId);
        var hitWithBuff = session.Stats.TotalHit;

        var unequipPacket = new Packet(GameOpcodes.GS_ITEM_MOVE);
        unequipPacket.WriteByte(1);
        unequipPacket.WriteByte((byte)ItemMoveDirection.SlotToInventory);
        unequipPacket.WriteInt(itemId);
        unequipPacket.WriteByte((byte)InventoryConstants.RightHand);
        unequipPacket.WriteByte(1);
        await coordinator.HandleMoveAsync(client, unequipPacket);

        session.ActiveBuffs.Should().NotContainKey(skillId);
        session.Stats.TotalHit.Should().BeLessThan(hitWithBuff);

        var magicPackets = sentPackets
            .Where(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_MAGIC_PROCESS)
            .ToList();
        magicPackets.Should().NotBeEmpty();
        magicPackets.Any(packet =>
        {
            packet.ResetOffset();
            return packet.ReadByte() == 5 && packet.ReadByte() == (byte)BuffType.Damage;
        }).Should().BeTrue();
    }

    [Fact]
    public async Task ItemPacketCoordinator_HandleMoveAsync_MapsClientLeftHandSlotForBowEquip()
    {
        const int itemId = 160210001;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Slot = 4,
                    Kind = 70,
                    Duration = 83
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 405, accountId: 415);
        session.ZoneId = 1;
        session.X = 10;
        session.Z = 10;
        session.Inventory[InventoryConstants.InventoryStart].ItemId = itemId;
        session.Inventory[InventoryConstants.InventoryStart].Durability = 83;
        session.Inventory[InventoryConstants.InventoryStart].Count = 1;
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_ITEM_MOVE);
        packet.WriteByte(1);
        packet.WriteByte((byte)ItemMoveDirection.InventoryToSlot);
        packet.WriteInt(itemId);
        packet.WriteByte(0);
        packet.WriteByte(GameConstants.ItemSlots.LeftHand);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleMoveAsync(client, packet);

        session.Inventory[InventoryConstants.InventoryStart].IsEmpty.Should().BeTrue();
        session.Inventory[InventoryConstants.LeftHand].ItemId.Should().Be(itemId);

        var itemMovePacket = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_MOVE);
        itemMovePacket.ResetOffset();
        itemMovePacket.ReadByte().Should().Be(1);
        itemMovePacket.ReadByte().Should().Be(1);
        sentPackets.Should().ContainSingle(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_WEIGHT_CHANGE);
    }

    [Fact]
    public async Task ItemPacketCoordinator_HandleMoveAsync_MapsClientLeftHandSlotForBowUnequip()
    {
        const int itemId = 160210001;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Slot = 4,
                    Kind = 70,
                    Duration = 83
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 406, accountId: 416);
        session.ZoneId = 1;
        session.X = 10;
        session.Z = 10;
        session.Inventory[InventoryConstants.LeftHand].ItemId = itemId;
        session.Inventory[InventoryConstants.LeftHand].Durability = 83;
        session.Inventory[InventoryConstants.LeftHand].Count = 1;
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_ITEM_MOVE);
        packet.WriteByte(1);
        packet.WriteByte((byte)ItemMoveDirection.SlotToInventory);
        packet.WriteInt(itemId);
        packet.WriteByte(GameConstants.ItemSlots.LeftHand);
        packet.WriteByte(0);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleMoveAsync(client, packet);

        session.Inventory[InventoryConstants.LeftHand].IsEmpty.Should().BeTrue();
        session.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(itemId);

        var itemMovePacket = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_MOVE);
        itemMovePacket.ResetOffset();
        itemMovePacket.ReadByte().Should().Be(1);
        itemMovePacket.ReadByte().Should().Be(1);
        sentPackets.Should().ContainSingle(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_WEIGHT_CHANGE);
    }

    [Fact]
    public async Task ItemPacketCoordinator_HandleMoveAsync_UsesTheEquipStatBlockWithoutATail()
    {
        const int itemId = 700150;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Slot = 1,
                    Kind = 21,
                    Duration = 30
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 4060, accountId: 4160);
        session.Class = 101;
        session.Level = 20;
        session.Strength = 50;
        session.Stamina = 50;
        session.Dexterity = 50;
        session.Intelligence = 50;
        session.Magic = 50;
        session.Inventory[InventoryConstants.InventoryStart].ItemId = itemId;
        session.Inventory[InventoryConstants.InventoryStart].Durability = 30;
        session.Inventory[InventoryConstants.InventoryStart].Count = 1;
        session.RecalculateStats(CreateBasicCoefficient(101), provider.GetRequiredService<IGameDataService>());

        var packet = new Packet(GameOpcodes.GS_ITEM_MOVE);
        packet.WriteByte(1);
        packet.WriteByte((byte)ItemMoveDirection.InventoryToSlot);
        packet.WriteInt(itemId);
        packet.WriteByte(0);
        packet.WriteByte((byte)InventoryConstants.RightHand);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleMoveAsync(client, packet);

        var itemMovePacket = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_MOVE);
        itemMovePacket.GetLength().Should().Be(38);
        itemMovePacket.ResetOffset();
        itemMovePacket.ReadByte().Should().Be(1);
        itemMovePacket.ReadByte().Should().Be(1);
        itemMovePacket.ReadShort();
        itemMovePacket.ReadShort();
        itemMovePacket.ReadInt();
        itemMovePacket.ReadByte().Should().Be(0);
        itemMovePacket.ReadByte().Should().Be(0);
        itemMovePacket.ReadShort();
        itemMovePacket.ReadShort();
        for (var i = 0; i < 5; i++)
            itemMovePacket.ReadShort();
        for (var i = 0; i < 6; i++)
            itemMovePacket.ReadShort();
        itemMovePacket.RemainingBytes.Should().Be(0);
        sentPackets.Should().ContainSingle(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_WEIGHT_CHANGE);
    }

    [Fact]
    public async Task ItemPacketCoordinator_HandleMoveAsync_ArrangeRequestSortsTheBagAndSendsEverySlot()
    {
        const int firstItemId = 700160;
        const int secondItemId = 700170;

        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 4061, accountId: 4161);
        session.Inventory[InventoryConstants.InventoryStart].ItemId = firstItemId;
        session.Inventory[InventoryConstants.InventoryStart].Durability = 20;
        session.Inventory[InventoryConstants.InventoryStart].Count = 2;
        session.Inventory[InventoryConstants.InventoryStart].Flag = 100;
        session.Inventory[InventoryConstants.InventoryStart + 5].ItemId = secondItemId;
        session.Inventory[InventoryConstants.InventoryStart + 5].Durability = 15;
        session.Inventory[InventoryConstants.InventoryStart + 5].Count = 7;

        var packet = new Packet(GameOpcodes.GS_ITEM_MOVE);
        packet.WriteByte(2);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleMoveAsync(client, packet);

        var refreshPacket = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_MOVE);
        refreshPacket.GetLength().Should().Be(534);
        refreshPacket.ResetOffset();
        refreshPacket.ReadByte().Should().Be(2);
        refreshPacket.ReadByte().Should().Be(1);

        for (var slot = 0; slot < InventoryConstants.HaveMax; slot++)
        {
            var itemId = refreshPacket.ReadInt();
            var durability = refreshPacket.ReadUShort();
            var count = refreshPacket.ReadUShort();
            var flag = refreshPacket.ReadByte();
            var rentalTime = refreshPacket.ReadUShort();
            var uniqueId = refreshPacket.ReadInt();
            var expirationTime = refreshPacket.ReadInt();

            if (slot == 0)
            {
                itemId.Should().Be(secondItemId, because: "the higher id sorts first");
                durability.Should().Be(15);
                count.Should().Be(7);
                flag.Should().Be(0);
            }
            else if (slot == 1)
            {
                itemId.Should().Be(
                    firstItemId,
                    because: "arranging closes the gap the item used to sit behind");
                durability.Should().Be(20);
                count.Should().Be(2);
                flag.Should().Be(100);
            }
            else
            {
                itemId.Should().Be(0);
                durability.Should().Be(0);
                count.Should().Be(0);
                flag.Should().Be(0);
            }

            rentalTime.Should().Be(0);
            uniqueId.Should().Be(0);
            expirationTime.Should().Be(0);
        }

        refreshPacket.RemainingBytes.Should().Be(0);

        session.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(secondItemId);
        session.Inventory[InventoryConstants.InventoryStart + 1].ItemId.Should().Be(firstItemId);
        session.Inventory[InventoryConstants.InventoryStart + 5].ItemId.Should().Be(0);
    }

    [Fact]
    public async Task ItemPacketCoordinator_HandleMoveAsync_ArrangeIsRefusedWhileGathering()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 4062, accountId: 4162);
        session.Inventory[InventoryConstants.InventoryStart + 5].ItemId = 700170;
        session.IsMining = true;

        var packet = new Packet(GameOpcodes.GS_ITEM_MOVE);
        packet.WriteByte(2);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleMoveAsync(client, packet);

        var reply = sentPackets.Single(sent => sent.GetOpcode() == (byte)GameOpcodes.GS_ITEM_MOVE);
        reply.ResetOffset();
        reply.ReadByte().Should().Be((byte)ItemMoveSubOpcode.ArrangeInventory);
        reply.ReadByte().Should().Be(
            (byte)ArrangeResult.Failed,
            because: "the client shows its own failure message for a zero here");
        reply.RemainingBytes.Should().Be(0);

        session.Inventory[InventoryConstants.InventoryStart + 5].ItemId.Should().Be(
            700170, because: "a refused arrange must not move anything");
    }

    [Fact]
    public async Task ItemPacketCoordinator_HandleMoveAsync_MapsCospreVisualSlotForLookChange()
    {
        const int itemId = 700200;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Slot = 110,
                    Kind = 11,
                    Duration = 20
                });
            });

        var actorClient = Substitute.For<IClient>();
        actorClient.Id.Returns(Guid.NewGuid());
        actorClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var observerClient = Substitute.For<IClient>();
        observerClient.Id.Returns(Guid.NewGuid());
        var observerPackets = new List<Packet>();
        observerClient.SendPacket(Arg.Do<Packet>(packet => observerPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var actor = sessionManager.CreateSession(actorClient, characterId: 410, accountId: 420);
        actor.Class = 101;
        actor.ZoneId = 1;
        actor.X = 10;
        actor.Z = 10;
        actor.Inventory[InventoryConstants.InventoryStart].ItemId = itemId;
        actor.Inventory[InventoryConstants.InventoryStart].Durability = 20;
        actor.Inventory[InventoryConstants.InventoryStart].Count = 1;
        sessionManager.Regions.AddToRegion(actor);

        var observer = sessionManager.CreateSession(observerClient, characterId: 411, accountId: 421);
        observer.ZoneId = 1;
        observer.X = 10;
        observer.Z = 10;
        sessionManager.Regions.AddToRegion(observer);

        var packet = new Packet(GameOpcodes.GS_ITEM_MOVE);
        packet.WriteByte(1);
        packet.WriteByte((byte)ItemMoveDirection.InventoryToCospre);
        packet.WriteInt(itemId);
        packet.WriteByte(0);
        packet.WriteByte(0);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleMoveAsync(actorClient, packet);

        actor.Inventory[InventoryConstants.CospreStart].ItemId.Should().Be(itemId);

        var lookChange = observerPackets.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_USERLOOK_CHANGE);
        lookChange.ResetOffset();
        // uint32 socket id, uint8 pos, uint32 itemId, ...
        lookChange.ReadInt().Should().Be(actor.CharacterId);
        lookChange.ReadByte().Should().Be((byte)InventoryConstants.CosWing);
        lookChange.ReadInt().Should().Be(itemId);
    }

    [Fact]
    public async Task ItemPacketCoordinator_HandleMoveAsync_AllowsUnequippingEmptyBag()
    {
        const int itemId = 700201;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Slot = 25,
                    Kind = 11,
                    Duration = 10
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 412, accountId: 422);
        session.Class = 101;
        var bagSlot = InventoryConstants.BagSlotFor(0);
        session.Inventory[bagSlot].ItemId = itemId;
        session.Inventory[bagSlot].Durability = 10;
        session.Inventory[bagSlot].Count = 1;

        var packet = new Packet(GameOpcodes.GS_ITEM_MOVE);
        packet.WriteByte(1);
        packet.WriteByte((byte)ItemMoveDirection.BagSlotToInventory);
        packet.WriteInt(itemId);
        packet.WriteByte(0);
        packet.WriteByte(0);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleMoveAsync(client, packet);

        session.Inventory[bagSlot].Should().Match<ItemSlot>(slot => slot.IsEmpty);
        session.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(itemId);

        var itemMovePacket = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_MOVE);
        itemMovePacket.ResetOffset();
        itemMovePacket.ReadByte().Should().Be(1);
        itemMovePacket.ReadByte().Should().Be(1);
        sentPackets.Should().ContainSingle(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_WEIGHT_CHANGE);
    }

    [Theory]
    [InlineData(110, InventoryConstants.CosPosWing)]
    [InlineData(107, InventoryConstants.CosPosHelmet)]
    [InlineData(101, InventoryConstants.CosPosGloveRight)]
    [InlineData(102, InventoryConstants.CosPosGloveLeft)]
    [InlineData(105, InventoryConstants.CosPosPauldron)]
    [InlineData(114, InventoryConstants.CosPosEmblem)]
    [InlineData(111, InventoryConstants.CosPosFairy)]
    [InlineData(112, InventoryConstants.CosPosTattoo)]
    [InlineData(127, InventoryConstants.CosPosTattoo)]
    [InlineData(113, InventoryConstants.CosPosTalisman)]
    public async Task ItemPacketCoordinator_HandleMoveAsync_AcceptsCospreItemOnlyInItsRetailSlot(
        int slotCode, int cospreSlot)
    {
        const int itemId = 700240;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Slot = (byte)slotCode,
                    Kind = 11,
                    Duration = 20
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 460, accountId: 470);
        session.Class = 101;
        session.Inventory[InventoryConstants.InventoryStart].ItemId = itemId;
        session.Inventory[InventoryConstants.InventoryStart].Durability = 20;
        session.Inventory[InventoryConstants.InventoryStart].Count = 1;

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();

        var wrongSlot = (byte)(cospreSlot == InventoryConstants.CosPosWing
            ? InventoryConstants.CosPosTalisman
            : InventoryConstants.CosPosWing);
        await coordinator.HandleMoveAsync(client, BuildCospreMove(itemId, wrongSlot));
        session.Inventory[InventoryConstants.CospreStart + wrongSlot].Should().Match<ItemSlot>(s => s.IsEmpty);

        await coordinator.HandleMoveAsync(client, BuildCospreMove(itemId, (byte)cospreSlot));
        session.Inventory[InventoryConstants.CospreStart + cospreSlot].ItemId.Should().Be(itemId);
    }

    private static Packet BuildCospreMove(int itemId, byte destinationPosition)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_MOVE);
        packet.WriteByte(1);
        packet.WriteByte((byte)ItemMoveDirection.InventoryToCospre);
        packet.WriteInt(itemId);
        packet.WriteByte(0);
        packet.WriteByte(destinationPosition);
        return packet;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ItemPacketCoordinator_HandleMoveAsync_MagicBagSlotsNeedTheMatchingBagEquipped(int bagIndex)
    {
        const int bagItemId = 700011;
        const int storedItemId = 700241;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
                gameData.GetItem(bagItemId).Returns(new ItemData
                {
                    Num = bagItemId, Slot = 25, Kind = 11, Duration = 10
                });
                gameData.GetItem(storedItemId).Returns(new ItemData
                {
                    Num = storedItemId, Slot = 15, Kind = 21, Duration = 10
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 461, accountId: 471);
        session.Class = 101;
        session.Inventory[InventoryConstants.InventoryStart].ItemId = storedItemId;
        session.Inventory[InventoryConstants.InventoryStart].Durability = 10;
        session.Inventory[InventoryConstants.InventoryStart].Count = 1;

        var bagPosition = (byte)(bagIndex * InventoryConstants.MagicBagMax);
        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();

        await coordinator.HandleMoveAsync(client, BuildMagicBagMove(storedItemId, bagPosition));
        session.Inventory[InventoryConstants.MagicBagStart + bagPosition]
            .Should().Match<ItemSlot>(s => s.IsEmpty);

        session.Inventory[InventoryConstants.BagSlotFor(bagIndex)].ItemId = bagItemId;
        session.Inventory[InventoryConstants.BagSlotFor(bagIndex)].Count = 1;

        await coordinator.HandleMoveAsync(client, BuildMagicBagMove(storedItemId, bagPosition));
        session.Inventory[InventoryConstants.MagicBagStart + bagPosition].ItemId.Should().Be(storedItemId);
    }

    private static Packet BuildMagicBagMove(int itemId, byte destinationPosition)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_MOVE);
        packet.WriteByte(1);
        packet.WriteByte((byte)ItemMoveDirection.InventoryToMagicBag);
        packet.WriteInt(itemId);
        packet.WriteByte(0);
        packet.WriteByte(destinationPosition);
        return packet;
    }

    [Fact]
    public async Task ItemPacketCoordinator_HandleMoveAsync_RejectsUnequippingBagThatStillHoldsItems()
    {
        const int bagItemId = 700011;
        const int storedItemId = 700242;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
                gameData.GetItem(bagItemId).Returns(new ItemData
                {
                    Num = bagItemId, Slot = 25, Kind = 11, Duration = 10
                });
                gameData.GetItem(storedItemId).Returns(new ItemData
                {
                    Num = storedItemId, Slot = 15, Kind = 21, Duration = 10
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 462, accountId: 472);
        session.Class = 101;
        var bagSlot = InventoryConstants.BagSlotFor(1);
        session.Inventory[bagSlot].ItemId = bagItemId;
        session.Inventory[bagSlot].Count = 1;
        var contents = InventoryConstants.MagicBagPageStart(1);
        session.Inventory[contents].ItemId = storedItemId;
        session.Inventory[contents].Count = 1;

        var packet = new Packet(GameOpcodes.GS_ITEM_MOVE);
        packet.WriteByte(1);
        packet.WriteByte((byte)ItemMoveDirection.BagSlotToInventory);
        packet.WriteInt(bagItemId);
        packet.WriteByte(1);
        packet.WriteByte(1);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleMoveAsync(client, packet);

        session.Inventory[bagSlot].ItemId.Should().Be(bagItemId);
    }

    [Fact]
    public async Task ItemPacketCoordinator_HandleMoveAsync_RejectsMoveWhileMining()
    {
        const int itemId = 700202;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Slot = 1,
                    Kind = 21,
                    Duration = 30
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 413, accountId: 423);
        session.IsMining = true;
        session.Inventory[InventoryConstants.InventoryStart].ItemId = itemId;
        session.Inventory[InventoryConstants.InventoryStart].Durability = 30;
        session.Inventory[InventoryConstants.InventoryStart].Count = 1;

        var packet = new Packet(GameOpcodes.GS_ITEM_MOVE);
        packet.WriteByte(1);
        packet.WriteByte((byte)ItemMoveDirection.InventoryToSlot);
        packet.WriteInt(itemId);
        packet.WriteByte(0);
        packet.WriteByte((byte)InventoryConstants.RightHand);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleMoveAsync(client, packet);

        session.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(itemId);
        session.Inventory[InventoryConstants.RightHand].ItemId.Should().Be(0);

        sentPacket.Should().NotBeNull();
        sentPacket!.ResetOffset();
        sentPacket.ReadByte().Should().Be(1);
        sentPacket.ReadByte().Should().Be(0);
    }

    [Fact]
    public async Task ItemPacketCoordinator_HandleRemoveAsync_RejectsRemoveWhileMining()
    {
        const int itemId = 700203;

        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 414, accountId: 424);
        session.IsMining = true;
        session.Inventory[InventoryConstants.InventoryStart].ItemId = itemId;
        session.Inventory[InventoryConstants.InventoryStart].Durability = 10;
        session.Inventory[InventoryConstants.InventoryStart].Count = 1;

        var packet = new Packet(GameOpcodes.GS_ITEM_REMOVE);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteInt(itemId);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleRemoveAsync(client, packet);

        session.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(itemId);

        sentPacket.Should().NotBeNull();
        sentPacket!.ResetOffset();
        sentPacket.ReadByte().Should().Be(0);
    }

    [Fact]
    public async Task ItemPacketCoordinator_HandleRemoveAsync_MapsClientLeftHandSlot()
    {
        const int itemId = 160210001;

        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 4140, accountId: 4240);
        session.Inventory[InventoryConstants.LeftHand].ItemId = itemId;
        session.Inventory[InventoryConstants.LeftHand].Durability = 83;
        session.Inventory[InventoryConstants.LeftHand].Count = 1;

        var packet = new Packet(GameOpcodes.GS_ITEM_REMOVE);
        packet.WriteByte(1);
        packet.WriteByte(GameConstants.ItemSlots.LeftHand);
        packet.WriteInt(itemId);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleRemoveAsync(client, packet);

        session.Inventory[InventoryConstants.LeftHand].IsEmpty.Should().BeTrue();

        sentPacket.Should().NotBeNull();
        sentPacket!.ResetOffset();
        sentPacket.ReadByte().Should().Be(1);
    }

    [Fact]
    public async Task ItemPacketCoordinator_HandleRemoveAsync_RemovesEquippedItemOpBuffOnTrash()
    {
        const int itemId = 700204;
        const int skillId = 900002;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
                gameData.GetItemOps(itemId).Returns(
                [
                    new ItemOpData
                    {
                        ItemId = itemId,
                        TriggerType = 3,
                        SkillId = skillId,
                        TriggerRate = 100
                    }
                ]);
                gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data>
                {
                    [1] = new()
                    {
                        Id = 1,
                        Attack = 25,
                        Duration = 60
                    }
                });
                gameData.GetMagic(skillId).Returns(new MagicData
                {
                    Id = skillId,
                    Etc = 1
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 415, accountId: 425);
        session.Class = 101;
        session.Level = 20;
        session.Strength = 50;
        session.Stamina = 50;
        session.Dexterity = 50;
        session.Intelligence = 50;
        session.Magic = 50;
        session.Inventory[InventoryConstants.RightHand].ItemId = itemId;
        session.Inventory[InventoryConstants.RightHand].Durability = 30;
        session.Inventory[InventoryConstants.RightHand].Count = 1;
        session.ActiveBuffs[skillId] = new ActiveBuff
        {
            MagicId = skillId,
            BonusAttack = 25,
            Duration = 60,
            ExpireTicks = DateTime.UtcNow.AddMinutes(1).Ticks
        };
        session.RecalculateStats(CreateBasicCoefficient(101), provider.GetRequiredService<IGameDataService>());
        session.RecalculateStatsWithBuffs(provider.GetRequiredService<IGameDataService>());

        var packet = new Packet(GameOpcodes.GS_ITEM_REMOVE);
        packet.WriteByte(1);
        packet.WriteByte((byte)InventoryConstants.RightHand);
        packet.WriteInt(itemId);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleRemoveAsync(client, packet);

        session.Inventory[InventoryConstants.RightHand].IsEmpty.Should().BeTrue();
        session.ActiveBuffs.Should().NotContainKey(skillId);
    }

    [Fact]
    public async Task ItemPacketCoordinator_HandleTradeAsync_SellsSaleTypeLowItemsAtBuyPriceOverSix()
    {
        const int itemId = 700202;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    SellingGroup = 1,
                    BuyPrice = 1200,
                    SellPrice = 150,
                    Countable = 1
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 413, accountId: 423);
        session.Hp = 100;
        session.ZoneId = 1;
        session.X = 10;
        session.Z = 10;
        session.Inventory[InventoryConstants.InventoryStart].ItemId = itemId;
        session.Inventory[InventoryConstants.InventoryStart].Durability = 20;
        session.Inventory[InventoryConstants.InventoryStart].Count = 2;

        var npc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = 5000,
            SellingGroup = 1,
            ZoneId = 1,
            X = 10,
            Z = 10,
            MaxHp = 1,
            Hp = 1,
            NpcType = 11
        });

        var packet = new Packet(GameOpcodes.GS_ITEM_TRADE);
        packet.WriteByte(2);
        packet.WriteInt(1);
        packet.WriteInt(npc.UniqueId);
        packet.WriteByte(1);
        packet.WriteInt(itemId);
        packet.WriteByte(0);
        packet.WriteUShort(2);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleTradeAsync(client, packet);

        session.Money.Should().Be(400);

        var reply = sentPackets.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_TRADE);
        reply.ResetOffset();
        reply.ReadByte().Should().Be(1);
        reply.ReadInt().Should().Be(400);
        reply.ReadInt().Should().Be(400);
    }

    [Fact]
    public async Task ItemPacketCoordinator_HandleTradeAsync_SellsSaleTypeFullItemsAtFullPrice()
    {
        const int baseItemId = 379110000;
        const int itemId = 379110002;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(baseItemId).Returns(new ItemData
                {
                    Num = baseItemId,
                    SellingGroup = 1,
                    BuyPrice = 10000,
                    SellPrice = 1,
                    Countable = 1
                });
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    SellingGroup = 1,
                    BuyPrice = 30000,
                    SellPrice = 3,
                    Countable = 1
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 414, accountId: 424);
        session.Hp = 100;
        session.ZoneId = 1;
        session.X = 10;
        session.Z = 10;
        session.Inventory[InventoryConstants.InventoryStart].ItemId = itemId;
        session.Inventory[InventoryConstants.InventoryStart].Durability = 20;
        session.Inventory[InventoryConstants.InventoryStart].Count = 1;

        var npc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = 5002,
            SellingGroup = 1,
            ZoneId = 1,
            X = 10,
            Z = 10,
            MaxHp = 1,
            Hp = 1,
            NpcType = 11
        });

        var packet = new Packet(GameOpcodes.GS_ITEM_TRADE);
        packet.WriteByte(2);
        packet.WriteInt(1);
        packet.WriteInt(npc.UniqueId);
        packet.WriteByte(1);
        packet.WriteInt(itemId);
        packet.WriteByte(0);
        packet.WriteUShort(1);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleTradeAsync(client, packet);

        session.Money.Should().Be(30000);
    }

    [Fact]
    public async Task ItemPacketCoordinator_HandleTradeAsync_BuyRecalculatesAndSendsWeightChange()
    {
        const int itemId = 700205;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    SellingGroup = 1,
                    BuyPrice = 100,
                    Countable = 1,
                    Duration = 20,
                    Weight = 15
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 416, accountId: 426);
        session.Class = 101;
        session.Level = 20;
        session.Strength = 50;
        session.Hp = 100;
        session.ZoneId = 1;
        session.X = 10;
        session.Z = 10;
        session.Money = 1_000;
        session.RecalculateStats(CreateBasicCoefficient(101), provider.GetRequiredService<IGameDataService>());

        var npc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = 5001,
            SellingGroup = 1,
            ZoneId = 1,
            X = 10,
            Z = 10,
            MaxHp = 1,
            Hp = 1,
            NpcType = 11
        });

        var packet = new Packet(GameOpcodes.GS_ITEM_TRADE);
        packet.WriteByte(1);
        packet.WriteInt(1);
        packet.WriteInt(npc.UniqueId);
        packet.WriteByte(1);
        packet.WriteInt(itemId);
        packet.WriteByte(0);
        packet.WriteUShort(2);
        packet.WriteByte(0);
        packet.WriteByte(0);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleTradeAsync(client, packet);

        session.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(itemId);
        session.Inventory[InventoryConstants.InventoryStart].Count.Should().Be(2);
        session.Stats.ItemWeight.Should().Be(30);

        var tradePacket = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_TRADE);
        tradePacket.ResetOffset();
        tradePacket.ReadByte().Should().Be(1);
        tradePacket.ReadInt().Should().Be(800);
        tradePacket.ReadInt().Should().Be(200);

        sentPackets.Should().NotContain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_COUNT_CHANGE);

        var weightPacket = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_WEIGHT_CHANGE);
        weightPacket.ResetOffset();
        weightPacket.ReadInt().Should().Be(30);
    }

    private const int UpgradeAnvilId = 500;
    private const int HighClassScroll = 379021000;
    private const int MiddleClassScroll = 379205000;
    private const int TrinaPiece = 700002000;

    private static ItemData UpgradeItem(int num, short itemType = 5, short itemClass = 3, short duration = 15)
        => new() { Num = num, Kind = 52, ItemType = (byte)itemType, ItemClass = itemClass, Duration = duration };

    private static Packet UpgradeRequest(byte subOpcode, byte upgradeType, params (int ItemId, byte Position)[] slots)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        packet.WriteByte(subOpcode);
        packet.WriteByte(upgradeType);
        packet.WriteInt(UpgradeAnvilId);
        for (var index = 0; index < 10; index++)
        {
            if (index < slots.Length)
            {
                packet.WriteInt(slots[index].ItemId);
                packet.WriteByte(slots[index].Position);
            }
            else
            {
                packet.WriteInt(0);
                packet.WriteByte(byte.MaxValue);
            }
        }

        return packet;
    }

    private static UserSession CreateUpgradeSession(
        ServiceProvider provider,
        IClient client,
        string name,
        params int[] inventory)
    {
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 402, accountId: 412);
        session.Name = name;
        session.ZoneId = 1;
        session.X = 20;
        session.Z = 20;
        session.Hp = 100;
        session.Money = 1_000_000;
        sessionManager.Maps = CreateMapManagerWithObjectEvent(1, new ObjectEvent
        {
            Index = (short)UpgradeAnvilId,
            Type = 8,
            Status = 1,
            PosX = 20,
            PosZ = 20
        });

        for (var index = 0; index < inventory.Length; index++)
        {
            var slot = session.Inventory[InventoryConstants.InventoryStart + index];
            slot.ItemId = inventory[index];
            slot.Count = 1;
            slot.Durability = 15;
        }

        sessionManager.Regions.AddToRegion(session);
        return session;
    }

    private static IClient CreateRecordingClient(List<Packet> sentPackets)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Do<Packet>(sentPackets.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return client;
    }

    private static (byte Sub, byte Type, byte Result, int ItemId, byte Position) ReadUpgradeReply(List<Packet> sentPackets)
    {
        var reply = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_UPGRADE);
        reply.ResetOffset();
        return (reply.ReadByte(), reply.ReadByte(), reply.ReadByte(), reply.ReadInt(), reply.ReadByte());
    }

    [Fact]
    public async Task HandleUpgradeAsync_AppliesRecipeTargetAndSettingsCost()
    {
        const int originItemId = 156210008;
        const int upgradedItemId = 156210009;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(originItemId).Returns(UpgradeItem(originItemId));
                gameData.GetItem(upgradedItemId).Returns(UpgradeItem(upgradedItemId, duration: 20));
                gameData.GetUpgradeRecipe(originItemId, HighClassScroll).Returns(new ItemUpgradeRecipeData
                {
                    Index = 409824,
                    OriginNumber = originItemId,
                    NewNumber = upgradedItemId,
                    RequiredItem = HighClassScroll,
                    Grade = 8
                });
                gameData.GetUpgradeSetting(5, 8, HighClassScroll, 0).Returns(new ItemUpgradeSettingsData
                {
                    Index = 24,
                    ReqItem1 = HighClassScroll,
                    ItemType = 5,
                    ItemGrade = 8,
                    ReqNoah = 150_000,
                    SuccessRate = 10000
                });
            });

        var sentPackets = new List<Packet>();
        var client = CreateRecordingClient(sentPackets);
        var session = CreateUpgradeSession(provider, client, "Rin", originItemId, HighClassScroll);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleUpgradeAsync(client, UpgradeRequest(2, 1, (originItemId, 0), (HighClassScroll, 1)));

        session.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(upgradedItemId);
        session.Inventory[InventoryConstants.InventoryStart].Durability.Should().Be(20);
        session.Inventory[InventoryConstants.InventoryStart + 1].Should().Match<ItemSlot>(slot => slot.IsEmpty);
        session.Money.Should().Be(850_000);

        ReadUpgradeReply(sentPackets).Should().Be(((byte)2, (byte)1, (byte)1, upgradedItemId, (byte)0));

        sentPackets.Any(packet =>
        {
            if (packet.GetOpcode() != (byte)GameOpcodes.GS_OBJECT_EVENT)
                return false;

            packet.ResetOffset();
            return packet.ReadByte() == 8 && packet.ReadByte() == 1 && packet.ReadInt() == UpgradeAnvilId;
        }).Should().BeTrue();
    }

    [Fact]
    public async Task HandleUpgradeAsync_PreviewReportsTargetWithoutSpendingAnything()
    {
        const int originItemId = 156210008;
        const int upgradedItemId = 156210009;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(originItemId).Returns(UpgradeItem(originItemId));
                gameData.GetItem(upgradedItemId).Returns(UpgradeItem(upgradedItemId, duration: 20));
                gameData.GetUpgradeRecipe(originItemId, HighClassScroll).Returns(new ItemUpgradeRecipeData
                {
                    OriginNumber = originItemId,
                    NewNumber = upgradedItemId,
                    RequiredItem = HighClassScroll
                });
                gameData.GetUpgradeSetting(5, 8, HighClassScroll, 0).Returns(new ItemUpgradeSettingsData
                {
                    ItemType = 5,
                    ItemGrade = 8,
                    ReqNoah = 150_000,
                    SuccessRate = 300
                });
            });

        var sentPackets = new List<Packet>();
        var client = CreateRecordingClient(sentPackets);
        var session = CreateUpgradeSession(provider, client, "Rin", originItemId, HighClassScroll);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleUpgradeAsync(client, UpgradeRequest(2, 2, (originItemId, 0), (HighClassScroll, 1)));

        session.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(originItemId);
        session.Inventory[InventoryConstants.InventoryStart + 1].ItemId.Should().Be(HighClassScroll);
        session.Money.Should().Be(1_000_000);

        ReadUpgradeReply(sentPackets).Should().Be(((byte)2, (byte)2, (byte)1, upgradedItemId, (byte)0));
        sentPackets.Should().NotContain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_OBJECT_EVENT);
    }

    [Fact]
    public async Task HandleUpgradeAsync_ReportsNoMatchWhenTheRecipeIsMissing()
    {
        const int originItemId = 156210010;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(originItemId).Returns(UpgradeItem(originItemId));
                gameData.GetUpgradeRecipe(originItemId, HighClassScroll).Returns((ItemUpgradeRecipeData?)null);
            });

        var sentPackets = new List<Packet>();
        var client = CreateRecordingClient(sentPackets);
        var session = CreateUpgradeSession(provider, client, "Rin", originItemId, HighClassScroll);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleUpgradeAsync(client, UpgradeRequest(2, 1, (originItemId, 0), (HighClassScroll, 1)));

        session.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(originItemId);
        session.Inventory[InventoryConstants.InventoryStart + 1].ItemId.Should().Be(HighClassScroll);
        session.Money.Should().Be(1_000_000);

        ReadUpgradeReply(sentPackets).Should().Be(((byte)2, (byte)1, (byte)4, originItemId, (byte)0));
    }

    [Fact]
    public async Task HandleUpgradeAsync_RejectsAScrollBelowTheItemClass()
    {
        const int originItemId = 156210008;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(originItemId).Returns(UpgradeItem(originItemId));
                gameData.GetUpgradeRecipe(originItemId, MiddleClassScroll).Returns(new ItemUpgradeRecipeData
                {
                    OriginNumber = originItemId,
                    NewNumber = 156210009,
                    RequiredItem = MiddleClassScroll
                });
            });

        var sentPackets = new List<Packet>();
        var client = CreateRecordingClient(sentPackets);
        var session = CreateUpgradeSession(provider, client, "Rin", originItemId, MiddleClassScroll);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleUpgradeAsync(client, UpgradeRequest(2, 1, (originItemId, 0), (MiddleClassScroll, 1)));

        session.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(originItemId);
        ReadUpgradeReply(sentPackets).Result.Should().Be(4);
        provider.GetRequiredService<IGameDataService>()
            .Received(0)
            .GetUpgradeSetting(Arg.Any<short>(), Arg.Any<short>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public async Task HandleUpgradeAsync_ReportsNoMatchWhenTheSettingsRateIsZero()
    {
        const int originItemId = 156210008;
        const int upgradedItemId = 156210009;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(originItemId).Returns(UpgradeItem(originItemId));
                gameData.GetItem(upgradedItemId).Returns(UpgradeItem(upgradedItemId));
                gameData.GetUpgradeRecipe(originItemId, HighClassScroll).Returns(new ItemUpgradeRecipeData
                {
                    OriginNumber = originItemId,
                    NewNumber = upgradedItemId,
                    RequiredItem = HighClassScroll
                });
                gameData.GetUpgradeSetting(5, 8, HighClassScroll, 0).Returns(new ItemUpgradeSettingsData
                {
                    ItemType = 5,
                    ItemGrade = 8,
                    ReqNoah = 300_000,
                    SuccessRate = 0
                });
            });

        var sentPackets = new List<Packet>();
        var client = CreateRecordingClient(sentPackets);
        var session = CreateUpgradeSession(provider, client, "Rin", originItemId, HighClassScroll);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleUpgradeAsync(client, UpgradeRequest(2, 1, (originItemId, 0), (HighClassScroll, 1)));

        session.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(originItemId);
        session.Money.Should().Be(1_000_000);
        ReadUpgradeReply(sentPackets).Result.Should().Be(4);
    }

    [Fact]
    public async Task HandleUpgradeAsync_TrinaPieceSelectsTheProtectedRateRow()
    {
        const int originItemId = 156210008;
        const int upgradedItemId = 156210009;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(originItemId).Returns(UpgradeItem(originItemId));
                gameData.GetItem(upgradedItemId).Returns(UpgradeItem(upgradedItemId, duration: 20));
                gameData.GetUpgradeRecipe(originItemId, HighClassScroll).Returns(new ItemUpgradeRecipeData
                {
                    OriginNumber = originItemId,
                    NewNumber = upgradedItemId,
                    RequiredItem = HighClassScroll
                });
                gameData.GetUpgradeSetting(5, 8, HighClassScroll, TrinaPiece).Returns(new ItemUpgradeSettingsData
                {
                    Index = 33,
                    ReqItem1 = TrinaPiece,
                    ReqItem2 = HighClassScroll,
                    ItemType = 5,
                    ItemGrade = 8,
                    ReqNoah = 150_000,
                    SuccessRate = 10000
                });
            });

        var sentPackets = new List<Packet>();
        var client = CreateRecordingClient(sentPackets);
        var session = CreateUpgradeSession(provider, client, "Rin", originItemId, HighClassScroll, TrinaPiece);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleUpgradeAsync(
            client, UpgradeRequest(2, 1, (originItemId, 0), (HighClassScroll, 1), (TrinaPiece, 2)));

        session.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(upgradedItemId);
        session.Inventory[InventoryConstants.InventoryStart + 1].Should().Match<ItemSlot>(slot => slot.IsEmpty);
        session.Inventory[InventoryConstants.InventoryStart + 2].Should().Match<ItemSlot>(slot => slot.IsEmpty);
        ReadUpgradeReply(sentPackets).Result.Should().Be(1);
    }

    [Fact]
    public async Task HandleUpgradeAsync_RejectsTwoProtectionMaterials()
    {
        const int originItemId = 156210008;
        const int karivdis = 379258000;

        using var provider = CreateProvider(
            _ => { },
            gameData => gameData.GetItem(originItemId).Returns(UpgradeItem(originItemId)));

        var sentPackets = new List<Packet>();
        var client = CreateRecordingClient(sentPackets);
        var session = CreateUpgradeSession(
            provider, client, "Rin", originItemId, HighClassScroll, TrinaPiece, karivdis);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleUpgradeAsync(
            client, UpgradeRequest(2, 1, (originItemId, 0), (HighClassScroll, 1), (TrinaPiece, 2), (karivdis, 3)));

        session.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(originItemId);
        ReadUpgradeReply(sentPackets).Result.Should().Be(4);
    }

    [Fact]
    public async Task HandleUpgradeAsync_FailedRollDestroysTheItemAndStillChargesGold()
    {
        const int originItemId = 156210008;
        const int upgradedItemId = 156210009;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(originItemId).Returns(UpgradeItem(originItemId));
                gameData.GetItem(upgradedItemId).Returns(UpgradeItem(upgradedItemId));
                gameData.GetUpgradeRecipe(originItemId, HighClassScroll).Returns(new ItemUpgradeRecipeData
                {
                    OriginNumber = originItemId,
                    NewNumber = upgradedItemId,
                    RequiredItem = HighClassScroll
                });
                gameData.GetUpgradeSetting(5, 8, HighClassScroll, 0).Returns(new ItemUpgradeSettingsData
                {
                    ItemType = 5,
                    ItemGrade = 8,
                    ReqNoah = 150_000,
                    SuccessRate = 10000
                });
            },
            settings =>
            {
                settings.Global.Anvil.Enabled = true;
                settings.Global.Anvil.MaxRateSwingPercent = 100;
                settings.Global.Anvil.SuccessStepPercent = 100;
                settings.Global.Anvil.FailureStepPercent = 100;
            });

        provider.GetRequiredService<IGlobalAnvilRateService>().ResolveRoll(10000, roll: 0).Succeeded.Should().BeTrue();

        var sentPackets = new List<Packet>();
        var client = CreateRecordingClient(sentPackets);
        var session = CreateUpgradeSession(provider, client, "Rin", originItemId, HighClassScroll);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleUpgradeAsync(client, UpgradeRequest(2, 1, (originItemId, 0), (HighClassScroll, 1)));

        session.Inventory[InventoryConstants.InventoryStart].Should().Match<ItemSlot>(slot => slot.IsEmpty);
        session.Inventory[InventoryConstants.InventoryStart + 1].Should().Match<ItemSlot>(slot => slot.IsEmpty);
        session.Money.Should().Be(850_000);
        ReadUpgradeReply(sentPackets).Should().Be(((byte)2, (byte)1, (byte)0, 0, (byte)0));
    }

    [Fact]
    public async Task HandleUpgradeAsync_AccessoryCompoundNeedsThreeCopies()
    {
        const int originItemId = 310110005;
        const int upgradedItemId = 310110006;
        const int accessoryScroll = 379159000;

        static ItemData Earring(int num) => new()
        {
            Num = num,
            Kind = 91,
            ItemType = 5,
            ItemClass = 0,
            Duration = 15
        };

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(originItemId).Returns(Earring(originItemId));
                gameData.GetItem(upgradedItemId).Returns(Earring(upgradedItemId));
                gameData.GetUpgradeRecipe(originItemId, accessoryScroll).Returns(new ItemUpgradeRecipeData
                {
                    OriginNumber = originItemId,
                    NewNumber = upgradedItemId,
                    RequiredItem = accessoryScroll
                });
                gameData.GetUpgradeSetting(5, 5, accessoryScroll, 0).Returns(new ItemUpgradeSettingsData
                {
                    ReqItem1 = accessoryScroll,
                    ItemType = 5,
                    ItemGrade = 5,
                    SuccessRate = 10000
                });
            });

        var sentPackets = new List<Packet>();
        var client = CreateRecordingClient(sentPackets);
        var session = CreateUpgradeSession(provider, client, "Rin", originItemId, originItemId, accessoryScroll);

        var coordinator = provider.GetRequiredService<IItemPacketCoordinator>();
        await coordinator.HandleUpgradeAsync(
            client, UpgradeRequest(3, 1, (originItemId, 0), (originItemId, 1), (accessoryScroll, 2)));

        ReadUpgradeReply(sentPackets).Should().Be(((byte)3, (byte)1, (byte)4, originItemId, (byte)0));
        session.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(originItemId);
    }

    [Fact]
    public void GlobalAnvilRateService_SuccessAndFailureClampWithinConfiguredBounds()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new GameServerSettings
        {
            Global = new GlobalSettings
            {
                Anvil = new AnvilSettings
                {
                    Enabled = true,
                    MaxRateSwingPercent = 10,
                    SuccessStepPercent = 7,
                    FailureStepPercent = 7
                }
            }
        });

        var service = new GlobalAnvilRateService(options);

        var firstFailure = service.ResolveRoll(5000, roll: 9999);
        var secondFailure = service.ResolveRoll(5000, roll: 9999);
        var afterFailures = service.GetSnapshot(5000);

        firstFailure.Succeeded.Should().BeFalse();
        firstFailure.ModifierAfterPercent.Should().BeApproximately(7d, 0.001d);
        secondFailure.ModifierAfterPercent.Should().BeApproximately(10d, 0.001d);
        afterFailures.EffectiveGenRate.Should().Be(5500);

        var firstSuccess = service.ResolveRoll(5000, roll: 0);
        var secondSuccess = service.ResolveRoll(5000, roll: 0);
        var afterSuccesses = service.GetSnapshot(5000);

        firstSuccess.Succeeded.Should().BeTrue();
        firstSuccess.ModifierBeforePercent.Should().BeApproximately(10d, 0.001d);
        firstSuccess.ModifierAfterPercent.Should().BeApproximately(3d, 0.001d);
        secondSuccess.ModifierAfterPercent.Should().BeApproximately(-4d, 0.001d);
        afterSuccesses.EffectiveGenRate.Should().Be(4800);
    }

}
