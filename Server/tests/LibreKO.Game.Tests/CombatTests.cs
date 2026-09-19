using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Tests;

public class CombatTests : GameTestBase
{
    private const int AlwaysHits = 101;

    [Fact]
    public async Task CombatPacketCoordinator_HandleAttackAsync_BasicAttackCanDamageLowLevelWorm()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var attacker = sessionManager.CreateSession(client, characterId: 900, accountId: 901);
        attacker.Name = "Attacker";
        attacker.ZoneId = 21;
        attacker.X = 542;
        attacker.Z = 377;
        attacker.Hp = 100;
        attacker.MaxHp = 100;
        attacker.AttackAmount = 100;
        attacker.PlayerAttackAmount = 100;
        attacker.Stats.TotalHit = 50;
        attacker.Stats.TotalHitrate = 10;
        sessionManager.Regions.AddToRegion(attacker);

        var worm = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 750,
            Name = "Worm",
            NpcType = 0,
            ZoneId = 21,
            X = 543,
            Z = 377,
            SpawnX = 543,
            SpawnZ = 377,
            Hp = 1000,
            MaxHp = 1000,
            Ac = 5,
            EvadeRate = 1,
            AttackRange = 3,
            SearchRange = 8,
            TracingRange = 20
        });

        var coordinator = provider.GetRequiredService<ICombatPacketCoordinator>();

        for (var i = 0; i < 25 && worm.Hp == worm.MaxHp; i++)
        {
            var packet = new Packet(GameOpcodes.GS_ATTACK);
            packet.WriteByte(1);
            packet.WriteByte(0);
            packet.WriteInt(worm.UniqueId);
            packet.WriteShort(100);
            packet.WriteShort(1);
            packet.WriteByte(0);
            packet.WriteByte(0);
            await coordinator.HandleAttackAsync(client, packet);
        }

        worm.Hp.Should().BeLessThan(worm.MaxHp);
        sentPackets.Should().Contain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ATTACK);
        var targetHpPacket = sentPackets.Last(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_TARGET_HP);
        targetHpPacket.ResetOffset();
        targetHpPacket.ReadInt().Should().Be(worm.UniqueId);
        targetHpPacket.ReadByte().Should().Be(0);
        targetHpPacket.ReadInt().Should().Be(worm.MaxHp);
        targetHpPacket.ReadInt().Should().Be(worm.Hp);
        targetHpPacket.ReadInt().Should().Be(worm.Hp - worm.MaxHp);
        targetHpPacket.ReadInt().Should().Be(0);
        targetHpPacket.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task CombatPacketCoordinator_HandleAttackAsync_BowAttackConsumesArrow()
    {
        const int bowItemId = 160210001;
        const int arrowItemId = 391010000;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(bowItemId).Returns(new ItemData
                {
                    Num = bowItemId,
                    Kind = 70,
                    Slot = 4,
                    Delay = 150,
                    Range = 350,
                    Damage = 15,
                    Duration = 83
                });
                gameData.GetItem(arrowItemId).Returns(new ItemData
                {
                    Num = arrowItemId,
                    Kind = 120,
                    Countable = 1,
                    Duration = 1,
                    ReqLevelMax = 83
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var attacker = sessionManager.CreateSession(client, characterId: 905, accountId: 906);
        attacker.Name = "BowRogue";
        attacker.Class = 202;
        attacker.Level = 20;
        attacker.ZoneId = 21;
        attacker.X = 542;
        attacker.Z = 377;
        attacker.Hp = 100;
        attacker.MaxHp = 100;
        attacker.AttackAmount = 100;
        attacker.PlayerAttackAmount = 100;
        attacker.Stats.TotalHit = 50;
        attacker.Stats.TotalHitrate = 10;
        attacker.Inventory[InventoryConstants.LeftHand].ItemId = bowItemId;
        attacker.Inventory[InventoryConstants.LeftHand].Count = 1;
        attacker.Inventory[InventoryConstants.LeftHand].Durability = 83;
        attacker.Inventory[InventoryConstants.InventoryStart].ItemId = arrowItemId;
        attacker.Inventory[InventoryConstants.InventoryStart].Count = 5;
        attacker.Inventory[InventoryConstants.InventoryStart].Durability = 1;
        sessionManager.Regions.AddToRegion(attacker);

        var worm = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 750,
            Name = "Worm",
            NpcType = 0,
            ZoneId = 21,
            X = 543,
            Z = 377,
            SpawnX = 543,
            SpawnZ = 377,
            Hp = 1000,
            MaxHp = 1000,
            Ac = 5,
            EvadeRate = 1,
            AttackRange = 3,
            SearchRange = 8,
            TracingRange = 20
        });

        var coordinator = provider.GetRequiredService<ICombatPacketCoordinator>();

        for (var i = 0; i < 25 && attacker.Inventory[InventoryConstants.InventoryStart].Count == 5; i++)
        {
            var packet = new Packet(GameOpcodes.GS_ATTACK);
            packet.WriteByte(1);
            packet.WriteByte(0);
            packet.WriteInt(worm.UniqueId);
            packet.WriteShort(160);
            packet.WriteShort(1);
            packet.WriteByte(0);
            packet.WriteByte(0);
            await coordinator.HandleAttackAsync(client, packet);
        }

        attacker.Inventory[InventoryConstants.InventoryStart].Count.Should().BeLessThan(5);
        sentPackets.Should().Contain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_COUNT_CHANGE);
    }

    [Fact]
    public async Task CombatLifecycleService_HandlePlayerDeathAsync_AppliesEnemyZoneExpPenaltyImmediately()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMaxExpForLevel(10).Returns(1000L);
                gameData.PremiumItemTable.Returns(new Dictionary<byte, PremiumItemData>());
            });

        var lifecycleService = provider.GetRequiredService<ICombatLifecycleService>();
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var victimClient = Substitute.For<IClient>();
        victimClient.Id.Returns(Guid.NewGuid());
        var victim = sessionManager.CreateSession(victimClient, characterId: 930, accountId: 931);
        victim.Name = "Victim";
        victim.Level = 10;
        victim.Experience = 500;
        victim.Nation = AccountNation.Karus;
        victim.ZoneId = 2;
        victim.X = 100;
        victim.Z = 100;
        victim.Hp = 0;
        victim.MaxHp = 100;
        sessionManager.Regions.AddToRegion(victim);

        var killerClient = Substitute.For<IClient>();
        killerClient.Id.Returns(Guid.NewGuid());
        var killer = sessionManager.CreateSession(killerClient, characterId: 932, accountId: 933);
        killer.Name = "Killer";
        killer.Nation = AccountNation.ElMorad;
        killer.ZoneId = 2;
        killer.X = 101;
        killer.Z = 100;
        killer.Hp = 100;
        killer.MaxHp = 100;
        sessionManager.Regions.AddToRegion(killer);

        await lifecycleService.HandlePlayerDeathAsync(victim, killer);

        victim.Experience.Should().Be(490);
        await victimClient.Received().SendPacket(Arg.Any<Packet>());
    }

    [Fact]
    public async Task CombatLifecycleService_HandleNpcDeathAsync_BroadcastsTheDeadPacketAndLootDrop()
    {
        const int itemId = 379109000;
        const short dropGroup = 875;

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
        var killer = sessionManager.CreateSession(client, characterId: 940, accountId: 941);
        killer.Name = "Killer";
        killer.ZoneId = 21;
        killer.X = 100;
        killer.Z = 100;
        killer.Hp = 100;
        sessionManager.Regions.AddToRegion(killer);

        var npc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = 750,
            Name = "Worm",
            NpcType = 0,
            ZoneId = 21,
            X = 100,
            Z = 100,
            Y = 0,
            SpawnX = 100,
            SpawnZ = 100,
            Hp = 0,
            MaxHp = 100,
            DropItemGroup = dropGroup,
            IsMonster = true
        });
        npc.TopDamagerCharId = killer.CharacterId;
        npc.DamageMap[killer.CharacterId] = 100;

        var lifecycleService = provider.GetRequiredService<ICombatLifecycleService>();
        await lifecycleService.HandleNpcDeathAsync(npc, killer);

        var deadPacket = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_DEAD);
        deadPacket.ResetOffset();
        deadPacket.ReadInt().Should().Be(npc.UniqueId);
        deadPacket.RemainingBytes.Should().Be(0);

        var dropPacket = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ITEM_DROP);
        dropPacket.ResetOffset();
        dropPacket.ReadInt().Should().Be(npc.UniqueId);
        dropPacket.ReadInt().Should().BeGreaterThan(0);
        dropPacket.ReadByte().Should().Be(1);
    }

    [Fact]
    public async Task CombatLifecycleService_HandlePlayerDeathAsync_BroadcastsTheDeadPacket()
    {
        using var provider = CreateProvider(_ => { });

        var victimClient = Substitute.For<IClient>();
        victimClient.Id.Returns(Guid.NewGuid());
        var victimPackets = new List<Packet>();
        victimClient.SendPacket(Arg.Do<Packet>(packet => victimPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var killerClient = Substitute.For<IClient>();
        killerClient.Id.Returns(Guid.NewGuid());
        killerClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var victim = sessionManager.CreateSession(victimClient, characterId: 950, accountId: 951);
        victim.Name = "Victim";
        victim.ZoneId = 21;
        victim.X = 120;
        victim.Z = 120;
        victim.Hp = 0;
        victim.MaxHp = 100;
        victim.Nation = AccountNation.Karus;
        sessionManager.Regions.AddToRegion(victim);

        var killer = sessionManager.CreateSession(killerClient, characterId: 952, accountId: 953);
        killer.Name = "Killer";
        killer.ZoneId = 21;
        killer.X = 121;
        killer.Z = 120;
        killer.Hp = 100;
        killer.MaxHp = 100;
        killer.Nation = AccountNation.ElMorad;
        sessionManager.Regions.AddToRegion(killer);

        var lifecycleService = provider.GetRequiredService<ICombatLifecycleService>();
        await lifecycleService.HandlePlayerDeathAsync(victim, killer);

        var deadPacket = victimPackets.First(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_DEAD);
        deadPacket.ResetOffset();
        deadPacket.ReadInt().Should().Be(victim.CharacterId);
        deadPacket.ReadInt().Should().Be(killer.CharacterId);
        deadPacket.ReadInt().Should().Be(0);
        deadPacket.ReadInt().Should().Be(0);
        deadPacket.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task NpcAiDeathService_HandlePlayerKilledByNpcAsync_BroadcastsTheDeadPacket()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMaxExpForLevel(10).Returns(1000L);
            });

        var targetClient = Substitute.For<IClient>();
        targetClient.Id.Returns(Guid.NewGuid());
        var targetPackets = new List<Packet>();
        targetClient.SendPacket(Arg.Do<Packet>(packet => targetPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var target = sessionManager.CreateSession(targetClient, characterId: 960, accountId: 961);
        target.Name = "Victim";
        target.ZoneId = 21;
        target.X = 140;
        target.Z = 140;
        target.Level = 10;
        target.Experience = 500;
        target.Hp = 0;
        target.MaxHp = 100;
        sessionManager.Regions.AddToRegion(target);

        var npc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 751,
            Name = "Worm",
            NpcType = 0,
            ZoneId = 21,
            X = 141,
            Z = 140,
            SpawnX = 141,
            SpawnZ = 140,
            Hp = 100,
            MaxHp = 100,
            Attack1 = 10
        });

        var deathService = provider.GetRequiredService<INpcAiDeathService>();
        await deathService.HandlePlayerKilledByNpcAsync(target, npc);

        var deadPacket = targetPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_DEAD);
        deadPacket.ResetOffset();
        deadPacket.ReadInt().Should().Be(target.CharacterId);
        deadPacket.ReadInt().Should().Be(npc.UniqueId);
        deadPacket.ReadInt().Should().Be(0);
        deadPacket.ReadInt().Should().Be(0);
        deadPacket.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task CombatPacketCoordinator_HandleAttackAsync_AttackingReturningMonsterReengagesAttacker()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var attacker = sessionManager.CreateSession(client, characterId: 915, accountId: 916);
        attacker.Name = "Chaser";
        attacker.ZoneId = 21;
        attacker.X = 52;
        attacker.Z = 50;
        attacker.Hp = 100;
        attacker.MaxHp = 100;
        attacker.AttackAmount = 100;
        attacker.PlayerAttackAmount = 100;
        attacker.Stats.TotalHit = 200;
        attacker.Stats.TotalHitrate = 100;
        sessionManager.Regions.AddToRegion(attacker);

        var worm = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 751,
            Name = "ReturningWorm",
            NpcType = 0,
            ZoneId = 21,
            X = 60,
            Z = 50,
            SpawnX = 50,
            SpawnZ = 50,
            Hp = 1000,
            MaxHp = 1000,
            Ac = 0,
            EvadeRate = 1,
            AttackRange = 1,
            SearchRange = 8,
            TracingRange = 20,
            State = NpcState.Returning
        });

        var packet = new Packet(GameOpcodes.GS_ATTACK);
        packet.WriteByte(1);
        packet.WriteByte(0);
        packet.WriteInt(worm.UniqueId);
        packet.WriteShort(100);
        packet.WriteShort(1);
        packet.WriteByte(0);
        packet.WriteByte(0);

        var coordinator = provider.GetRequiredService<ICombatPacketCoordinator>();
        await coordinator.HandleAttackAsync(client, packet);

        worm.TargetUserId.Should().Be(attacker.CharacterId);
        worm.State.Should().Be(NpcState.Attacking);
    }

    [Fact]
    public async Task CombatPacketCoordinator_HandleTargetHpAsync_ReturnsNpcHpWithZeroDamageField()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 910, accountId: 911);
        session.ZoneId = 21;
        session.X = 542;
        session.Z = 377;
        sessionManager.Regions.AddToRegion(session);

        var worm = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 750,
            Name = "Worm",
            NpcType = 0,
            ZoneId = 21,
            X = 543,
            Z = 377,
            SpawnX = 543,
            SpawnZ = 377,
            Hp = 987,
            MaxHp = 1000,
            Ac = 5,
            EvadeRate = 1
        });

        var packet = new Packet(GameOpcodes.GS_TARGET_HP);
        packet.WriteInt(worm.UniqueId);
        packet.WriteByte(7);

        var coordinator = provider.GetRequiredService<ICombatPacketCoordinator>();
        await coordinator.HandleTargetHpAsync(client, packet);

        var targetHpPacket = sentPackets.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_TARGET_HP);
        targetHpPacket.ResetOffset();
        targetHpPacket.ReadInt().Should().Be(worm.UniqueId);
        targetHpPacket.ReadByte().Should().Be(7);
        targetHpPacket.ReadInt().Should().Be(worm.MaxHp);
        targetHpPacket.ReadInt().Should().Be(worm.Hp);
        targetHpPacket.ReadInt().Should().Be(0);
        targetHpPacket.ReadInt().Should().Be(0);
        targetHpPacket.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void UserSession_RecalculateStats_DoesNotAddStaminaDirectlyToBaseAc()
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());

        var session = new UserSession(client, 920, 921)
        {
            Class = 101,
            Level = 20,
            Strength = 50,
            Stamina = 200,
            Dexterity = 50,
            Intelligence = 50,
            Magic = 50
        };

        var gameData = Substitute.For<IGameDataService>();
        var coefficient = CreateBasicCoefficient(101);

        session.RecalculateStats(coefficient, gameData);

        // Base AC = coefficient.Ac * (level + itemAc) = 1.0 * 20 = 20.
        // Stamina does NOT feed into the base formula, but adds a passive
        // bonus of (stamina - 100) when stamina > 100.
        session.Stats.TotalAc.Should().Be((short)(20 + (200 - 100)));
    }

    [Fact]
    public async Task CombatPacketCoordinator_HandleSkillDataAsync_RoundTripsSavedSkills()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.CreateSession(client, characterId: 501, accountId: 601);

        var coordinator = provider.GetRequiredService<ICombatPacketCoordinator>();

        var savePacket = new Packet(GameOpcodes.GS_SKILLDATA);
        savePacket.WriteByte(1);
        savePacket.WriteShort(3);
        savePacket.WriteInt(101001);
        savePacket.WriteInt(201002);
        savePacket.WriteInt(301003);
        await coordinator.HandleSkillDataAsync(client, savePacket);

        var loadPacket = new Packet(GameOpcodes.GS_SKILLDATA);
        loadPacket.WriteByte(2);
        await coordinator.HandleSkillDataAsync(client, loadPacket);

        var skillPackets = sentPackets
            .Where(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_SKILLDATA)
            .ToList();

        skillPackets.Should().HaveCount(2);

        var reply = skillPackets.Last();
        reply.ResetOffset();
        reply.ReadShort().Should().Be(3);
        reply.ReadInt().Should().Be(101001);
        reply.ReadInt().Should().Be(201002);
        reply.ReadInt().Should().Be(301003);
    }

    [Fact]
    public async Task CombatPacketCoordinator_HandleRegeneAsync_RestoresHpAndDropsVolatileBuffsButKeepsScrolls()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetStartPosition(1).Returns(new StartPositionData
                {
                    ZoneId = 1,
                    KarusX = 120,
                    KarusZ = 240,
                    ElmoradX = 600,
                    ElmoradZ = 700,
                    RangeX = 0,
                    RangeZ = 0
                });
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
                gameData.GetMaxExpForLevel(10).Returns(1000L);
                gameData.GetMagic(500034).Returns(new MagicData { Id = 500034, Type1 = 4 });
                gameData.GetMagic(491008).Returns(new MagicData
                {
                    Id = 491008,
                    Type1 = 4,
                    UseItem = 379123000
                });
                gameData.GetMagic(490025).Returns(new MagicData
                {
                    Id = 490025,
                    Type1 = 4,
                    Moral = 7,
                    UseItem = 389025000
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 502, accountId: 602);
        session.Class = 101;
        session.Level = 10;
        session.Nation = AccountNation.Karus;
        session.ZoneId = 1;
        session.X = 10;
        session.Z = 10;
        session.MaxHp = 250;
        session.Hp = 0;
        session.TransformId = 321;
        session.Invisibility = InvisibilityType.DispelOnAttack;
        session.ActiveBuffs[490001] = new ActiveBuff
        {
            MagicId = 490001,
            CasterId = session.CharacterId,
            Duration = 60,
            ExpireTicks = DateTime.UtcNow.AddMinutes(1).Ticks,
            BonusAttack = 20
        };
        session.ActiveBuffs[500034] = new ActiveBuff
        {
            MagicId = 500034,
            CasterId = session.CharacterId,
            Duration = 1800,
            ExpireTicks = DateTime.UtcNow.AddMinutes(30).Ticks,
            BuffType = BuffType.Damage,
            BonusAttack = 130
        };
        session.ActiveBuffs[491008] = new ActiveBuff
        {
            MagicId = 491008,
            CasterId = session.CharacterId,
            Duration = 600,
            ExpireTicks = DateTime.UtcNow.AddMinutes(10).Ticks,
            BuffType = BuffType.Ac,
            BonusAc = 150
        };
        session.ActiveBuffs[490025] = new ActiveBuff
        {
            MagicId = 490025,
            CasterId = session.CharacterId + 1,
            Duration = 10,
            ExpireTicks = DateTime.UtcNow.AddSeconds(10).Ticks,
            BuffType = BuffType.Speed,
            BonusSpeed = 30
        };
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_REGENE);
        packet.WriteByte(1);

        var coordinator = provider.GetRequiredService<ICombatPacketCoordinator>();
        await coordinator.HandleRegeneAsync(client, packet);

        session.Hp.Should().Be(session.MaxHp);
        session.ActiveBuffs.Should().NotContainKey(490001);
        session.ActiveBuffs.Should().ContainKey(500034);
        session.ActiveBuffs.Should().ContainKey(491008);
        session.ActiveBuffs.Should().NotContainKey(490025);
        session.TransformId.Should().Be(0);
        session.Invisibility.Should().Be(InvisibilityType.None);
        session.X.Should().Be(120);
        session.Z.Should().Be(240);

        sentPackets.Should().Contain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_REGENE);
        sentPackets.Should().Contain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_HP_CHANGE);

        var recasts = sentPackets
            .Where(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_MAGIC_PROCESS)
            .ToList();
        recasts.Should().HaveCount(2);

        var recast = recasts.Single(packet =>
        {
            packet.ResetOffset();
            packet.ReadByte();
            return packet.ReadInt() == 500034;
        });
        recast.ResetOffset();
        recast.ReadByte().Should().Be(3);
        recast.ReadInt().Should().Be(500034);
        recast.ReadInt().Should().Be(session.CharacterId);
        recast.ReadInt().Should().Be(session.CharacterId);
        recast.ReadInt().Should().Be(0);
        recast.ReadInt().Should().Be(1);
        recast.ReadInt().Should().Be(0);
        recast.ReadInt().Should().BeInRange(1700, 1800,
            "the replayed buff keeps the time it had left, and the lower bound only has to survive a slow test run");
    }

    [Fact]
    public async Task MagicPacketCoordinator_HandleAsync_CancelRemovesSpeedBuffAndSendsLegacyExpiryPayload()
    {
        const int skillId = 700001;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(skillId).Returns(new MagicData
                {
                    Id = skillId,
                    Type1 = 4
                });
                gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data>
                {
                    [skillId] = new()
                    {
                        Id = skillId,
                        BuffType = (byte)BuffType.Speed,
                        Duration = 120,
                        Speed = 150
                    }
                });
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 503, accountId: 603);
        session.Class = 101;
        session.Level = 20;
        session.Strength = 50;
        session.Stamina = 50;
        session.Dexterity = 50;
        session.Intelligence = 50;
        session.Magic = 50;
        session.ZoneId = 1;
        session.X = 20;
        session.Z = 20;
        session.ActiveBuffs[skillId] = new ActiveBuff
        {
            MagicId = skillId,
            CasterId = session.CharacterId,
            Duration = 120,
            ExpireTicks = DateTime.UtcNow.AddMinutes(2).Ticks,
            BuffType = BuffType.Speed,
            BonusSpeed = 150
        };
        session.RecalculateStats(CreateBasicCoefficient(101), provider.GetRequiredService<IGameDataService>());
        session.SpeedAmount.Should().Be(150);
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte(6);
        packet.WriteInt(skillId);
        packet.WriteInt(session.CharacterId);
        packet.WriteInt(session.CharacterId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        await coordinator.HandleAsync(client, packet);

        session.ActiveBuffs.Should().NotContainKey(skillId);
        session.SpeedAmount.Should().Be(100);

        var magicPacket = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_MAGIC_PROCESS);
        magicPacket.ResetOffset();
        magicPacket.ReadByte().Should().Be(5);
        magicPacket.ReadByte().Should().Be((byte)BuffType.Speed);
    }

    [Fact]
    public async Task MagicPacketCoordinator_HandleAsync_UsesSkillIdForType4Lookup_WhenEtcIsZero()
    {
        const int skillId = 202002;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(skillId).Returns(new MagicData
                {
                    Id = skillId,
                    Type1 = 4,
                    Moral = 1,
                    Etc = 0
                });
                gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data>
                {
                    [skillId] = new()
                    {
                        Id = skillId,
                        BuffType = 6,
                        Duration = 10,
                        Speed = 150,
                        Attack = 100,
                        AttackSpeed = 100,
                        HitRate = AlwaysHits,
                        AvoidRate = 100,
                        ExpPct = 100
                    }
                });
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 530, accountId: 630);
        session.Name = "Mage";
        session.Class = 101;
        session.Level = 20;
        session.Strength = 50;
        session.Stamina = 50;
        session.Dexterity = 50;
        session.Intelligence = 50;
        session.Magic = 50;
        session.ZoneId = 1;
        session.X = 20;
        session.Z = 20;
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Effecting);
        packet.WriteInt(skillId);
        packet.WriteInt(session.CharacterId);
        packet.WriteInt(session.CharacterId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        await coordinator.HandleAsync(client, packet);

        session.ActiveBuffs.Should().ContainKey(skillId);
        sentPackets.Should().ContainSingle(p => p.GetOpcode() == (byte)GameOpcodes.GS_MAGIC_PROCESS);
    }

    [Fact]
    public async Task MagicPacketCoordinator_HandleAsync_Type2FlyingConsumesArrowAndMp()
    {
        const int skillId = 202008;
        const int arrowItemId = 391010000;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(skillId).Returns(new MagicData
                {
                    Id = skillId,
                    Type1 = 2,
                    Moral = 7,
                    UseItem = arrowItemId,
                    Msp = 7
                });
                gameData.MagicType2Table.Returns(new Dictionary<int, MagicType2Data>
                {
                    [skillId] = new()
                    {
                        Id = skillId,
                        NeedArrow = 1
                    }
                });
                gameData.GetItem(arrowItemId).Returns(new ItemData
                {
                    Num = arrowItemId,
                    Kind = 120,
                    Countable = 1,
                    Duration = 1,
                    ReqLevelMax = 83
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 540, accountId: 640);
        session.Class = 202;
        session.Level = 20;
        session.ZoneId = 1;
        session.X = 20;
        session.Z = 20;
        session.MaxMp = 100;
        session.Mp = 20;
        session.Inventory[InventoryConstants.InventoryStart].ItemId = arrowItemId;
        session.Inventory[InventoryConstants.InventoryStart].Count = 10;
        session.Inventory[InventoryConstants.InventoryStart].Durability = 1;
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Flying);
        packet.WriteInt(skillId);
        packet.WriteInt(session.CharacterId);
        packet.WriteInt(session.CharacterId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        await coordinator.HandleAsync(client, packet);

        session.Mp.Should().Be(13);
        session.Inventory[InventoryConstants.InventoryStart].Count.Should().Be(9);

        var stackPacket = sentPackets.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_COUNT_CHANGE);
        stackPacket.ResetOffset();
        stackPacket.ReadShort().Should().Be(1);
        stackPacket.ReadByte().Should().Be(1);
        stackPacket.ReadByte().Should().Be(0);
        stackPacket.ReadInt().Should().Be(arrowItemId);
        stackPacket.ReadInt().Should().Be(9);

        sentPackets.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_MSP_CHANGE);
        sentPackets.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_MAGIC_PROCESS);
    }

    [Fact]
    public async Task MagicPacketCoordinator_HandleAsync_Type1EffectingOnlyConsumesMp()
    {
        const int skillId = 101001;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(skillId).Returns(new MagicData
                {
                    Id = skillId,
                    Type1 = 1,
                    Moral = 7,
                    Msp = 4
                });
                gameData.MagicType1Table.Returns(new Dictionary<int, MagicType1Data>
                {
                    [skillId] = new()
                    {
                        Id = skillId,
                        HitType = 1,
                        HitRate = AlwaysHits,
                        Hit = 100,
                        AddDamage = 100
                    }
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var targetClient = Substitute.For<IClient>();
        targetClient.Id.Returns(Guid.NewGuid());
        targetClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var warrior = sessionManager.CreateSession(client, characterId: 5410, accountId: 6410);
        warrior.Class = 101;
        warrior.Level = 20;
        warrior.ZoneId = BattleZoneManager.ZONE_RONARK_LAND;
        warrior.Nation = AccountNation.Karus;
        warrior.X = 20;
        warrior.Z = 20;
        warrior.MaxMp = 100;
        warrior.Mp = 20;
        warrior.Stats = new DerivedStats
        {
            TotalHit = 100,
            TotalHitrate = 1f
        };
        sessionManager.Regions.AddToRegion(warrior);

        var target = sessionManager.CreateSession(targetClient, characterId: 5411, accountId: 6411);
        target.Class = 201;
        target.Level = 20;
        target.ZoneId = BattleZoneManager.ZONE_RONARK_LAND;
        target.Nation = AccountNation.ElMorad;
        target.X = 21;
        target.Z = 20;
        target.MaxHp = 200;
        target.Hp = 200;
        target.Stats = new DerivedStats
        {
            TotalAc = 0,
            TotalEvasionrate = 1f
        };
        sessionManager.Regions.AddToRegion(target);

        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Effecting);
        packet.WriteInt(skillId);
        packet.WriteInt(warrior.CharacterId);
        packet.WriteInt(target.CharacterId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        await coordinator.HandleAsync(client, packet);

        warrior.Mp.Should().Be(16);
        target.Hp.Should().BeLessThan((short)200);
        sentPackets.Count(p => p.GetOpcode() == (byte)GameOpcodes.GS_MSP_CHANGE).Should().Be(1);
        sentPackets.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_MAGIC_PROCESS);
    }

    [Fact]
    public async Task MagicPacketCoordinator_HandleAsync_Type1CastingThenEffectingConsumesMpOnceOnEffecting()
    {
        const int skillId = 101001;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(skillId).Returns(new MagicData
                {
                    Id = skillId,
                    Type1 = 1,
                    Moral = 7,
                    Msp = 4
                });
                gameData.MagicType1Table.Returns(new Dictionary<int, MagicType1Data>
                {
                    [skillId] = new()
                    {
                        Id = skillId,
                        HitType = 1,
                        HitRate = AlwaysHits,
                        Hit = 100,
                        AddDamage = 100
                    }
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var targetClient = Substitute.For<IClient>();
        targetClient.Id.Returns(Guid.NewGuid());
        targetClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var warrior = sessionManager.CreateSession(client, characterId: 5412, accountId: 6412);
        warrior.Class = 101;
        warrior.Level = 20;
        warrior.ZoneId = BattleZoneManager.ZONE_RONARK_LAND;
        warrior.Nation = AccountNation.Karus;
        warrior.X = 20;
        warrior.Z = 20;
        warrior.MaxMp = 100;
        warrior.Mp = 20;
        warrior.Stats = new DerivedStats
        {
            TotalHit = 100,
            TotalHitrate = 1f
        };
        sessionManager.Regions.AddToRegion(warrior);

        var target = sessionManager.CreateSession(targetClient, characterId: 5413, accountId: 6413);
        target.Class = 201;
        target.Level = 20;
        target.ZoneId = BattleZoneManager.ZONE_RONARK_LAND;
        target.Nation = AccountNation.ElMorad;
        target.X = 21;
        target.Z = 20;
        target.MaxHp = 200;
        target.Hp = 200;
        target.Stats = new DerivedStats
        {
            TotalAc = 0,
            TotalEvasionrate = 1f
        };
        sessionManager.Regions.AddToRegion(target);

        var castingPacket = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        castingPacket.WriteByte((byte)MagicProcessOpcode.Casting);
        castingPacket.WriteInt(skillId);
        castingPacket.WriteInt(warrior.CharacterId);
        castingPacket.WriteInt(target.CharacterId);
        for (var i = 0; i < 7; i++)
            castingPacket.WriteInt(0);

        var effectingPacket = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        effectingPacket.WriteByte((byte)MagicProcessOpcode.Effecting);
        effectingPacket.WriteInt(skillId);
        effectingPacket.WriteInt(warrior.CharacterId);
        effectingPacket.WriteInt(target.CharacterId);
        for (var i = 0; i < 7; i++)
            effectingPacket.WriteInt(0);

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        await coordinator.HandleAsync(client, castingPacket);

        warrior.Mp.Should().Be(20);
        sentPackets.Should().NotContain(p => p.GetOpcode() == (byte)GameOpcodes.GS_MSP_CHANGE);

        await coordinator.HandleAsync(client, effectingPacket);

        warrior.Mp.Should().Be(16);
        target.Hp.Should().BeLessThan((short)200);
        sentPackets.Count(p => p.GetOpcode() == (byte)GameOpcodes.GS_MSP_CHANGE).Should().Be(1);
    }

    [Fact]
    public async Task MagicPacketCoordinator_HandleAsync_Type3EffectingOnlyMinorHealConsumesMp()
    {
        const int skillId = 207705;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(skillId).Returns(new MagicData
                {
                    Id = skillId,
                    Type1 = 3,
                    Moral = 2,
                    Msp = 100,
                    Range = 25
                });
                gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
                {
                    [skillId] = new()
                    {
                        Id = skillId,
                        DirectType = 1,
                        FirstDamage = 60
                    }
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 541, accountId: 641);
        session.Class = 201;
        session.Level = 20;
        session.ZoneId = 1;
        session.X = 20;
        session.Z = 20;
        session.MaxHp = 100;
        session.Hp = 30;
        session.MaxMp = 300;
        session.Mp = 200;
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Effecting);
        packet.WriteInt(skillId);
        packet.WriteInt(session.CharacterId);
        packet.WriteInt(session.CharacterId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        await coordinator.HandleAsync(client, packet);

        session.Hp.Should().Be(90);
        session.Mp.Should().Be(100);
        sentPackets.Count(p => p.GetOpcode() == (byte)GameOpcodes.GS_MSP_CHANGE).Should().Be(1);
        sentPackets.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_HP_CHANGE);
        sentPackets.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_MAGIC_PROCESS);
    }

    [Fact]
    public async Task MagicPacketCoordinator_HandleAsync_DefenseSkillCannotOverwriteAnArmourScroll()
    {
        const int scrollId = 491008;
        const int defenseId = 101007;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(defenseId).Returns(new MagicData
                {
                    Id = defenseId,
                    Type1 = 4,
                    Moral = 1
                });
                gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data>
                {
                    [defenseId] = new()
                    {
                        Id = defenseId,
                        BuffType = (byte)BuffType.Ac,
                        Duration = 10,
                        Ac = 50,
                        AcPct = 100
                    }
                });
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 561, accountId: 661);
        session.Class = 101;
        session.Level = 20;
        session.ZoneId = 1;
        session.X = 20;
        session.Z = 20;
        session.ActiveBuffs[scrollId] = new ActiveBuff
        {
            MagicId = scrollId,
            CasterId = session.CharacterId,
            Duration = 600,
            ExpireTicks = DateTime.UtcNow.AddMinutes(10).Ticks,
            BuffType = BuffType.Ac,
            BonusAc = 150,
            BonusAcPct = 100
        };
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Effecting);
        packet.WriteInt(defenseId);
        packet.WriteInt(session.CharacterId);
        packet.WriteInt(session.CharacterId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);

        var castingPacket = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        castingPacket.WriteByte((byte)MagicProcessOpcode.Casting);
        castingPacket.WriteInt(defenseId);
        castingPacket.WriteInt(session.CharacterId);
        castingPacket.WriteInt(session.CharacterId);
        for (var i = 0; i < 7; i++)
            castingPacket.WriteInt(0);

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        await coordinator.HandleAsync(client, castingPacket);
        await coordinator.HandleAsync(client, packet);

        session.ActiveBuffs.Should().ContainKey(scrollId);
        session.ActiveBuffs[scrollId].BonusAc.Should().Be(150);
        session.ActiveBuffs.Should().NotContainKey(defenseId);

        var fails = sentPackets.Where(p =>
        {
            if (p.GetOpcode() != (byte)GameOpcodes.GS_MAGIC_PROCESS) return false;
            p.ResetOffset();
            return p.ReadByte() == (byte)MagicProcessOpcode.Fail;
        }).ToList();
        fails.Should().HaveCount(1);
    }

    [Fact]
    public async Task MagicPacketCoordinator_HandleAsync_CancelRemovesAPlayersOwnBuff()
    {
        const int skillId = 491008;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(skillId).Returns(new MagicData { Id = skillId, Type1 = 4, Moral = 1 });
                gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data>
                {
                    [skillId] = new()
                    {
                        Id = skillId,
                        BuffType = (byte)BuffType.Ac,
                        Duration = 600,
                        Ac = 150,
                        AcPct = 100
                    }
                });
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 571, accountId: 671);
        session.Class = 101;
        session.Level = 20;
        session.ZoneId = 1;
        session.ActiveBuffs[skillId] = new ActiveBuff
        {
            MagicId = skillId,
            CasterId = session.CharacterId,
            Duration = 600,
            ExpireTicks = DateTime.UtcNow.AddMinutes(10).Ticks,
            BuffType = BuffType.Ac,
            BonusAc = 150,
            BonusAcPct = 100
        };
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Cancel);
        packet.WriteInt(skillId);
        packet.WriteInt(session.CharacterId);
        packet.WriteInt(session.CharacterId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        await coordinator.HandleAsync(client, packet);

        session.ActiveBuffs.Should().NotContainKey(skillId);
    }

    [Fact]
    public async Task MagicPacketCoordinator_HandleAsync_CancelRefusesToDropADebuff()
    {
        const int skillId = 300209;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(skillId).Returns(new MagicData { Id = skillId, Type1 = 4, Moral = 1 });
                gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data>
                {
                    [skillId] = new()
                    {
                        Id = skillId,
                        BuffType = (byte)BuffType.Ac,
                        Duration = 600,
                        Ac = 0,
                        AcPct = 75
                    }
                });
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 572, accountId: 672);
        session.Class = 101;
        session.Level = 20;
        session.ZoneId = 1;
        session.ActiveBuffs[skillId] = new ActiveBuff
        {
            MagicId = skillId,
            CasterId = session.CharacterId,
            Duration = 600,
            ExpireTicks = DateTime.UtcNow.AddMinutes(10).Ticks,
            BuffType = BuffType.Ac,
            BonusAc = 0,
            BonusAcPct = 75
        };
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Cancel);
        packet.WriteInt(skillId);
        packet.WriteInt(session.CharacterId);
        packet.WriteInt(session.CharacterId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        await coordinator.HandleAsync(client, packet);

        session.ActiveBuffs.Should().ContainKey(skillId);
    }

    [Fact]
    public async Task MagicPacketCoordinator_HandleAsync_Type3CastingThenEffectingConsumesMpOnceOnEffecting()
    {
        const int skillId = 207006;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(skillId).Returns(new MagicData
                {
                    Id = skillId,
                    Type1 = 3,
                    Moral = 1,
                    Msp = 6,
                    CastTime = 2
                });
                gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
                {
                    [skillId] = new()
                    {
                        Id = skillId,
                        DirectType = 1,
                        FirstDamage = 40
                    }
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 542, accountId: 642);
        session.Class = 201;
        session.Level = 20;
        session.ZoneId = 1;
        session.X = 20;
        session.Z = 20;
        session.MaxHp = 100;
        session.Hp = 50;
        session.MaxMp = 100;
        session.Mp = 20;
        sessionManager.Regions.AddToRegion(session);

        var castingPacket = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        castingPacket.WriteByte((byte)MagicProcessOpcode.Casting);
        castingPacket.WriteInt(skillId);
        castingPacket.WriteInt(session.CharacterId);
        castingPacket.WriteInt(session.CharacterId);
        for (var i = 0; i < 7; i++)
            castingPacket.WriteInt(0);

        var effectingPacket = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        effectingPacket.WriteByte((byte)MagicProcessOpcode.Effecting);
        effectingPacket.WriteInt(skillId);
        effectingPacket.WriteInt(session.CharacterId);
        effectingPacket.WriteInt(session.CharacterId);
        for (var i = 0; i < 7; i++)
            effectingPacket.WriteInt(0);

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        await coordinator.HandleAsync(client, castingPacket);

        session.Mp.Should().Be(20);
        sentPackets.Should().NotContain(p => p.GetOpcode() == (byte)GameOpcodes.GS_MSP_CHANGE);

        await coordinator.HandleAsync(client, effectingPacket);

        session.Hp.Should().Be(90);
        session.Mp.Should().Be(14);
        sentPackets.Count(p => p.GetOpcode() == (byte)GameOpcodes.GS_MSP_CHANGE).Should().Be(1);
    }

    [Fact]
    public async Task MagicPacketCoordinator_HandleAsync_Type3ManaPotionRestoresMpAndConsumesItem()
    {
        const int skillId = 490016;
        const int potionItemId = 389016000;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(skillId).Returns(new MagicData
                {
                    Id = skillId,
                    Type1 = 3,
                    Moral = 1,
                    UseItem = potionItemId,
                    ItemGroup = 9
                });
                gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
                {
                    [skillId] = new()
                    {
                        Id = skillId,
                        DirectType = 2,
                        FirstDamage = 120
                    }
                });
                gameData.GetItem(potionItemId).Returns(new ItemData
                {
                    Num = potionItemId,
                    Kind = 97,
                    Countable = 1,
                    Weight = 1,
                    Duration = 1,
                    ReqLevelMax = 83
                });
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 541, accountId: 641);
        session.Class = 101;
        session.Level = 20;
        session.ZoneId = 1;
        session.X = 20;
        session.Z = 20;
        session.MaxHp = 100;
        session.Hp = 100;
        session.MaxMp = 300;
        session.Mp = 50;
        session.Inventory[InventoryConstants.InventoryStart].ItemId = potionItemId;
        session.Inventory[InventoryConstants.InventoryStart].Count = 5;
        session.Inventory[InventoryConstants.InventoryStart].Durability = 1;
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Effecting);
        packet.WriteInt(skillId);
        packet.WriteInt(session.CharacterId);
        packet.WriteInt(session.CharacterId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        await coordinator.HandleAsync(client, packet);

        session.Mp.Should().Be(170);
        session.Inventory[InventoryConstants.InventoryStart].Count.Should().Be(4);

        var stackPacket = sentPackets.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_COUNT_CHANGE);
        stackPacket.ResetOffset();
        stackPacket.ReadShort().Should().Be(1);
        stackPacket.ReadByte().Should().Be(1);
        stackPacket.ReadByte().Should().Be(0);
        stackPacket.ReadInt().Should().Be(potionItemId);
        stackPacket.ReadInt().Should().Be(4);

        sentPackets.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_MSP_CHANGE);
    }

    [Fact]
    public async Task MagicPacketCoordinator_HandleAsync_Type3HealthPotionRestoresHpAndConsumesItem()
    {
        const int skillId = 490010;
        const int potionItemId = 389010000;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(skillId).Returns(new MagicData
                {
                    Id = skillId,
                    Type1 = 3,
                    Moral = 1,
                    UseItem = potionItemId,
                    ItemGroup = 9
                });
                gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
                {
                    [skillId] = new()
                    {
                        Id = skillId,
                        DirectType = 1,
                        FirstDamage = 180
                    }
                });
                gameData.GetItem(potionItemId).Returns(new ItemData
                {
                    Num = potionItemId,
                    Kind = 97,
                    Countable = 1,
                    Weight = 1,
                    Duration = 1,
                    ReqLevelMax = 83
                });
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 542, accountId: 642);
        session.Class = 101;
        session.Level = 20;
        session.ZoneId = 1;
        session.X = 20;
        session.Z = 20;
        session.MaxHp = 500;
        session.Hp = 100;
        session.Inventory[InventoryConstants.InventoryStart].ItemId = potionItemId;
        session.Inventory[InventoryConstants.InventoryStart].Count = 2;
        session.Inventory[InventoryConstants.InventoryStart].Durability = 1;
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Effecting);
        packet.WriteInt(skillId);
        packet.WriteInt(session.CharacterId);
        packet.WriteInt(session.CharacterId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        await coordinator.HandleAsync(client, packet);

        session.Hp.Should().Be(280);
        session.Inventory[InventoryConstants.InventoryStart].Count.Should().Be(1);

        var stackPacket = sentPackets.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_COUNT_CHANGE);
        stackPacket.ResetOffset();
        stackPacket.ReadShort().Should().Be(1);
        stackPacket.ReadByte().Should().Be(1);
        stackPacket.ReadByte().Should().Be(0);
        stackPacket.ReadInt().Should().Be(potionItemId);
        stackPacket.ReadInt().Should().Be(1);

        sentPackets.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_HP_CHANGE);
    }

    [Fact]
    public async Task MagicPacketCoordinator_HandleAsync_Type4PotionConsumesItemAndAppliesBuff()
    {
        const int skillId = 610095;
        const int potionItemId = 900128000;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(skillId).Returns(new MagicData
                {
                    Id = skillId,
                    Type1 = 4,
                    Moral = 1,
                    UseItem = potionItemId,
                    ItemGroup = 9,
                    Etc = 0
                });
                gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data>
                {
                    [skillId] = new()
                    {
                        Id = skillId,
                        BuffType = (byte)BuffType.Ac,
                        Duration = 60,
                        Ac = 30
                    }
                });
                gameData.GetItem(potionItemId).Returns(new ItemData
                {
                    Num = potionItemId,
                    Kind = 97,
                    Countable = 1,
                    Weight = 1,
                    Duration = 1,
                    ReqLevelMax = 83
                });
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 543, accountId: 643);
        session.Class = 101;
        session.Level = 20;
        session.Strength = 50;
        session.Stamina = 50;
        session.Dexterity = 50;
        session.Intelligence = 50;
        session.Magic = 50;
        session.ZoneId = 1;
        session.X = 20;
        session.Z = 20;
        session.Inventory[InventoryConstants.InventoryStart].ItemId = potionItemId;
        session.Inventory[InventoryConstants.InventoryStart].Count = 3;
        session.Inventory[InventoryConstants.InventoryStart].Durability = 1;
        session.RecalculateStats(CreateBasicCoefficient(101), provider.GetRequiredService<IGameDataService>());
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Effecting);
        packet.WriteInt(skillId);
        packet.WriteInt(session.CharacterId);
        packet.WriteInt(session.CharacterId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        await coordinator.HandleAsync(client, packet);

        session.ActiveBuffs.Should().ContainKey(skillId);
        session.Inventory[InventoryConstants.InventoryStart].Count.Should().Be(2);

        var stackPacket = sentPackets.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_COUNT_CHANGE);
        stackPacket.ResetOffset();
        stackPacket.ReadShort().Should().Be(1);
        stackPacket.ReadByte().Should().Be(1);
        stackPacket.ReadByte().Should().Be(0);
        stackPacket.ReadInt().Should().Be(potionItemId);
        stackPacket.ReadInt().Should().Be(2);
    }

    [Fact]
    public void UserSession_RecalculateStats_Type4AcBuff_IgnoresNeutralHpMpPercentFields()
    {
        var gameData = Substitute.For<IGameDataService>();
        var coefficient = CreateBasicCoefficient(101);

        var baselineClient = Substitute.For<IClient>();
        baselineClient.Id.Returns(Guid.NewGuid());
        var baseline = new UserSession(baselineClient, 1, 1)
        {
            Class = 101,
            Level = 20,
            Strength = 50,
            Stamina = 50,
            Dexterity = 50,
            Intelligence = 50,
            Magic = 50
        };
        baseline.RecalculateStats(coefficient, gameData);

        var buffedClient = Substitute.For<IClient>();
        buffedClient.Id.Returns(Guid.NewGuid());
        var buffed = new UserSession(buffedClient, 2, 2)
        {
            Class = 101,
            Level = 20,
            Strength = 50,
            Stamina = 50,
            Dexterity = 50,
            Intelligence = 50,
            Magic = 50
        };
        buffed.ActiveBuffs[101007] = new ActiveBuff
        {
            MagicId = 101007,
            BuffType = BuffType.Ac,
            BonusAc = 50,
            BonusAcPct = 100,
            BonusMaxHpPct = 100,
            BonusMaxMpPct = 100,
            ExpireTicks = DateTime.UtcNow.AddMinutes(1).Ticks
        };

        buffed.RecalculateStats(coefficient, gameData);

        buffed.Stats.TotalAc.Should().Be((short)(baseline.Stats.TotalAc + 50));
        buffed.Stats.MaxHp.Should().Be(baseline.Stats.MaxHp);
        buffed.Stats.MaxMp.Should().Be(baseline.Stats.MaxMp);
    }

    [Fact]
    public void UserSession_RecalculateStats_Type4HpMpBuff_UsesPercentDeltaRelativeTo100()
    {
        var gameData = Substitute.For<IGameDataService>();
        var coefficient = CreateBasicCoefficient(101);

        var baselineClient = Substitute.For<IClient>();
        baselineClient.Id.Returns(Guid.NewGuid());
        var baseline = new UserSession(baselineClient, 1, 1)
        {
            Class = 101,
            Level = 20,
            Strength = 50,
            Stamina = 50,
            Dexterity = 50,
            Intelligence = 50,
            Magic = 50
        };
        baseline.RecalculateStats(coefficient, gameData);

        var buffedClient = Substitute.For<IClient>();
        buffedClient.Id.Returns(Guid.NewGuid());
        var buffed = new UserSession(buffedClient, 2, 2)
        {
            Class = 101,
            Level = 20,
            Strength = 50,
            Stamina = 50,
            Dexterity = 50,
            Intelligence = 50,
            Magic = 50
        };
        buffed.ActiveBuffs[610001] = new ActiveBuff
        {
            MagicId = 610001,
            BuffType = BuffType.HpMp,
            BonusMaxHpPct = 150,
            BonusMaxMpPct = 200,
            ExpireTicks = DateTime.UtcNow.AddMinutes(1).Ticks
        };

        buffed.RecalculateStats(coefficient, gameData);

        var expectedMaxHp = (short)Math.Clamp(
            baseline.Stats.MaxHp + baseline.Stats.MaxHp * 50 / 100,
            1,
            short.MaxValue);
        var expectedMaxMp = (short)Math.Clamp(
            baseline.Stats.MaxMp + baseline.Stats.MaxMp * 100 / 100,
            1,
            short.MaxValue);

        buffed.Stats.MaxHp.Should().Be(expectedMaxHp);
        buffed.Stats.MaxMp.Should().Be(expectedMaxMp);
    }

    [Fact]
    public async Task MagicExecutionService_CancelAsync_RebuildsSpecialStatesFromRemainingBuffs()
    {
        const int transformSkillId = 700101;
        const int stealthSkillId = 700102;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(transformSkillId).Returns(new MagicData
                {
                    Id = transformSkillId,
                    Type1 = 6,
                    Etc = 1
                });
                gameData.GetMagic(stealthSkillId).Returns(new MagicData
                {
                    Id = stealthSkillId,
                    Type1 = 9,
                    Etc = 2
                });
                gameData.MagicType6Table.Returns(new Dictionary<int, MagicType6Data>
                {
                    [1] = new()
                    {
                        Id = 1,
                        TransformId = 321,
                        Duration = 60
                    }
                });
                gameData.MagicType9Table.Returns(new Dictionary<int, MagicType9Data>
                {
                    [2] = new()
                    {
                        Id = 2,
                        StateChange = 1,
                        Duration = 60
                    }
                });
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 504, accountId: 604);
        session.Class = 101;
        session.Level = 20;
        session.ZoneId = 1;
        session.TransformId = 321;
        session.Invisibility = InvisibilityType.DispelOnMove;
        session.ActiveBuffs[transformSkillId] = new ActiveBuff
        {
            MagicId = transformSkillId,
            CasterId = session.CharacterId,
            Duration = 60,
            ExpireTicks = DateTime.UtcNow.AddMinutes(1).Ticks
        };
        session.ActiveBuffs[stealthSkillId] = new ActiveBuff
        {
            MagicId = stealthSkillId,
            CasterId = session.CharacterId,
            Duration = 60,
            ExpireTicks = DateTime.UtcNow.AddMinutes(1).Ticks
        };

        var executionService = provider.GetRequiredService<IMagicExecutionService>();
        await executionService.CancelAsync(session, transformSkillId);

        session.TransformId.Should().Be(0);
        session.Invisibility.Should().Be(InvisibilityType.DispelOnMove);
        session.ActiveBuffs.Should().NotContainKey(transformSkillId);

        await executionService.CancelAsync(session, stealthSkillId);

        session.Invisibility.Should().Be(InvisibilityType.None);
        session.ActiveBuffs.Should().NotContainKey(stealthSkillId);
    }

    [Fact]
    public async Task MagicExecutionService_ExecuteAsync_Type7NpcDamageRecordsAttacker()
    {
        const int skillId = 700201;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.MagicType7Table.Returns(new Dictionary<int, MagicType7Data>
                {
                    [1] = new()
                    {
                        Id = 1,
                        TargetChange = (byte)MagicAreaTargetChange.Provoke,
                        Damage = 80,
                        HitRate = AlwaysHits
                    }
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 505, accountId: 605);
        session.ZoneId = 1;
        session.Nation = AccountNation.Karus;
        session.Hp = 100;
        session.X = 50;
        session.Z = 50;
        sessionManager.Regions.AddToRegion(session);

        var npc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 2000,
            NpcType = 0,
            ZoneId = 1,
            X = 50,
            Z = 50,
            MaxHp = 200,
            Hp = 200,
            Ac = 0,
            EvadeRate = 1
        });

        var executionService = provider.GetRequiredService<IMagicExecutionService>();
        await executionService.ExecuteAsync(
            session,
            new MagicData
            {
                Id = skillId,
                Type1 = 7,
                Moral = 0,
                Etc = 1
            },
            skillId,
            npc.UniqueId,
            new int[7]);

        npc.DamageMap.Should().ContainKey(session.CharacterId);
        npc.DamageMap[session.CharacterId].Should().Be(80);
        npc.TopDamagerCharId.Should().Be(session.CharacterId);
    }

    [Theory]
    [InlineData(202003, 100)]
    [InlineData(202008, 120)]
    public async Task MagicExecutionService_ExecuteAsync_RogueArcherySkillsCanDamageNpc(int skillId, short addDamage)
    {
        const int bowItemId = 160210001;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetCoefficient(202).Returns(new CoefficientData
                {
                    ClassId = 202,
                    ShortSword = 0.00015,
                    Sword = 0.0001,
                    Axe = 0.0001,
                    Club = 0.0001,
                    Spear = 0.0001,
                    Bow = 0.00015,
                    Staff = 0.0001,
                    Hitrate = 0.01,
                    Evasionrate = 0.01,
                    Hp = 0.0005,
                    Mp = 0.0,
                    Ac = 1.0
                });
                gameData.GetItem(bowItemId).Returns(new ItemData
                {
                    Num = bowItemId,
                    Name = "Short Bow(+1)",
                    Kind = 70,
                    Slot = 4,
                    Damage = 15,
                    Delay = 150,
                    Range = 350,
                    Duration = 83
                });
                gameData.MagicType2Table.Returns(new Dictionary<int, MagicType2Data>
                {
                    [skillId] = new()
                    {
                        Id = skillId,
                        HitType = 0,
                        HitRate = AlwaysHits,
                        AddDamage = addDamage,
                        NeedArrow = 1
                    }
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var rogue = sessionManager.CreateSession(client, characterId: 506, accountId: 606);
        rogue.Name = "Rogue";
        rogue.Class = 202;
        rogue.Level = 20;
        rogue.Strength = 60;
        rogue.Stamina = 60;
        rogue.Dexterity = 80;
        rogue.Intelligence = 50;
        rogue.Magic = 50;
        rogue.ZoneId = 21;
        rogue.X = 542;
        rogue.Z = 377;
        rogue.Hp = 100;
        rogue.MaxHp = 100;
        rogue.Inventory[InventoryConstants.LeftHand].ItemId = bowItemId;
        rogue.Inventory[InventoryConstants.LeftHand].Durability = 83;
        rogue.Inventory[InventoryConstants.LeftHand].Count = 1;
        rogue.RecalculateStats(provider.GetRequiredService<IGameDataService>().GetCoefficient(202)!, provider.GetRequiredService<IGameDataService>());
        sessionManager.Regions.AddToRegion(rogue);

        var worm = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 750,
            Name = "Worm",
            NpcType = 0,
            ZoneId = 21,
            X = 543,
            Z = 377,
            SpawnX = 543,
            SpawnZ = 377,
            Hp = 200,
            MaxHp = 200,
            Ac = 5,
            EvadeRate = 1,
            AttackRange = 3,
            SearchRange = 8,
            TracingRange = 20
        });

        var executionService = provider.GetRequiredService<IMagicExecutionService>();

        for (var i = 0; i < 25 && worm.Hp == worm.MaxHp; i++)
        {
            await executionService.ExecuteAsync(
                rogue,
                new MagicData
                {
                    Id = skillId,
                    Type1 = 2,
                    Moral = 7,
                    Etc = 0
                },
                skillId,
                (short)worm.UniqueId,
                new int[7]);
        }

        worm.Hp.Should().BeLessThan(worm.MaxHp);
    }

    [Fact]
    public async Task MagicExecutionService_ExecuteAsync_Type3BlazeTicksDamageOnNpc()
    {
        const int skillId = 209509;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
                {
                    [skillId] = new()
                    {
                        Id = skillId,
                        DirectType = 1,
                        FirstDamage = 0,
                        TimeDamage = -280,
                        Duration = 20,
                        Attribute = 1
                    }
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var mage = sessionManager.CreateSession(client, characterId: 560, accountId: 660);
        mage.Name = "Mage";
        mage.Class = 101;
        mage.ZoneId = 21;
        mage.X = 542;
        mage.Z = 377;
        mage.Hp = 100;
        mage.MaxHp = 100;
        sessionManager.Regions.AddToRegion(mage);

        var worm = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 750,
            Name = "Worm",
            NpcType = 0,
            ZoneId = 21,
            X = 543,
            Z = 377,
            SpawnX = 543,
            SpawnZ = 377,
            Hp = 200,
            MaxHp = 200,
            Ac = 5,
            EvadeRate = 1
        });

        var executionService = provider.GetRequiredService<IMagicExecutionService>();
        await executionService.ExecuteAsync(
            mage,
            new MagicData
            {
                Id = skillId,
                Type1 = 3,
                Moral = 7,
                Etc = 0
            },
            skillId,
            worm.UniqueId,
            new int[7]);

        worm.Hp.Should().Be(200);
        worm.ActiveOverTimeEffects.Should().ContainKey(skillId);

        var buffExpiryService = new BuffExpiryService(
            sessionManager,
            executionService,
            provider.GetRequiredService<ICombatLifecycleService>(),
            provider.GetRequiredService<ICombatNotificationService>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BuffExpiryService>>());

        worm.ActiveOverTimeEffects[skillId].NextTickTicks = DateTime.UtcNow.Ticks;
        await buffExpiryService.ProcessTickAsync(DateTime.UtcNow.Ticks);

        // Exact damage depends on caster magic stats + formula tuning — assert
        // that the DOT tick reduced HP and recorded attribution to the caster.
        worm.Hp.Should().BeLessThan(200);
        worm.DamageMap.Should().ContainKey(mage.CharacterId);
        worm.DamageMap[mage.CharacterId].Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MagicExecutionService_ExecuteAsync_Type3FireBurstDamagesNearbyNpcInArea()
    {
        const int skillId = 209533;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
                {
                    [skillId] = new()
                    {
                        Id = skillId,
                        DirectType = 1,
                        FirstDamage = -588,
                        TimeDamage = 0,
                        Duration = 0,
                        Attribute = 1,
                        Radius = 8
                    }
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var mage = sessionManager.CreateSession(client, characterId: 561, accountId: 661);
        mage.Name = "Mage";
        mage.Class = 101;
        mage.ZoneId = 21;
        mage.X = 542;
        mage.Z = 377;
        mage.Hp = 100;
        mage.MaxHp = 100;
        sessionManager.Regions.AddToRegion(mage);

        var nearbyWorm = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 750,
            Name = "Nearby Worm",
            NpcType = 0,
            ZoneId = 21,
            X = 547,
            Z = 377,
            SpawnX = 547,
            SpawnZ = 377,
            Hp = 1000,
            MaxHp = 1000,
            Ac = 5,
            EvadeRate = 1
        });

        var farWorm = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 751,
            Name = "Far Worm",
            NpcType = 0,
            ZoneId = 21,
            X = 565,
            Z = 377,
            SpawnX = 565,
            SpawnZ = 377,
            Hp = 1000,
            MaxHp = 1000,
            Ac = 5,
            EvadeRate = 1
        });

        var executionService = provider.GetRequiredService<IMagicExecutionService>();
        await executionService.ExecuteAsync(
            mage,
            new MagicData
            {
                Id = skillId,
                Type1 = 3,
                Moral = 10,
                Etc = 0
            },
            skillId,
            -1,
            [(short)nearbyWorm.X, 0, (short)nearbyWorm.Z, 0, 0, 0, 0]);

        nearbyWorm.Hp.Should().BeLessThan(1000);
        farWorm.Hp.Should().Be(1000);
    }

    [Fact]
    public async Task MagicExecutionService_ExecuteAsync_Type3FireBurstDamagesNearbyNpcInLargeCoordinateArea()
    {
        const int skillId = 209533;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
                {
                    [skillId] = new()
                    {
                        Id = skillId,
                        DirectType = 1,
                        FirstDamage = -588,
                        TimeDamage = 0,
                        Duration = 0,
                        Attribute = 1,
                        Radius = 8
                    }
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var mage = sessionManager.CreateSession(client, characterId: 5611, accountId: 6611);
        mage.Name = "Mage";
        mage.Class = 101;
        mage.ZoneId = 2;
        mage.X = 1555;
        mage.Z = 405;
        mage.Hp = 100;
        mage.MaxHp = 100;
        sessionManager.Regions.AddToRegion(mage);

        // Nearby worm: at the AoE center, within radius 8 and within view distance
        var nearbyWorm = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 752,
            Name = "Nearby Worm",
            NpcType = 0,
            ZoneId = 2,
            X = 1553,
            Z = 407,
            SpawnX = 1553,
            SpawnZ = 407,
            Hp = 1000,
            MaxHp = 1000,
            Ac = 5,
            EvadeRate = 1
        });

        // Far worm: within view distance but outside AoE radius 8 (~25 units from center)
        var farWorm = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 753,
            Name = "Far Worm",
            NpcType = 0,
            ZoneId = 2,
            X = 1575,
            Z = 420,
            SpawnX = 1575,
            SpawnZ = 420,
            Hp = 1000,
            MaxHp = 1000,
            Ac = 5,
            EvadeRate = 1
        });

        var executionService = provider.GetRequiredService<IMagicExecutionService>();
        await executionService.ExecuteAsync(
            mage,
            new MagicData
            {
                Id = skillId,
                Type1 = 3,
                Moral = 10,
                Etc = 0
            },
            skillId,
            -1,
            [1553, 15, 407, -101, 4, 0, 0]);

        nearbyWorm.Hp.Should().BeLessThan(1000);
        farWorm.Hp.Should().Be(1000);
    }

    [Fact]
    public async Task MagicPacketCoordinator_HandleAsync_Type3Subtype4SchedulesBlazeDotOnNpc()
    {
        const int skillId = 209509;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(skillId).Returns(new MagicData
                {
                    Id = skillId,
                    Type1 = 3,
                    Moral = 7,
                    Etc = 0
                });
                gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
                {
                    [skillId] = new()
                    {
                        Id = skillId,
                        DirectType = 1,
                        FirstDamage = 0,
                        TimeDamage = -280,
                        Duration = 20,
                        Attribute = 1
                    }
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var mage = sessionManager.CreateSession(client, characterId: 562, accountId: 662);
        mage.Name = "Mage";
        mage.Class = 101;
        mage.ZoneId = 21;
        mage.X = 542;
        mage.Z = 377;
        mage.Hp = 100;
        mage.MaxHp = 100;
        sessionManager.Regions.AddToRegion(mage);

        var worm = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 750,
            Name = "Worm",
            NpcType = 0,
            ZoneId = 21,
            X = 543,
            Z = 377,
            SpawnX = 543,
            SpawnZ = 377,
            Hp = 200,
            MaxHp = 200,
            Ac = 5,
            EvadeRate = 1
        });

        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Fail);
        packet.WriteInt(skillId);
        packet.WriteInt(mage.CharacterId);
        packet.WriteInt(worm.UniqueId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        await coordinator.HandleAsync(client, packet);

        worm.ActiveOverTimeEffects.Should().ContainKey(skillId);

        var executionService = provider.GetRequiredService<IMagicExecutionService>();
        var buffExpiryService = new BuffExpiryService(
            sessionManager,
            executionService,
            provider.GetRequiredService<ICombatLifecycleService>(),
            provider.GetRequiredService<ICombatNotificationService>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BuffExpiryService>>());

        worm.ActiveOverTimeEffects[skillId].NextTickTicks = DateTime.UtcNow.Ticks;
        await buffExpiryService.ProcessTickAsync(DateTime.UtcNow.Ticks);

        worm.Hp.Should().BeLessThan(200);
    }

    [Fact]
    public async Task MagicPacketCoordinator_HandleAsync_Type3Subtype4OverridesQueuedAreaExecution()
    {
        const int skillId = 209533;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(skillId).Returns(new MagicData
                {
                    Id = skillId,
                    Type1 = 3,
                    Moral = 10,
                    Etc = 0
                });
                gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
                {
                    [skillId] = new()
                    {
                        Id = skillId,
                        DirectType = 1,
                        FirstDamage = -588,
                        TimeDamage = 0,
                        Duration = 0,
                        Attribute = 1,
                        Radius = 8
                    }
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var mage = sessionManager.CreateSession(client, characterId: 563, accountId: 663);
        mage.Name = "Mage";
        mage.Class = 101;
        mage.ZoneId = 21;
        mage.X = 542;
        mage.Z = 377;
        mage.Hp = 100;
        mage.MaxHp = 100;
        sessionManager.Regions.AddToRegion(mage);

        var primaryWorm = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 750,
            Name = "Primary Worm",
            NpcType = 0,
            ZoneId = 21,
            X = 547,
            Z = 377,
            SpawnX = 547,
            SpawnZ = 377,
            Hp = 1000,
            MaxHp = 1000,
            Ac = 5,
            EvadeRate = 1
        });

        var nearbyWorm = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 751,
            Name = "Nearby Worm",
            NpcType = 0,
            ZoneId = 21,
            X = 550,
            Z = 377,
            SpawnX = 550,
            SpawnZ = 377,
            Hp = 1000,
            MaxHp = 1000,
            Ac = 5,
            EvadeRate = 1
        });

        var farWorm = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 752,
            Name = "Far Worm",
            NpcType = 0,
            ZoneId = 21,
            X = 565,
            Z = 377,
            SpawnX = 565,
            SpawnZ = 377,
            Hp = 1000,
            MaxHp = 1000,
            Ac = 5,
            EvadeRate = 1
        });

        var effectingPacket = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        effectingPacket.WriteByte((byte)MagicProcessOpcode.Effecting);
        effectingPacket.WriteInt(skillId);
        effectingPacket.WriteInt(mage.CharacterId);
        effectingPacket.WriteInt(primaryWorm.UniqueId);
        for (var i = 0; i < 7; i++)
            effectingPacket.WriteInt(0);

        var finalizePacket = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        finalizePacket.WriteByte((byte)MagicProcessOpcode.Fail);
        finalizePacket.WriteInt(skillId);
        finalizePacket.WriteInt(mage.CharacterId);
        finalizePacket.WriteInt(primaryWorm.UniqueId);
        finalizePacket.WriteInt((short)primaryWorm.X);
        finalizePacket.WriteInt(5);
        finalizePacket.WriteInt((short)primaryWorm.Z);
        finalizePacket.WriteInt(-101);
        finalizePacket.WriteInt(1);
        finalizePacket.WriteInt(0);
        finalizePacket.WriteInt(0);

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        await coordinator.HandleAsync(client, effectingPacket);
        await coordinator.HandleAsync(client, finalizePacket);
        await Task.Delay(250);

        primaryWorm.Hp.Should().BeLessThan(1000);
        nearbyWorm.Hp.Should().BeLessThan(1000);
        farWorm.Hp.Should().Be(1000);
    }

    [Fact]
    public async Task MagicPacketCoordinator_HandleAsync_Type3AreaKillStillBroadcastsGroundImpactEffect()
    {
        const int skillId = 209533;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(skillId).Returns(new MagicData
                {
                    Id = skillId,
                    Type1 = 3,
                    Moral = 10,
                    Etc = 0
                });
                gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
                {
                    [skillId] = new()
                    {
                        Id = skillId,
                        DirectType = 1,
                        FirstDamage = -588,
                        TimeDamage = 0,
                        Duration = 0,
                        Attribute = 1,
                        Radius = 8
                    }
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var mage = sessionManager.CreateSession(client, characterId: 5631, accountId: 6631);
        mage.Name = "Mage";
        mage.Class = 101;
        mage.ZoneId = 21;
        mage.X = 542;
        mage.Z = 377;
        mage.Hp = 100;
        mage.MaxHp = 100;
        sessionManager.Regions.AddToRegion(mage);

        var doomedWorm = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 754,
            Name = "Doomed Worm",
            NpcType = 0,
            ZoneId = 21,
            X = 547,
            Z = 377,
            SpawnX = 547,
            SpawnZ = 377,
            // Low HP so any positive damage roll lethals — the test asserts the
            // ground-impact effect still broadcasts when an area cast kills its target.
            Hp = 1,
            MaxHp = 1,
            Ac = 0,
            EvadeRate = 1
        });

        var finalizePacket = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        finalizePacket.WriteByte((byte)MagicProcessOpcode.Fail);
        finalizePacket.WriteInt(skillId);
        finalizePacket.WriteInt(mage.CharacterId);
        finalizePacket.WriteInt(doomedWorm.UniqueId);
        finalizePacket.WriteInt((short)doomedWorm.X);
        finalizePacket.WriteInt(5);
        finalizePacket.WriteInt((short)doomedWorm.Z);
        finalizePacket.WriteInt(-101);
        finalizePacket.WriteInt(1);
        finalizePacket.WriteInt(0);
        finalizePacket.WriteInt(0);

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        await coordinator.HandleAsync(client, finalizePacket);

        doomedWorm.Hp.Should().Be(0);

        var magicPackets = sentPackets
            .Where(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_MAGIC_PROCESS)
            .Select(ReadMagicProcessPacket)
            .Where(packet => packet.ProcessOpcode == MagicProcessOpcode.Effecting && packet.SkillId == skillId)
            .ToList();

        magicPackets.Should().Contain(packet =>
            packet.TargetId == -1
            && packet.Data[0] == (short)doomedWorm.X
            && packet.Data[2] == (short)doomedWorm.Z);
        magicPackets.Should().Contain(packet => packet.TargetId == doomedWorm.UniqueId);
    }

    [Fact]
    public async Task MagicPacketCoordinator_HandleAsync_Type3MissPacketDoesNotDamageCaster()
    {
        const int skillId = 209545;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(skillId).Returns(new MagicData
                {
                    Id = skillId,
                    Type1 = 3,
                    Moral = 10,
                    Etc = 0
                });
                gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
                {
                    [skillId] = new()
                    {
                        Id = skillId,
                        DirectType = 1,
                        FirstDamage = -504,
                        TimeDamage = 0,
                        Duration = 0,
                        Attribute = 1,
                        Radius = 15
                    }
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var mage = sessionManager.CreateSession(client, characterId: 564, accountId: 664);
        mage.Name = "Mage";
        mage.Class = 101;
        mage.ZoneId = 21;
        mage.X = 675.7f;
        mage.Z = 426.7f;
        mage.Hp = 1000;
        mage.MaxHp = 1000;
        sessionManager.Regions.AddToRegion(mage);

        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Fail);
        packet.WriteInt(skillId);
        packet.WriteInt(mage.CharacterId);
        packet.WriteInt(mage.CharacterId);
        packet.WriteInt(0);
        packet.WriteInt(0);
        packet.WriteInt(0);
        packet.WriteInt(-100);
        packet.WriteInt(0);
        packet.WriteInt(0);
        packet.WriteInt(0);

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        await coordinator.HandleAsync(client, packet);
        await Task.Delay(200);

        mage.Hp.Should().Be(1000);
        mage.ActiveOverTimeEffects.Should().BeEmpty();
    }

    [Fact]
    public void UserSession_LoadSavedMagic_HydratesBuffEffectsFromGameData()
    {
        const int armorSkillId = 700301;
        const int transformSkillId = 700302;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(armorSkillId).Returns(new MagicData
                {
                    Id = armorSkillId,
                    Type1 = 4,
                    Etc = 0
                });
                gameData.GetMagic(transformSkillId).Returns(new MagicData
                {
                    Id = transformSkillId,
                    Type1 = 6,
                    Etc = 0
                });
                gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data>
                {
                    [armorSkillId] = new()
                    {
                        Id = armorSkillId,
                        BuffType = 2,
                        Ac = 30,
                        AcPct = 100,
                        Attack = 100,
                        Duration = 120
                    }
                });
                gameData.MagicType6Table.Returns(new Dictionary<int, MagicType6Data>
                {
                    [transformSkillId] = new()
                    {
                        Id = transformSkillId,
                        TransformId = 555,
                        Duration = 90
                    }
                });
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
            });

        var gameData = provider.GetRequiredService<IGameDataService>();

        var sourceClient = Substitute.For<IClient>();
        sourceClient.Id.Returns(Guid.NewGuid());
        var source = new UserSession(sourceClient, 1, 1);
        source.ActiveBuffs[armorSkillId] = new ActiveBuff
        {
            MagicId = armorSkillId,
            CasterId = 99,
            Duration = 120,
            ExpireTicks = DateTime.UtcNow.AddMinutes(2).Ticks,
            BonusAc = 30
        };
        source.ActiveBuffs[transformSkillId] = new ActiveBuff
        {
            MagicId = transformSkillId,
            CasterId = 99,
            Duration = 90,
            ExpireTicks = DateTime.UtcNow.AddMinutes(2).Ticks
        };

        var serialized = source.SerializeSavedMagic();

        var loadedClient = Substitute.For<IClient>();
        loadedClient.Id.Returns(Guid.NewGuid());
        var loaded = new UserSession(loadedClient, 2, 2)
        {
            Class = 101,
            Level = 20,
            Strength = 50,
            Stamina = 50,
            Dexterity = 50,
            Intelligence = 50,
            Magic = 50
        };

        var baselineClient = Substitute.For<IClient>();
        baselineClient.Id.Returns(Guid.NewGuid());
        var baseline = new UserSession(baselineClient, 3, 3)
        {
            Class = 101,
            Level = 20,
            Strength = 50,
            Stamina = 50,
            Dexterity = 50,
            Intelligence = 50,
            Magic = 50
        };

        loaded.LoadSavedMagic(serialized, gameData);

        var coefficient = CreateBasicCoefficient(101);
        baseline.RecalculateStats(coefficient, gameData);
        loaded.RecalculateStats(coefficient, gameData);

        loaded.ActiveBuffs[armorSkillId].BonusAc.Should().Be(30);
        loaded.Stats.TotalAc.Should().Be((short)(baseline.Stats.TotalAc + 30));
        loaded.TransformId.Should().Be((short)555);
    }

    [Fact]
    public void UserSession_LoadSavedMagic_KeepsAttackAmountOnRestoredDamageBuff()
    {
        const int attackScrollId = 501160;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(attackScrollId).Returns(new MagicData
                {
                    Id = attackScrollId,
                    Type1 = 4,
                    Etc = 0
                });
                gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data>
                {
                    [attackScrollId] = new()
                    {
                        Id = attackScrollId,
                        BuffType = (byte)BuffType.Damage,
                        AcPct = 100,
                        Attack = 155,
                        Duration = 1800
                    }
                });
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
            });

        var gameData = provider.GetRequiredService<IGameDataService>();

        var sourceClient = Substitute.For<IClient>();
        sourceClient.Id.Returns(Guid.NewGuid());
        var source = new UserSession(sourceClient, 1, 1);
        source.ActiveBuffs[attackScrollId] = new ActiveBuff
        {
            MagicId = attackScrollId,
            CasterId = 1,
            BuffType = BuffType.Damage,
            Duration = 1800,
            ExpireTicks = DateTime.UtcNow.AddMinutes(30).Ticks,
            BonusAttack = 155
        };

        var serialized = source.SerializeSavedMagic();

        var loadedClient = Substitute.For<IClient>();
        loadedClient.Id.Returns(Guid.NewGuid());
        var loaded = new UserSession(loadedClient, 2, 2)
        {
            Class = 101,
            Level = 20,
            Strength = 50,
            Stamina = 50,
            Dexterity = 50,
            Intelligence = 50,
            Magic = 50
        };

        loaded.LoadSavedMagic(serialized, gameData);
        loaded.RecalculateStats(CreateBasicCoefficient(101), gameData);

        loaded.ActiveBuffs[attackScrollId].BonusAttack.Should().Be(155);
        loaded.AttackAmount.Should().Be(155);
    }

}
