using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Tests;

public class EventTests : GameTestBase
{
    [Fact]
    public async Task EventSystemsPacketCoordinator_AssignRivalAsync_NamesTheKillerToTheVictimAlone()
    {
        using var provider = CreateProvider(_ => { });

        var victimClient = Substitute.For<IClient>();
        victimClient.Id.Returns(Guid.NewGuid());
        Packet? victimPacket = null;
        victimClient.SendPacket(Arg.Do<Packet>(packet => victimPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var killerClient = Substitute.For<IClient>();
        killerClient.Id.Returns(Guid.NewGuid());
        Packet? killerPacket = null;
        killerClient.SendPacket(Arg.Do<Packet>(packet => killerPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var victim = sessionManager.CreateSession(victimClient, characterId: 195, accountId: 205);
        victim.Name = "Hunted";
        victim.Money = 1000;
        victim.Loyalty = 200;

        var killer = sessionManager.CreateSession(killerClient, characterId: 196, accountId: 206);
        killer.Name = "Hunter";
        killer.Money = 2500;
        killer.Loyalty = 300;

        var coordinator = provider.GetRequiredService<IEventSystemsPacketCoordinator>();
        await coordinator.AssignRivalAsync(victim, killer);

        victim.RivalId.Should().Be(killer.CharacterId);
        killer.RivalId.Should().Be(-1, "only the player who died gains a rival");
        killerPacket.Should().BeNull();

        victimPacket.Should().NotBeNull();
        victimPacket!.GetOpcode().Should().Be((byte)GameOpcodes.GS_PVP);
        victimPacket.ResetOffset();
        victimPacket.ReadByte().Should().Be(1);
        victimPacket.ReadInt().Should().Be(killer.CharacterId);
        victimPacket.ReadInt().Should().Be(victim.Money);
        victimPacket.ReadInt().Should().Be(victim.Loyalty);
        victimPacket.ReadUShort().Should().Be(1);
        victimPacket.ReadUShort().Should().Be(1);
        victimPacket.ReadString().Should().BeEmpty();
        victimPacket.ReadString().Should().Be(killer.Name);
    }

    [Fact]
    public async Task EventSystemsPacketCoordinator_HandleBattleEventAsync_OpenReportsCurrentBattleState()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 197, accountId: 207);
        sessionManager.Battle.OpenBattleZone(BattleZoneManager.NATION_BATTLE, BattleZoneManager.ZONE_BATTLE1);

        var packet = new Packet(GameOpcodes.GS_BATTLE_EVENT);
        packet.WriteByte(1);

        var coordinator = provider.GetRequiredService<IEventSystemsPacketCoordinator>();
        await coordinator.HandleBattleEventAsync(client, packet);

        sentPacket.Should().NotBeNull();
        sentPacket!.GetOpcode().Should().Be((byte)GameOpcodes.GS_BATTLE_EVENT);
        sentPacket.ResetOffset();
        sentPacket.ReadByte().Should().Be(1);
        sentPacket.ReadByte().Should().Be(BattleZoneManager.NATION_BATTLE);
        sentPacket.ReadByte().Should().Be(BattleZoneManager.ZONE_BATTLE1);
    }

    [Fact]
    public async Task MiscPacketCoordinator_HandleNameChangeAsync_ConsumesScrollAndPersistsName()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "rename-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(account => account.Login == "rename-user").Id;
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Slot = 0,
                    Name = "Original",
                    Level = 35,
                    Class = 101,
                    MapId = 1,
                    X = 100,
                    Z = 100,
                    Hp = 200,
                    Mp = 100
                });
            });

        var characterId = await GetCharacterIdAsync(provider, "Original");

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId, accountId: 208);
        session.Name = "Original";
        session.ZoneId = 1;
        session.X = 10;
        session.Z = 10;
        session.Inventory[InventoryConstants.SlotMax].ItemId = 379090000;
        session.Inventory[InventoryConstants.SlotMax].Count = 1;
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_NAME_CHANGE);
        packet.WriteByte(0);
        packet.WriteString("Renamed");

        var coordinator = provider.GetRequiredService<IMiscPacketCoordinator>();
        await coordinator.HandleNameChangeAsync(client, packet);

        session.Name.Should().Be("Renamed");
        session.Inventory[InventoryConstants.SlotMax].IsEmpty.Should().BeTrue();

        var resultPacket = sentPackets.Last(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_NAME_CHANGE);
        resultPacket.ResetOffset();
        resultPacket.ReadByte().Should().Be(3);
        resultPacket.RemainingBytes.Should().Be(0);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db.Characters.FindAsync(characterId);
        persisted.Should().NotBeNull();
        persisted!.Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task LootPacketCoordinator_HandleItemDropAsync_CreatesBundleAndClearsInventorySlot()
    {
        const int itemId = 810001000;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Countable = 1,
                    Duration = 20,
                    Weight = 1
                });
                gameData.GetCoefficient((short)101).Returns(CreateBasicCoefficient(101));
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 198, accountId: 208);
        session.Class = 101;
        session.ZoneId = 1;
        session.X = 10;
        session.Z = 20;
        session.Hp = 100;
        session.Inventory[InventoryConstants.SlotMax].ItemId = itemId;
        session.Inventory[InventoryConstants.SlotMax].Count = 2;
        session.Inventory[InventoryConstants.SlotMax].Durability = 20;

        var packet = new Packet(GameOpcodes.GS_ITEM_DROP);
        packet.WriteByte(0);
        packet.WriteInt(itemId);
        packet.WriteUShort(2);

        var coordinator = provider.GetRequiredService<ILootPacketCoordinator>();
        await coordinator.HandleItemDropAsync(client, packet);

        session.Inventory[InventoryConstants.SlotMax].IsEmpty.Should().BeTrue();
        sentPackets.Should().Contain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_DROP);

        var dropPacket = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_DROP);
        dropPacket.ResetOffset();
        dropPacket.ReadInt().Should().Be(session.CharacterId);
        var bundleId = dropPacket.ReadInt();
        dropPacket.ReadByte().Should().Be(1);

        var bundle = sessionManager.Regions.GetBundle(bundleId);
        bundle.Should().NotBeNull();
        bundle!.Items.Should().ContainSingle();
        bundle.Items[0].ItemId.Should().Be(itemId);
        bundle.Items[0].Count.Should().Be(2);
    }

    [Fact]
    public async Task CombatRewardService_AwardNpcKillAsync_SendsTheItemDropPacketForNpcLoot()
    {
        const int itemId = 379109000;
        const short dropGroup = 750;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetNpcItem(dropGroup, true).Returns(new NpcItemData
                {
                    Index = dropGroup,
                    IsMonster = true,
                    Item1 = itemId,
                    Percent1 = 10000
                });
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Countable = 1,
                    Duration = 1
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var killer = sessionManager.CreateSession(client, characterId: 210, accountId: 310);
        killer.Hp = 100;
        killer.ZoneId = 21;

        var npc = new NpcInstance
        {
            UniqueId = 10042,
            NpcId = 750,
            ZoneId = 21,
            X = 100,
            Z = 200,
            Y = 0,
            DropItemGroup = dropGroup,
            IsMonster = true
        };
        npc.DamageMap[killer.CharacterId] = 10;
        npc.TopDamagerCharId = killer.CharacterId;

        var rewardService = provider.GetRequiredService<ICombatRewardService>();
        await rewardService.AwardNpcKillAsync(npc, killer);

        sentPackets.Should().ContainSingle(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_DROP);
        sentPackets.Should().NotContain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_BUNDLE_OPEN_REQ);

        var dropPacket = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_DROP);
        dropPacket.ResetOffset();
        dropPacket.ReadInt().Should().Be(npc.UniqueId);
        var bundleId = dropPacket.ReadInt();
        dropPacket.ReadByte().Should().Be(1);

        var bundle = sessionManager.Regions.GetBundle(bundleId);
        bundle.Should().NotBeNull();
        bundle!.OwnerCharId.Should().Be(killer.CharacterId);
        bundle.Items.Should().ContainSingle();
        bundle.Items[0].ItemId.Should().Be(itemId);
        bundle.Items[0].Count.Should().Be(1);
    }

    [Fact]
    public async Task CombatRewardService_AwardNpcKillAsync_SendsTheExperiencePacket()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMaxExpForLevel(10).Returns(1000L);
                gameData.PremiumItemExpTable.Returns([]);
            },
            settings =>
            {
                settings.Global.ExpMultiplier = 10;
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var killer = sessionManager.CreateSession(client, characterId: 212, accountId: 312);
        killer.Level = 10;
        killer.Experience = 100;
        killer.Hp = 100;
        killer.ZoneId = 21;

        var npc = new NpcInstance
        {
            IsMonster = true,
            UniqueId = 10043,
            NpcId = 751,
            Level = 10,
            ZoneId = 21,
            X = 100,
            Z = 200,
            Y = 0,
            Experience = 25
        };
        npc.DamageMap[killer.CharacterId] = 10;
        npc.TopDamagerCharId = killer.CharacterId;

        var rewardService = provider.GetRequiredService<ICombatRewardService>();
        await rewardService.AwardNpcKillAsync(npc, killer);

        killer.Experience.Should().Be(350);

        var expPacket = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_EXP_CHANGE);
        expPacket.ResetOffset();
        expPacket.ReadByte().Should().Be(0x04);
        expPacket.ReadLong().Should().Be(350);
        expPacket.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task CombatRewardService_AwardNpcKillAsync_HandlesLargeRewardAcrossMultipleLevels()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMaxExpForLevel(10).Returns(100L);
                gameData.GetMaxExpForLevel(11).Returns(200L);
                gameData.GetMaxExpForLevel(12).Returns(300L);
                gameData.GetCoefficient((short)101).Returns(CreateBasicCoefficient(101));
                gameData.PremiumItemExpTable.Returns([]);
            },
            settings =>
            {
                settings.Global.ExpMultiplier = 10;
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var killer = sessionManager.CreateSession(client, characterId: 213, accountId: 313);
        killer.Name = "Grinder";
        killer.Class = 101;
        killer.Level = 10;
        killer.Experience = 90;
        killer.Hp = 100;
        killer.MaxHp = 100;
        killer.Mp = 100;
        killer.MaxMp = 100;
        killer.ZoneId = 21;
        killer.X = 100;
        killer.Z = 200;
        sessionManager.Regions.AddToRegion(killer);

        var npc = new NpcInstance
        {
            IsMonster = true,
            UniqueId = 10044,
            NpcId = 752,
            Level = 10,
            ZoneId = 21,
            X = 100,
            Z = 200,
            Y = 0,
            Experience = 25
        };
        npc.DamageMap[killer.CharacterId] = 10;
        npc.TopDamagerCharId = killer.CharacterId;

        var rewardService = provider.GetRequiredService<ICombatRewardService>();
        await rewardService.AwardNpcKillAsync(npc, killer);

        killer.Level.Should().Be(12);
        killer.Experience.Should().Be(40);

        var levelPacket = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_LEVEL_CHANGE);
        levelPacket.ResetOffset();
        levelPacket.ReadInt().Should().Be(killer.CharacterId);
        levelPacket.ReadByte().Should().Be(12);
        levelPacket.ReadShort();
        levelPacket.ReadByte();
        levelPacket.ReadLong().Should().Be(300);
        levelPacket.ReadLong().Should().Be(40);
    }

    [Fact]
    public async Task LootPacketCoordinator_HandleBundleOpenAsync_SendsTheLootListWithBundleIdAndStatus()
    {
        const int itemId = 379109000;

        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 211, accountId: 311);
        session.ZoneId = 21;
        session.X = 100;
        session.Z = 200;
        session.Hp = 100;

        var bundle = sessionManager.Regions.CreateBundle(session.X, session.Z, session.Y);
        bundle.OwnerCharId = session.CharacterId;
        bundle.Items.Add(new LootItem
        {
            ItemId = itemId,
            Count = 1
        });

        var packet = new Packet(GameOpcodes.GS_BUNDLE_OPEN_REQ);
        packet.WriteInt(bundle.BundleId);

        var coordinator = provider.GetRequiredService<ILootPacketCoordinator>();
        await coordinator.HandleBundleOpenAsync(client, packet);

        var openPacket = sentPackets.Single(sent => sent.GetOpcode() == (byte)GameOpcodes.GS_BUNDLE_OPEN_REQ);
        openPacket.GetLength().Should().Be(
            5 + BundleOpenPacketWriter.WireSlots * BundleOpenPacketWriter.EntryBytes);
        openPacket.ResetOffset();
        openPacket.ReadInt().Should().Be(bundle.BundleId);
        openPacket.ReadByte().Should().Be(1);
        openPacket.ReadInt().Should().Be(itemId);
        openPacket.ReadUShort().Should().Be(1);
        for (var index = 1; index < BundleOpenPacketWriter.WireSlots; index++)
        {
            openPacket.ReadInt().Should().Be(0);
            openPacket.ReadUShort().Should().Be(0);
        }
    }

    [Fact]
    public async Task LootPacketCoordinator_HandleItemGetAsync_AddsLootToInventoryAndRemovesBundle()
    {
        const int itemId = 810002000;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Countable = 1,
                    Duration = 35,
                    Weight = 2
                });
                gameData.GetCoefficient((short)101).Returns(CreateBasicCoefficient(101));
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 199, accountId: 209);
        session.Class = 101;
        session.ZoneId = 1;
        session.X = 15;
        session.Z = 25;
        session.Hp = 100;

        var bundle = sessionManager.Regions.CreateBundle(session.X, session.Z, session.Y);
        bundle.OwnerCharId = session.CharacterId;
        bundle.Items.Add(new LootItem
        {
            ItemId = itemId,
            Count = 3
        });

        var packet = new Packet(GameOpcodes.GS_ITEM_GET);
        packet.WriteInt(bundle.BundleId);
        packet.WriteInt(itemId);
        packet.WriteUShort(0);

        var coordinator = provider.GetRequiredService<ILootPacketCoordinator>();
        await coordinator.HandleItemGetAsync(client, packet);

        session.Inventory[InventoryConstants.SlotMax].ItemId.Should().Be(itemId);
        session.Inventory[InventoryConstants.SlotMax].Count.Should().Be(3);
        session.Inventory[InventoryConstants.SlotMax].Durability.Should().Be(35);
        sessionManager.Regions.GetBundle(bundle.BundleId).Should().BeNull();

        var successPacket = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_GET);
        successPacket.ResetOffset();
        successPacket.ReadByte().Should().Be(1);
        successPacket.ReadInt().Should().Be(bundle.BundleId);
        successPacket.ReadByte().Should().Be(0);
        successPacket.ReadInt().Should().Be(itemId);
        successPacket.ReadUShort().Should().Be(3);
        successPacket.ReadInt().Should().Be(session.Money);
        successPacket.ReadUShort().Should().Be(0);

        sentPackets.Should().ContainSingle(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_WEIGHT_CHANGE);
        sentPackets.Should().NotContain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_COUNT_CHANGE);
    }

    [Fact]
    public async Task ChatPacketCoordinator_HandleAsync_ShoutChatConsumesGoldAndMpForLowLevelPlayers()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 200, accountId: 210);
        session.Name = "Shouter";
        session.ZoneId = 1;
        session.Nation = AccountNation.Karus;
        session.Level = 20;
        session.Money = 5000;
        session.MaxMp = 100;
        session.Mp = 100;
        session.X = 12;
        session.Z = 12;
        sessionManager.Regions.AddToRegion(session);

        var coordinator = provider.GetRequiredService<IChatPacketCoordinator>();
        await coordinator.HandleAsync(session, 5, "Selling loot");

        session.Money.Should().Be(2000);
        session.Mp.Should().Be(80);
        sentPackets.Should().Contain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_GOLD_CHANGE);
        sentPackets.Should().Contain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_MSP_CHANGE);
        sentPackets.Should().Contain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_CHAT);
    }
}
