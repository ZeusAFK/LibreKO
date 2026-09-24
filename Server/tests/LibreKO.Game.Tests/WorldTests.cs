using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Reflection;

namespace LibreKO.Game.Tests;

public class WorldTests : GameTestBase
{
    [Fact]
    public void NpcInstance_UsesNpcSpawnStyle_DistinguishesMonsterFromNpcStyleSpawn()
    {
        var normalMonster = new NpcInstance { IsMonster = true, NpcId = 750, ZoneId = 21, SpawnActType = 1 };
        normalMonster.UsesNpcSpawnStyle.Should().BeFalse();

        var captain = new NpcInstance { IsMonster = true, NpcId = 8644, ZoneId = 21, SpawnActType = 1 };
        captain.UsesNpcSpawnStyle.Should().BeFalse();

        var npcStyleSpawn = new NpcInstance { NpcId = 25000, ZoneId = 21, SpawnActType = 100 };
        npcStyleSpawn.UsesNpcSpawnStyle.Should().BeTrue();
    }

    [Fact]
    public void GameServerBootstrapper_ShouldSpawnObjectEventNpcFromMapData()
    {
        var gameData = Substitute.For<IGameDataService>();
        gameData.GetNpc(1019, false).Returns(new NpcData
        {
            Id = 1019,
            Name = "Bind Stone",
            NpcType = 0,
            ModelId = 1019,
            Hp = 1
        });

        var mapManager = CreateMapManagerWithObjectEvent(21, new ObjectEvent
        {
            Index = 1019,
            Type = 0,
            Status = 0,
            PosX = 673.1f,
            PosY = -8.7f,
            PosZ = 179.5f
        });

        var sessionManager = new SessionManager
        {
            Maps = mapManager
        };

        var bootstrapper = new LibreKO.Game.Startup.GameServerBootstrapper(
            gameData,
            sessionManager,
            mapManager,
            Substitute.For<IAccountLockService>(),
            Substitute.For<IServiceScopeFactory>(),
            TestHostEnvironmentFactory.Create(),
            Microsoft.Extensions.Options.Options.Create(new GameServerSettings
            {
                Version = 2618
            }),
            Substitute.For<IMonsterAggressionPolicy>(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<LibreKO.Game.Startup.GameServerBootstrapper>>());

        var spawnMethod = typeof(LibreKO.Game.Startup.GameServerBootstrapper).GetMethod(
            "SpawnObjectEventNpcs",
            BindingFlags.Instance | BindingFlags.NonPublic);
        spawnMethod.Should().NotBeNull();

        gameData.ZoneInfoTable.Returns(new Dictionary<short, ZoneInfoData>
        {
            [21] = new()
        });

        spawnMethod!.Invoke(bootstrapper, [gameData]);

        var bindStone = sessionManager.Regions.GetNpcByProtoId(21, 1019);
        bindStone.Should().NotBeNull();
        bindStone!.NpcId.Should().Be(1019);
        bindStone.X.Should().BeApproximately(673.1f, 0.01f);
        bindStone.Z.Should().BeApproximately(179.5f, 0.01f);
        bindStone.IsNpc.Should().BeTrue();
        bindStone.HasAi.Should().BeFalse();
        bindStone.ObjectType.Should().Be(1);
    }

    [Fact]
    public void NpcInstance_ObjectEventNpcNeverInheritsMonsterIdentity()
    {
        var treeGhost = new NpcData
        {
            Id = 5001,
            Name = "Tree ghost",
            NpcType = 0,
            IsMonster = true,
            ModelId = 5210,
            Money = 250,
            Experience = 1200,
            ItemGroup = 5001,
            Hp = 5000
        };

        var instance = NpcInstance.FromObjectEvent(
            treeGhost,
            new ObjectEvent { Index = 5001, Type = 8, Belong = 0, PosX = 816, PosZ = 607 },
            21,
            1);

        instance.ObjectType.Should().Be(NpcInstance.MapObjectType);
        instance.ModelId.Should().Be(0);
        instance.GoldDrop.Should().Be(0);
        instance.Experience.Should().Be(0);
        instance.DropItemGroup.Should().Be(0);
        instance.IsMonster.Should().BeFalse();
        instance.IsAttackable.Should().BeFalse();
        instance.Nation.Should().NotBe((byte)0);
    }

    [Fact]
    public void NpcInstance_ObjectEventNpcKeepsModelOfARealObjectRow()
    {
        var gate = new NpcData
        {
            Id = 1101,
            Name = "Gate",
            NpcType = 50,
            IsMonster = false,
            ModelId = 1101,
            Hp = 50000
        };

        var instance = NpcInstance.FromObjectEvent(
            gate,
            new ObjectEvent { Index = 1101, Type = 1, Belong = 1 },
            30,
            2);

        instance.ModelId.Should().Be(1101);
        instance.MaxHp.Should().Be(50000);
        instance.Nation.Should().Be(EntityNation.Karus);
    }

    [Fact]
    public void GameServerBootstrapper_ShouldNotSpawnNpcForAnvilObjectEvent()
    {
        var filter = typeof(LibreKO.Game.Startup.GameServerBootstrapper).GetMethod(
            "ShouldSpawnObjectEventNpc",
            BindingFlags.Static | BindingFlags.NonPublic);
        filter.Should().NotBeNull();

        filter!.Invoke(null, [new ObjectEvent { Index = 5001, Type = 8, Status = 1 }])
            .Should().Be(false);
        filter.Invoke(null, [new ObjectEvent { Index = 1019, Type = 0 }])
            .Should().Be(true);
    }

    [Fact]
    public void GameServerBootstrapper_ShouldSpawnSharedMapVariantNpcsFromBaseZone()
    {
        var gameData = Substitute.For<IGameDataService>();
        gameData.ZoneInfoTable.Returns(new Dictionary<short, ZoneInfoData>
        {
            [21] = new() { ZoneNo = 21, MapName = "Moradon", SmdName = "moradon_0826.smd" },
            [22] = new() { ZoneNo = 22, MapName = "Moradon II", SmdName = "moradon_0826.smd" }
        });
        gameData.NpcPositions.Returns(
        [
            new NpcPosData
            {
                ZoneId = 21,
                NpcId = 19000,
                LeftX = 816,
                TopZ = 607,
                NumNPC = 1
            }
        ]);
        gameData.GetSpawnProto(Arg.Is<NpcPosData>(pos => pos.NpcId == 19000)).Returns(new NpcData
        {
            Id = 19000,
            Name = "Moradon Merchant",
            NpcType = 1,
            Hp = 100
        });

        var sessionManager = new SessionManager();
        var mapManager = new MapManager(Substitute.For<Microsoft.Extensions.Logging.ILogger<MapManager>>());
        var bootstrapper = new LibreKO.Game.Startup.GameServerBootstrapper(
            gameData,
            sessionManager,
            mapManager,
            Substitute.For<IAccountLockService>(),
            Substitute.For<IServiceScopeFactory>(),
            TestHostEnvironmentFactory.Create(),
            Microsoft.Extensions.Options.Options.Create(new GameServerSettings
            {
                Version = 2618
            }),
            Substitute.For<IMonsterAggressionPolicy>(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<LibreKO.Game.Startup.GameServerBootstrapper>>());

        var spawnMethod = typeof(LibreKO.Game.Startup.GameServerBootstrapper).GetMethod(
            "SpawnNpcs",
            BindingFlags.Instance | BindingFlags.NonPublic);
        spawnMethod.Should().NotBeNull();

        spawnMethod!.Invoke(bootstrapper, [gameData]);

        sessionManager.Regions.GetNpcByProtoId(21, 19000).Should().NotBeNull();
        sessionManager.Regions.GetNpcByProtoId(22, 19000).Should().NotBeNull();
    }

    [Theory]
    [InlineData(219)]
    [InlineData(212)]
    [InlineData(176)]
    [InlineData(62)]
    public void NpcInstance_FromData_AMonsterTableRowStaysAMonsterWhateverItsNpcType(byte npcType)
    {
        var proto = new NpcData
        {
            Id = 5301,
            Name = "Ancient",
            NpcType = npcType,
            IsMonster = true,
            ModelId = 5100,
            Hp = 900900,
            Level = 83,
            ActType = 1,
            SearchRange = 10,
        };

        var position = new NpcPosData
        {
            ZoneId = 2,
            NpcId = 5301,
            ActType = 1,
            LeftX = 811,
            TopZ = 1823
        };

        var instance = NpcInstance.FromData(proto, position, uniqueId: 1);

        instance.IsMonster.Should().BeTrue();
        instance.IsNpc.Should().BeFalse();
        instance.IsAttackable.Should().BeTrue();
        instance.HasAi.Should().BeTrue();
    }

    [Fact]
    public void NpcInstance_FromData_TreatsNpcStyleSpawnAsNpc()
    {
        var npcData = new NpcData
        {
            Id = 999001,
            Name = "QuestNpc",
            NpcType = 0,
            Group = 3,
            Hp = 30000,
            Attack1 = 3000,
            ActType = 7,
            SearchRange = 14,
            AttackRange = 7
        };

        var npcPosition = new NpcPosData
        {
            ZoneId = 21,
            NpcId = 999001,
            ActType = 100,
            LeftX = 804,
            TopZ = 551
        };

        var instance = NpcInstance.FromData(npcData, npcPosition, uniqueId: 1);

        instance.UsesNpcSpawnStyle.Should().BeTrue();
        instance.IsNpc.Should().BeTrue();
        instance.IsMonster.Should().BeFalse();
        instance.IsAttackable.Should().BeFalse();
        instance.HasAi.Should().BeFalse();
    }

    [Fact]
    public async Task GracefulShutdownService_StopAsync_LogsOutAllActiveSessions()
    {
        var sessionManager = new SessionManager();

        var client1 = Substitute.For<IClient>();
        client1.Id.Returns(Guid.NewGuid());
        sessionManager.CreateSession(client1, characterId: 901, accountId: 1901);

        var client2 = Substitute.For<IClient>();
        client2.Id.Returns(Guid.NewGuid());
        sessionManager.CreateSession(client2, characterId: 902, accountId: 1902);

        var terminationService = Substitute.For<ISessionTerminationService>();
        terminationService.LogoutAsync(Arg.Any<IClient>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var accountLockService = Substitute.For<IAccountLockService>();
        accountLockService.ClearOwnClaimsAsync().Returns(Task.CompletedTask);

        var service = new GracefulShutdownService(
            sessionManager,
            terminationService,
            accountLockService,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<GracefulShutdownService>>());

        await service.StopAsync(CancellationToken.None);

        await terminationService.Received(1).LogoutAsync(client1, Arg.Any<CancellationToken>());
        await terminationService.Received(1).LogoutAsync(client2, Arg.Any<CancellationToken>());
        await accountLockService.Received(1).ClearOwnClaimsAsync();
    }

    [Fact]
    public async Task WorldPacketCoordinator_HandleMoveAsync_BroadcastsTheDestinationLedOneStepAhead()
    {
        using var provider = CreateProvider(_ => { });

        var moverClient = Substitute.For<IClient>();
        moverClient.Id.Returns(Guid.NewGuid());
        moverClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var viewerClient = Substitute.For<IClient>();
        viewerClient.Id.Returns(Guid.NewGuid());
        var viewerPackets = new List<Packet>();
        viewerClient.SendPacket(Arg.Do<Packet>(packet => viewerPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var mover = sessionManager.CreateSession(moverClient, characterId: 1510, accountId: 1610);
        mover.Name = "Mover";
        mover.ZoneId = 1;
        mover.X = 55;
        mover.Z = 8;
        mover.Hp = 100;
        sessionManager.Regions.AddToRegion(mover);

        var viewer = sessionManager.CreateSession(viewerClient, characterId: 1520, accountId: 1620);
        viewer.Name = "Viewer";
        viewer.ZoneId = 1;
        viewer.X = 55.5f;
        viewer.Z = 8.5f;
        viewer.Hp = 100;
        sessionManager.Regions.AddToRegion(viewer);

        var packet = new Packet(GameOpcodes.GS_MOVE);
        packet.WriteUShort(600);
        packet.WriteUShort(80);
        packet.WriteUShort(0);
        packet.WriteShort(45);
        packet.WriteByte(3);
        packet.WriteUShort(550);
        packet.WriteUShort(80);
        packet.WriteUShort(0);

        var coordinator = provider.GetRequiredService<IWorldPacketCoordinator>();
        await coordinator.HandleMoveAsync(moverClient, packet);
        provider.GetRequiredService<MovementBroadcastService>().FlushMovers();

        mover.X.Should().Be(
            60.4f, because: "the destination is led one speed step along the path of travel");
        mover.Z.Should().Be(8);
        mover.MoveOldWillX.Should().Be(604);
        mover.MoveOldWillZ.Should().Be(80);

        var movePacket = viewerPackets.Should().ContainSingle(p => p.GetOpcode() == (byte)GameOpcodes.GS_MOVE).Subject;
        movePacket.ResetOffset();
        movePacket.ReadInt().Should().Be(mover.CharacterId);
        movePacket.ReadUShort().Should().Be(604);
        movePacket.ReadUShort().Should().Be(80);
        movePacket.ReadUShort().Should().Be(0);
        movePacket.ReadShort().Should().Be(45);
        movePacket.ReadByte().Should().Be(3);
        movePacket.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task WorldPacketCoordinator_HandleMoveAsync_LeadsTheStartOfAMoveLikeAnyOther()
    {
        using var provider = CreateProvider(_ => { });

        var moverClient = Substitute.For<IClient>();
        moverClient.Id.Returns(Guid.NewGuid());
        moverClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var viewerClient = Substitute.For<IClient>();
        viewerClient.Id.Returns(Guid.NewGuid());
        Packet? movePacket = null;
        viewerClient.SendPacket(
                Arg.Do<Packet>(packet =>
                {
                    if (packet.GetOpcode() == (byte)GameOpcodes.GS_MOVE)
                        movePacket = packet;
                }),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var mover = sessionManager.CreateSession(moverClient, characterId: 1530, accountId: 1630);
        mover.Name = "Starter";
        mover.ZoneId = 1;
        mover.X = 50;
        mover.Z = 8;
        mover.Hp = 100;
        sessionManager.Regions.AddToRegion(mover);

        var viewer = sessionManager.CreateSession(viewerClient, characterId: 1540, accountId: 1640);
        viewer.Name = "Viewer";
        viewer.ZoneId = 1;
        viewer.X = 50.5f;
        viewer.Z = 8.5f;
        viewer.Hp = 100;
        sessionManager.Regions.AddToRegion(viewer);

        var packet = new Packet(GameOpcodes.GS_MOVE);
        packet.WriteUShort(600);
        packet.WriteUShort(80);
        packet.WriteUShort(0);
        packet.WriteShort(45);
        packet.WriteByte(1);
        packet.WriteUShort(500);
        packet.WriteUShort(80);
        packet.WriteUShort(0);

        var coordinator = provider.GetRequiredService<IWorldPacketCoordinator>();
        await coordinator.HandleMoveAsync(moverClient, packet);
        provider.GetRequiredService<MovementBroadcastService>().FlushMovers();

        mover.X.Should().Be(
            60.4f, because: "the first packet of a move is not halved any more, only led");
        mover.Z.Should().Be(8);
        mover.MoveOldWillX.Should().Be(604);

        movePacket.Should().NotBeNull();
        movePacket!.ResetOffset();
        movePacket.ReadInt().Should().Be(mover.CharacterId);
        movePacket.ReadUShort().Should().Be(604);
        movePacket.ReadUShort().Should().Be(80);
        movePacket.ReadUShort().Should().Be(0);
        movePacket.ReadShort().Should().Be(45);
        movePacket.ReadByte().Should().Be(1);
        movePacket.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task WorldPacketCoordinator_HandleStateChangeAsync_TracksSitStateAndBroadcasts()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 151, accountId: 161);
        session.ZoneId = 1;
        session.X = 10;
        session.Z = 10;
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_STATE_CHANGE);
        packet.WriteByte(1);
        packet.WriteByte(2);

        var coordinator = provider.GetRequiredService<IWorldPacketCoordinator>();
        await coordinator.HandleStateChangeAsync(client, packet);

        session.IsSitting.Should().BeTrue();
        sentPacket.Should().NotBeNull();
        sentPacket!.GetOpcode().Should().Be((byte)GameOpcodes.GS_STATE_CHANGE);
        sentPacket.ResetOffset();
        // uint32 character id.
        sentPacket.ReadInt().Should().Be(session.CharacterId);
        sentPacket.ReadByte().Should().Be(1);
        sentPacket.ReadByte().Should().Be(2);
    }

    [Fact]
    public async Task WorldPacketCoordinator_BroadcastUserInOutAsync_UsesTheShortOutLayout()
    {
        using var provider = CreateProvider(_ => { });

        var senderClient = Substitute.For<IClient>();
        senderClient.Id.Returns(Guid.NewGuid());
        senderClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var viewerClient = Substitute.For<IClient>();
        viewerClient.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        viewerClient.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();

        var sender = sessionManager.CreateSession(senderClient, characterId: 1710, accountId: 1810);
        sender.Name = "Sender";
        sender.ZoneId = 1;
        sender.X = 8;
        sender.Z = 8;
        sessionManager.Regions.AddToRegion(sender);

        var viewer = sessionManager.CreateSession(viewerClient, characterId: 1720, accountId: 1820);
        viewer.Name = "Viewer";
        viewer.ZoneId = 1;
        viewer.X = 8.5f;
        viewer.Z = 8.5f;
        sessionManager.Regions.AddToRegion(viewer);

        var coordinator = provider.GetRequiredService<IWorldPacketCoordinator>();
        await coordinator.BroadcastUserInOutAsync(sender, InOutType.Out);

        sentPacket.Should().NotBeNull();
        sentPacket!.GetOpcode().Should().Be((byte)GameOpcodes.GS_USER_INOUT);
        sentPacket.ResetOffset();
        sentPacket.ReadByte().Should().Be((byte)InOutType.Out);
        sentPacket.ReadByte().Should().Be(0);
        sentPacket.ReadInt().Should().Be(sender.CharacterId);
        sentPacket.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task WorldPacketCoordinator_HandleReqUserInAsync_SendsNearbyUserSnapshot()
    {
        using var provider = CreateProvider(_ => { });

        var requesterClient = Substitute.For<IClient>();
        requesterClient.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        requesterClient.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var nearbyClient = Substitute.For<IClient>();
        nearbyClient.Id.Returns(Guid.NewGuid());
        nearbyClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();

        var requester = sessionManager.CreateSession(requesterClient, characterId: 171, accountId: 181);
        requester.Name = "Requester";
        requester.ZoneId = 1;
        requester.X = 8;
        requester.Z = 8;
        sessionManager.Regions.AddToRegion(requester);

        var nearby = sessionManager.CreateSession(nearbyClient, characterId: 172, accountId: 182);
        nearby.Name = "Nearby";
        nearby.ZoneId = 1;
        nearby.Nation = AccountNation.Karus;
        nearby.Race = 1;
        nearby.Class = 101;
        nearby.Level = 30;
        nearby.Face = 2;
        nearby.X = 8.5f;
        nearby.Z = 8.5f;
        sessionManager.Regions.AddToRegion(nearby);

        var request = new Packet(GameOpcodes.GS_REQ_USERIN);
        request.WriteUShort(1);
        request.WriteInt(nearby.CharacterId);

        var coordinator = provider.GetRequiredService<IWorldPacketCoordinator>();
        await coordinator.HandleReqUserInAsync(requesterClient, request);

        sentPackets.Should().ContainSingle();
        sentPackets[0].GetOpcode().Should().Be((byte)GameOpcodes.GS_REQ_USERIN);
        sentPackets[0].ResetOffset();
        sentPackets[0].ReadShort().Should().Be(1);
        sentPackets[0].ReadByte().Should().Be(0);
        sentPackets[0].ReadInt().Should().Be(nearby.CharacterId);
        sentPackets[0].ReadSByteString().Should().Be("Nearby");
        sentPackets[0].ReadShort().Should().Be((short)AccountNation.Karus);
        sentPackets[0].ReadShort().Should().Be(0);
    }

    [Fact]
    public async Task WorldPacketCoordinator_SendNearbyUsersToClientAsync_SendsNpcSnapshotInReqNpcInFormat()
    {
        using var provider = CreateProvider(_ => { });

        var requesterClient = Substitute.For<IClient>();
        requesterClient.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        requesterClient.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();

        var requester = sessionManager.CreateSession(requesterClient, characterId: 281, accountId: 291);
        requester.Name = "Requester";
        requester.ZoneId = 1;
        requester.X = 8;
        requester.Z = 8;
        sessionManager.Regions.AddToRegion(requester);

        sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 150,
            Name = "Kecoon",
            NpcType = 0,
            ZoneId = 1,
            X = 8.5f,
            Z = 8.5f,
            Size = 125,
            Bulk = 70,
            ModelId = 100,
            MaxHp = 100,
            Hp = 100
        });

        var coordinator = provider.GetRequiredService<IWorldPacketCoordinator>();
        await coordinator.SendNearbyUsersToClientAsync(requester);

        var npcPacket = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_REQ_NPCIN);
        npcPacket.ResetOffset();
        npcPacket.ReadShort().Should().Be(1);
        _ = npcPacket.ReadInt(); // unique id
        npcPacket.ReadShort().Should().Be(150);
        npcPacket.ReadByte().Should().Be(1);
        npcPacket.ReadShort().Should().Be(100);
        npcPacket.ReadInt().Should().Be(0);
        npcPacket.ReadByte().Should().Be(0);
        npcPacket.ReadInt().Should().Be(0);
        npcPacket.ReadShort().Should().Be(125);
    }

    [Fact]
    public async Task WorldPacketCoordinator_HandleReqNpcInAsync_UsesInt32NpcIds()
    {
        using var provider = CreateProvider(_ => { });

        var requesterClient = Substitute.For<IClient>();
        requesterClient.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        requesterClient.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();

        var requester = sessionManager.CreateSession(requesterClient, characterId: 284, accountId: 294);
        requester.Name = "Requester";
        requester.ZoneId = 1;
        requester.X = 8;
        requester.Z = 8;
        sessionManager.Regions.AddToRegion(requester);

        var npc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 150,
            Name = "Kecoon",
            NpcType = 0,
            ZoneId = 1,
            X = 8.5f,
            Z = 8.5f,
            Size = 125,
            ModelId = 100,
            MaxHp = 100,
            Hp = 100
        });

        var request = new Packet(GameOpcodes.GS_REQ_NPCIN);
        request.WriteShort(1);
        request.WriteInt(npc.UniqueId);

        var coordinator = provider.GetRequiredService<IWorldPacketCoordinator>();
        await coordinator.HandleReqNpcInAsync(requesterClient, request);

        sentPacket.Should().NotBeNull();
        sentPacket!.GetOpcode().Should().Be((byte)GameOpcodes.GS_REQ_NPCIN);
        sentPacket.ResetOffset();
        sentPacket.ReadShort().Should().Be(1);
        sentPacket.ReadInt().Should().Be(npc.UniqueId);
        sentPacket.ReadShort().Should().Be(150);
    }

    [Fact]
    public async Task SocialPacketCoordinator_HandleFriendProcessAsync_RequestRespondsWithFriendReportOpcode()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 600, accountId: 700);
        session.Name = "Zeus";

        var request = new Packet(GameOpcodes.GS_FRIEND_PROCESS);
        request.WriteByte(1);

        var coordinator = provider.GetRequiredService<ISocialPacketCoordinator>();
        await coordinator.HandleFriendProcessAsync(client, request);

        sentPackets.Should().ContainSingle();
        var sentPacket = sentPackets[0];
        sentPacket.GetOpcode().Should().Be((byte)GameOpcodes.GS_FRIEND_PROCESS);
        sentPacket.ResetOffset();
        sentPacket.ReadByte().Should().Be((byte)FriendSubOpcode.StatusList);
        sentPacket.ReadUShort().Should().Be(0);
        sentPacket.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task SocialPacketCoordinator_HandleFriendProcessAsync_RequestUsesInt32FriendIds()
    {
        using var provider = CreateProvider(db =>
        {
            db.Characters.AddRange(
                new Character
                {
                    Id = 601, AccountId = 701, Slot = 0, Name = "Zeus",
                    Race = 1, Class = 101, Face = 1, Hair = 1, Level = 10,
                    Hp = 100, Mp = 100, MapId = 1, X = 10, Z = 20,
                },
                new Character
                {
                    Id = 123456, AccountId = 702, Slot = 0, Name = "Rin",
                    Race = 1, Class = 101, Face = 1, Hair = 1, Level = 10,
                    Hp = 100, Mp = 100, MapId = 1, X = 10, Z = 20,
                });
            db.Friendships.Add(new Friendship
            {
                CharacterId = 601,
                FriendCharacterId = 123456,
                AddedAt = DateTime.UtcNow,
            });
        });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 601, accountId: 701);
        session.Name = "Zeus";

        var friendClient = Substitute.For<IClient>();
        friendClient.Id.Returns(Guid.NewGuid());
        friendClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var friend = sessionManager.CreateSession(friendClient, characterId: 123456, accountId: 702);
        friend.Name = "Rin";

        var request = new Packet(GameOpcodes.GS_FRIEND_PROCESS);
        request.WriteByte(1);

        var coordinator = provider.GetRequiredService<ISocialPacketCoordinator>();
        await coordinator.HandleFriendProcessAsync(client, request);

        sentPackets.Should().NotBeEmpty();
        var sentPacket = sentPackets[0];
        sentPacket.ResetOffset();
        sentPacket.ReadByte().Should().Be((byte)FriendSubOpcode.StatusList);
        sentPacket.ReadUShort().Should().Be(1);
        sentPacket.ReadString().Should().Be("Rin");
        sentPacket.ReadInt().Should().Be(friend.CharacterId);
        sentPacket.ReadByte().Should().Be(1);
    }

    [Fact]
    public async Task WorldPacketCoordinator_SendNearbyUsersToClientAsync_ShowsNpcStyleSpawnsButExcludesDeadNpcs()
    {
        using var provider = CreateProvider(_ => { });

        var requesterClient = Substitute.For<IClient>();
        requesterClient.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        requesterClient.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();

        var requester = sessionManager.CreateSession(requesterClient, characterId: 283, accountId: 293);
        requester.Name = "Requester";
        requester.ZoneId = 21;
        requester.X = 64.0f;
        requester.Z = 42.0f;
        sessionManager.Regions.AddToRegion(requester);

        // NPC-style spawn (ActType >= 100) should be visible (vendors, guards, quest NPCs)
        sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = 25000,
            Name = "Potrang",
            NpcType = 0,
            ZoneId = 21,
            SpawnActType = 100,
            X = 64.9f,
            Z = 42.1f,
            Size = 180,
            ModelId = 100,
            MaxHp = 100,
            Hp = 100
        });

        // Dead NPC should be excluded
        sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 750,
            Name = "Worm",
            NpcType = 0,
            ZoneId = 21,
            X = 64.9f,
            Z = 42.1f,
            MaxHp = 100,
            Hp = 0
        });

        var coordinator = provider.GetRequiredService<IWorldPacketCoordinator>();
        await coordinator.SendNearbyUsersToClientAsync(requester);

        var npcPacket = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_REQ_NPCIN);
        npcPacket.ResetOffset();
        npcPacket.ReadShort().Should().Be(1); // Only the alive NPC-style spawn is visible
    }

    [Fact]
    public async Task WorldPacketCoordinator_HandleMoveAsync_SendsNpcRegionListInsteadOfNpcRespawnsAfterRegionChange()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 282, accountId: 292);
        session.Name = "Walker";
        session.ZoneId = 1;
        session.X = 8;
        session.Z = 8;
        session.Y = 0;
        session.Hp = 100;
        sessionManager.Regions.AddToRegion(session);

        sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 150,
            Name = "Kecoon",
            NpcType = 0,
            ZoneId = 1,
            X = 100,
            Z = 8,
            Y = 0,
            Size = 100,
            ModelId = 100,
            MaxHp = 100,
            Hp = 100
        });

        var packet = new Packet(GameOpcodes.GS_MOVE);
        packet.WriteUShort(600); // x = 60 -> region 1, now adjacent to npc in region 2
        packet.WriteUShort(80);
        packet.WriteUShort(0);
        packet.WriteShort(45);
        packet.WriteByte(0);

        var coordinator = provider.GetRequiredService<IWorldPacketCoordinator>();
        await coordinator.HandleMoveAsync(client, packet);

        sentPackets.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_REGIONCHANGE);
        var npcRegionPacket = sentPackets.Should().ContainSingle(p => p.GetOpcode() == (byte)GameOpcodes.GS_NPC_REGION).Subject;
        npcRegionPacket.ResetOffset();
        npcRegionPacket.ReadShort().Should().Be(1);
        npcRegionPacket.ReadInt().Should().BeGreaterThan(0);
        sentPackets.Should().NotContain(p => p.GetOpcode() == (byte)GameOpcodes.GS_NPC_INOUT);
    }

    [Fact]
    public async Task WorldPacketCoordinator_HandleObjectEventAsync_BindSetsBindPointAndAcknowledges()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.Maps = CreateMapManagerWithObjectEvent(1, new ObjectEvent
        {
            Index = 55,
            Type = 0,
            Belong = 0,
            PosX = 10,
            PosZ = 20
        });

        var session = sessionManager.CreateSession(client, characterId: 173, accountId: 183);
        session.ZoneId = 1;
        session.Nation = AccountNation.Karus;
        session.Hp = 100;
        session.X = 10;
        session.Z = 20;
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_OBJECT_EVENT);
        packet.WriteShort(55);
        packet.WriteInt(0);

        var coordinator = provider.GetRequiredService<IWorldPacketCoordinator>();
        await coordinator.HandleObjectEventAsync(client, packet);

        session.Quest.BindPoint.Should().Be(55);
        sentPacket.Should().NotBeNull();
        sentPacket!.GetOpcode().Should().Be((byte)GameOpcodes.GS_OBJECT_EVENT);
        sentPacket.ResetOffset();
        sentPacket.ReadByte().Should().Be(0);
        sentPacket.ReadByte().Should().Be(1);
    }

    [Fact]
    public async Task WorldPacketCoordinator_HandleObjectEventAsync_WarpGateUsesMapWarpList()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.Maps = CreateMapManagerWithObjectEvent(
            21,
            new ObjectEvent
            {
                Index = 4013,
                Type = 5,
                ControlNpcId = 2,
                Belong = 0,
                PosX = 10,
                PosZ = 20
            },
            new WarpInfo
            {
                WarpId = 21,
                Name = "Town",
                Announce = "Moradon",
                Fee = 100,
                Zone = 21,
                X = 30,
                Z = 40
            },
            new WarpInfo
            {
                WarpId = 22,
                Name = "Delos",
                Announce = "Castle",
                Fee = 1000,
                Zone = 30,
                X = 50,
                Z = 60
            });

        var session = sessionManager.CreateSession(client, characterId: 174, accountId: 184);
        session.ZoneId = 21;
        session.Nation = AccountNation.Karus;
        session.Hp = 100;
        session.X = 10;
        session.Z = 20;
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_OBJECT_EVENT);
        packet.WriteShort(4013);
        packet.WriteInt(212);

        var coordinator = provider.GetRequiredService<IWorldPacketCoordinator>();
        await coordinator.HandleObjectEventAsync(client, packet);

        var warpListPacket = sentPackets.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_WARP_LIST);
        warpListPacket.ResetOffset();
        warpListPacket.ReadByte().Should().Be(1);
        warpListPacket.ReadShort().Should().Be(2);
        warpListPacket.ReadShort().Should().Be(21);
        warpListPacket.ReadString().Should().Be("Town");
        warpListPacket.ReadString().Should().Be("Moradon");
        warpListPacket.ReadShort().Should().Be(21);
        warpListPacket.ReadShort().Should().Be(0);
        warpListPacket.ReadUInt().Should().Be(100);
    }

    [Fact]
    public async Task WorldPacketCoordinator_HandleObjectEventAsync_WarpGateFallsBackToZoneWarpGroup()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.Maps = CreateMapManagerWithObjectEvent(
            73,
            new ObjectEvent
            {
                Index = 4020,
                Type = 5,
                ControlNpcId = 2011,
                Belong = 1,
                PosX = 502,
                PosZ = 104
            },
            new WarpInfo { WarpId = 7311, Name = "Luferson Castle", Fee = 5000, Zone = 1, Nation = 1, X = 437, Z = 1627 },
            new WarpInfo { WarpId = 7314, Name = "Moradon", Fee = 0, Zone = 21, Nation = 1, X = 261, Z = 299 },
            new WarpInfo { WarpId = 7321, Name = "El Morad Castle", Fee = 5000, Zone = 2, Nation = 2, X = 1598, Z = 407 },
            new WarpInfo { WarpId = 7324, Name = "Moradon", Fee = 0, Zone = 21, Nation = 2, X = 261, Z = 299 });

        var session = sessionManager.CreateSession(client, characterId: 175, accountId: 185);
        session.ZoneId = 73;
        session.Nation = AccountNation.Karus;
        session.Hp = 100;
        session.X = 502;
        session.Z = 104;
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_OBJECT_EVENT);
        packet.WriteShort(4020);
        packet.WriteInt(731);

        var coordinator = provider.GetRequiredService<IWorldPacketCoordinator>();
        await coordinator.HandleObjectEventAsync(client, packet);

        var warpListPacket = sentPackets.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_WARP_LIST);
        warpListPacket.ResetOffset();
        warpListPacket.ReadByte().Should().Be(1);
        warpListPacket.ReadShort().Should().Be(2);
        warpListPacket.ReadShort().Should().Be(7311);
        warpListPacket.ReadString().Should().Be("Luferson Castle");
        warpListPacket.ReadString().Should().BeEmpty();
        warpListPacket.ReadShort().Should().Be(1);
        warpListPacket.ReadShort().Should().Be(0);
        warpListPacket.ReadUInt().Should().Be(5000);
        warpListPacket.ReadShort().Should().Be(7314);
    }

    [Fact]
    public async Task WorldPacketCoordinator_HandleObjectEventAsync_WarpGateFallbackRejectsOtherNationGroup()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.Maps = CreateMapManagerWithObjectEvent(
            73,
            new ObjectEvent
            {
                Index = 4020,
                Type = 5,
                ControlNpcId = 2011,
                Belong = 1,
                PosX = 502,
                PosZ = 104
            },
            new WarpInfo { WarpId = 7311, Name = "El Morad Castle", Fee = 5000, Zone = 2, Nation = 2, X = 1598, Z = 407 });

        var session = sessionManager.CreateSession(client, characterId: 176, accountId: 186);
        session.ZoneId = 73;
        session.Nation = AccountNation.Karus;
        session.Hp = 100;
        session.X = 502;
        session.Z = 104;
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_OBJECT_EVENT);
        packet.WriteShort(4020);
        packet.WriteInt(731);

        var coordinator = provider.GetRequiredService<IWorldPacketCoordinator>();
        await coordinator.HandleObjectEventAsync(client, packet);

        sentPackets.Should().NotContain(p => p.GetOpcode() == (byte)GameOpcodes.GS_WARP_LIST);
        var result = sentPackets.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_OBJECT_EVENT);
        result.ResetOffset();
        result.ReadByte().Should().Be(5);
        result.ReadByte().Should().Be(0);
    }

    [Fact]
    public async Task WorldPacketCoordinator_HandleWarpListAsync_SameZoneWarpConsumesFeeAndWarps()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetNpc(900).Returns(new NpcData
                {
                    Id = 900,
                    NpcType = 1,
                    Group = 2
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        // WarpId 21 / 10 == group 2, matching the NPC Group
        sessionManager.Maps = CreateMapManagerWithObjectEvent(
            1,
            new ObjectEvent { Index = 1, Type = 5, ControlNpcId = 900, Belong = 0, PosX = 5, PosZ = 7 },
            new WarpInfo { WarpId = 21, Name = "Town", Fee = 100, Zone = 1, X = 30, Z = 40 });

        var session = sessionManager.CreateSession(client, characterId: 174, accountId: 184);
        session.ZoneId = 1;
        session.Nation = AccountNation.Karus;
        session.Hp = 100;
        session.Money = 500;
        session.X = 5;
        session.Z = 7;
        sessionManager.Regions.AddToRegion(session);

        var npc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = 900,
            ZoneId = 1,
            NpcType = 1,
            X = 5,
            Z = 7,
            MaxHp = 100,
            Hp = 100
        });
        session.Quest.EventNpcUniqueId = npc.UniqueId;

        var packet = new Packet(GameOpcodes.GS_WARP_LIST);
        packet.WriteShort(900);
        packet.WriteShort(21);

        var coordinator = provider.GetRequiredService<IWorldPacketCoordinator>();
        await coordinator.HandleWarpListAsync(client, packet);

        session.Money.Should().Be(400);
        session.X.Should().Be(30);
        session.Z.Should().Be(40);

        var warpPacket = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_WARP);
        warpPacket.ResetOffset();
        warpPacket.ReadUShort().Should().Be(300);
        warpPacket.ReadUShort().Should().Be(400);
    }

    [Fact]
    public async Task WorldPacketCoordinator_HandleWarpListAsync_MapWarpSameZoneConsumesFeeAndWarps()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.Maps = CreateMapManagerWithObjectEvent(
            21,
            new ObjectEvent
            {
                Index = 1,
                Type = 5,
                ControlNpcId = 2,
                Belong = 0,
                PosX = 10,
                PosZ = 20
            },
            new WarpInfo
            {
                WarpId = 21,
                Name = "Town",
                Announce = "Moradon",
                Fee = 100,
                Zone = 21,
                X = 30,
                Z = 40
            });

        var session = sessionManager.CreateSession(client, characterId: 175, accountId: 185);
        session.ZoneId = 21;
        session.Nation = AccountNation.Karus;
        session.Hp = 100;
        session.Money = 500;
        session.X = 10;
        session.Z = 20;
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_WARP_LIST);
        packet.WriteShort(2);
        packet.WriteShort(21);

        var coordinator = provider.GetRequiredService<IWorldPacketCoordinator>();
        await coordinator.HandleWarpListAsync(client, packet);

        session.Money.Should().Be(400);
        session.X.Should().Be(30);
        session.Z.Should().Be(40);

        var warpListAck = sentPackets.First(p => p.GetOpcode() == (byte)GameOpcodes.GS_WARP_LIST);
        warpListAck.ResetOffset();
        warpListAck.ReadByte().Should().Be(2);
        warpListAck.ReadByte().Should().Be(1);

        var warpPacket = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_WARP);
        warpPacket.ResetOffset();
        warpPacket.ReadUShort().Should().Be(300);
        warpPacket.ReadUShort().Should().Be(400);
    }

    [Fact]
    public async Task WorldPacketCoordinator_HandleWarpListAsync_SharedMapVariantDestinationStaysInCurrentZone()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.ZoneInfoTable.Returns(new Dictionary<short, ZoneInfoData>
                {
                    [21] = new() { ZoneNo = 21, MapName = "Moradon", SmdName = "moradon_0826.smd" },
                    [22] = new() { ZoneNo = 22, MapName = "Moradon II", SmdName = "moradon_0826.smd" }
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.Maps = CreateMapManagerWithObjectEvent(
            21,
            new ObjectEvent
            {
                Index = 4013,
                Type = 5,
                ControlNpcId = 4013,
                Belong = 0,
                PosX = 10,
                PosZ = 20
            },
            new WarpInfo
            {
                WarpId = 2121,
                Name = "Folk Village",
                Announce = "Moradon",
                Fee = 100,
                Zone = 22,
                X = 409,
                Z = 523
            });

        var session = sessionManager.CreateSession(client, characterId: 176, accountId: 186);
        session.ZoneId = 21;
        session.Nation = AccountNation.Karus;
        session.Hp = 100;
        session.Money = 500;
        session.X = 10;
        session.Z = 20;
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_WARP_LIST);
        packet.WriteShort(4013);
        packet.WriteShort(2121);

        var coordinator = provider.GetRequiredService<IWorldPacketCoordinator>();
        await coordinator.HandleWarpListAsync(client, packet);

        session.ZoneId.Should().Be(21);
        session.X.Should().Be(409);
        session.Z.Should().Be(523);

        sentPackets.Should().ContainSingle(p => p.GetOpcode() == (byte)GameOpcodes.GS_WARP);
        sentPackets.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_WARP_LIST);
        sentPackets.Should().NotContain(p => p.GetOpcode() == (byte)GameOpcodes.GS_ZONE_CHANGE);
    }

    [Fact]
    public async Task WorldPacketCoordinator_HandleWarpListAsync_CrossMapWarpArrivesAtDestinationStartPosition()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.ZoneInfoTable.Returns(new Dictionary<short, ZoneInfoData>
                {
                    [21] = new() { ZoneNo = 21, MapName = "Moradon", SmdName = "Moradon2.smd" },
                    [73] = new() { ZoneNo = 73, MapName = "Ronark Land Base", SmdName = "freezone_c.smd" }
                });
                gameData.GetStartPosition((short)21).Returns(new StartPositionData
                {
                    ZoneId = 21,
                    KarusX = 816,
                    KarusZ = 532,
                    ElmoradX = 816,
                    ElmoradZ = 532
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.Maps = CreateMapManagerWithObjectEvent(
            73,
            new ObjectEvent
            {
                Index = 4020,
                Type = 5,
                ControlNpcId = 2011,
                Belong = 1,
                PosX = 502,
                PosZ = 104
            },
            new WarpInfo
            {
                WarpId = 7314,
                Name = "Moradon",
                Zone = 21,
                Nation = 1,
                X = 261,
                Z = 299
            });

        var session = sessionManager.CreateSession(client, characterId: 177, accountId: 187);
        session.ZoneId = 73;
        session.Nation = AccountNation.Karus;
        session.Hp = 100;
        session.X = 502;
        session.Z = 104;
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_WARP_LIST);
        packet.WriteShort(4020);
        packet.WriteShort(7314);

        var coordinator = provider.GetRequiredService<IWorldPacketCoordinator>();
        await coordinator.HandleWarpListAsync(client, packet);

        session.ZoneId.Should().Be(21);
        session.X.Should().Be(816);
        session.Z.Should().Be(532);
        sentPackets.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_ZONE_CHANGE);
    }

    [Fact]
    public async Task WorldPacketCoordinator_HandleWarpListAsync_CrossMapWarpOutsideMoradonKeepsWarpCoordinates()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.ZoneInfoTable.Returns(new Dictionary<short, ZoneInfoData>
                {
                    [1] = new() { ZoneNo = 1, MapName = "Karus", SmdName = "karus2004.smd" },
                    [21] = new() { ZoneNo = 21, MapName = "Moradon", SmdName = "Moradon2.smd" }
                });
                gameData.GetStartPosition((short)1).Returns(new StartPositionData
                {
                    ZoneId = 1,
                    KarusX = 437,
                    KarusZ = 1627,
                    ElmoradX = 1869,
                    ElmoradZ = 172
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.Maps = CreateMapManagerWithObjectEvent(
            21,
            new ObjectEvent
            {
                Index = 4014,
                Type = 5,
                ControlNpcId = 211,
                Belong = 1,
                PosX = 797,
                PosZ = 526
            },
            new WarpInfo
            {
                WarpId = 2114,
                Name = "Lunar Valley",
                Zone = 1,
                Nation = 1,
                X = 1860,
                Z = 169
            });

        var session = sessionManager.CreateSession(client, characterId: 178, accountId: 188);
        session.ZoneId = 21;
        session.Nation = AccountNation.Karus;
        session.Hp = 100;
        session.X = 797;
        session.Z = 526;
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_WARP_LIST);
        packet.WriteShort(4014);
        packet.WriteShort(2114);

        var coordinator = provider.GetRequiredService<IWorldPacketCoordinator>();
        await coordinator.HandleWarpListAsync(client, packet);

        session.ZoneId.Should().Be(1);
        session.X.Should().Be(1860);
        session.Z.Should().Be(169);
    }

    [Fact]
    public async Task WorldPacketCoordinator_HandleZoneChangeAsync_SubOpcode1_SendsUserAndNpcSnapshotsBeforeAck()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 176, accountId: 186);
        session.ZoneId = 21;
        session.Hp = 100;
        session.X = 10;
        session.Z = 20;
        sessionManager.Regions.AddToRegion(session);

        sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 750,
            ZoneId = 21,
            NpcType = 0,
            X = 10,
            Z = 20,
            MaxHp = 100,
            Hp = 100
        });

        var packet = new Packet(GameOpcodes.GS_ZONE_CHANGE);
        packet.WriteByte(1);

        var coordinator = provider.GetRequiredService<IWorldPacketCoordinator>();
        await coordinator.HandleZoneChangeAsync(client, packet);

        sentPackets.Should().Contain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_REQ_USERIN);
        sentPackets.Should().Contain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_REQ_NPCIN);

        var ackPacket = sentPackets.Last(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ZONE_CHANGE);
        ackPacket.ResetOffset();
        ackPacket.ReadByte().Should().Be(2);
    }

    [Fact]
    public async Task QuestPacketCoordinator_CheckQuestKillAsync_IncrementsMatchingMonsterGroup()
    {
        using var scripts = ScriptedKillQuest(320, monsterId: 9001, count: 2);
        using var provider = CreateProvider(
            _ => { },
            _ => { },
            settings => settings.QuestsDirectory = scripts.Directory);

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 191, accountId: 201);
        session.Quest.QuestMap[320] = 1;

        var coordinator = provider.GetRequiredService<IQuestPacketCoordinator>();
        await coordinator.CheckQuestKillAsync(session, 9001);

        session.Quest.GetQuestKillCounts(320)[0].Should().Be(1);
        sentPacket.Should().NotBeNull();
        sentPacket!.GetOpcode().Should().Be((byte)GameOpcodes.GS_QUEST);
        sentPacket.ResetOffset();
        sentPacket.ReadByte().Should().Be(9);
        sentPacket.ReadByte().Should().Be(2);
        sentPacket.ReadShort().Should().Be(320);
        sentPacket.ReadByte().Should().Be(1);
        sentPacket.ReadShort().Should().Be(1);
    }

    [Fact]
    public async Task QuestPacketCoordinator_CheckQuestKillAsync_MarksQuestReadyToTurnInWhenFinalKillIsReached()
    {
        using var scripts = ScriptedKillQuest(320, monsterId: 9001, count: 1);
        using var provider = CreateProvider(
            _ => { },
            _ => { },
            settings => settings.QuestsDirectory = scripts.Directory);

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 196, accountId: 206);
        session.Quest.QuestMap[320] = 1;
        session.Quest.ActiveQuestId = 320;

        var coordinator = provider.GetRequiredService<IQuestPacketCoordinator>();
        await coordinator.CheckQuestKillAsync(session, 9001);

        session.Quest.QuestMap[320].Should().Be(3);
        session.Quest.KillCounts[0].Should().Be(1);

        sentPackets.Should().HaveCount(2);
        sentPackets[0].ResetOffset();
        sentPackets[0].ReadByte().Should().Be(9);
        sentPackets[0].ReadByte().Should().Be(2);
        sentPackets[0].ReadShort().Should().Be(320);
        sentPackets[0].ReadByte().Should().Be(1);
        sentPackets[0].ReadShort().Should().Be(1);

        sentPackets[1].ResetOffset();
        sentPackets[1].ReadByte().Should().Be(2);
        sentPackets[1].ReadShort().Should().Be(320);
        sentPackets[1].ReadByte().Should().Be(3);
    }

    [Fact]
    public async Task QuestPacketCoordinator_HandleQuestAsync_MonsterDataRequest_UsesUInt16Counts()
    {
        using var scripts = ScriptedKillQuest(320, monsterId: 9001, count: 2);
        using var provider = CreateProvider(
            _ => { },
            _ => { },
            settings => settings.QuestsDirectory = scripts.Directory);

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 291, accountId: 301);
        session.Quest.QuestMap[320] = 1;
        session.Quest.ActiveQuestId = 320;
        session.Quest.GetOrCreateQuestKillCounts(320)[0] = 1;
        session.Quest.SyncActiveQuestKillCounts();

        var packet = new Packet(GameOpcodes.GS_QUEST);
        packet.WriteByte(9);
        packet.WriteByte(1);
        packet.WriteShort(320);

        var coordinator = provider.GetRequiredService<IQuestPacketCoordinator>();
        await coordinator.HandleQuestAsync(client, packet);

        sentPacket.Should().NotBeNull();
        sentPacket!.GetOpcode().Should().Be((byte)GameOpcodes.GS_QUEST);
        sentPacket.ResetOffset();
        sentPacket.ReadByte().Should().Be(9);
        sentPacket.ReadByte().Should().Be(1);
        sentPacket.ReadShort().Should().Be(320);
        sentPacket.ReadShort().Should().Be(1);
        sentPacket.ReadShort().Should().Be(0);
        sentPacket.ReadShort().Should().Be(0);
        sentPacket.ReadShort().Should().Be(0);
    }

    [Theory]
    [InlineData(true, 3)]
    [InlineData(false, 2)]
    public async Task QuestPacketCoordinator_HandleQuestAsync_OnlyCompletesWhenTheRewardCanBePaid(
        bool inventoryFull, byte expectedState)
    {
        const int rewardItemId = 777;
        using var scripts = new TemporaryQuestScripts("quest51.quest", """
            Bind Npc 911

            Quest 51 "Paid on delivery"

            On fulfil for quest 51
                Transaction
                    Give 1 of 777
                    Complete 51

            On greeting
                Say "Bring me three."
                Topic "Right" goto close
            """);
        using var provider = CreateProvider(
            _ => { },
            gameData => gameData.GetItem(rewardItemId).Returns(new ItemData
            {
                Num = rewardItemId,
                Kind = 255,
                Countable = 0,
                Weight = 1,
                Duration = 0,
            }),
            settings => settings.QuestsDirectory = scripts.Directory);

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 193, accountId: 203);
        session.ZoneId = 1;
        session.X = 10;
        session.Z = 10;
        session.Name = "Quester";
        session.Hp = 100;
        session.Class = 101;
        session.Level = 10;
        var occupied = InventoryConstants.HaveMax - (inventoryFull ? 0 : 1);
        for (var slot = InventoryConstants.InventoryStart;
             slot < InventoryConstants.InventoryStart + occupied;
             slot++)
        {
            session.Inventory[slot].ItemId = 900000 + slot;
            session.Inventory[slot].Count = 1;
        }
        session.Quest.QuestMap[51] = 3;
        sessionManager.Regions.AddToRegion(session);

        var questNpc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 911, ZoneId = 1, Hp = 100, MaxHp = 100, X = 10, Z = 10,
        });
        session.Quest.EventNpcId = questNpc.NpcId;
        session.Quest.EventNpcUniqueId = questNpc.UniqueId;

        var packet = new Packet(GameOpcodes.GS_QUEST);
        packet.WriteByte(4);
        packet.WriteInt(51);
        await provider.GetRequiredService<IQuestPacketCoordinator>().HandleQuestAsync(client, packet);

        session.Quest.QuestMap[51].Should().Be(expectedState,
            "a reward that cannot be paid must not complete the quest");
        session.Inventory.Any(slot => slot.ItemId == rewardItemId).Should().Be(!inventoryFull);
    }

    [Fact]
    public async Task QuestPacketCoordinator_HandleQuestAsync_CompletesQuestFromTheScriptsFulfilEntry()
    {
        using var scripts = new TemporaryQuestScripts("quest50.quest", """
            Bind Npc 910

            Quest 50
                Kill 3 of 9001

            On fulfil for quest 50
                Exchange 4 for quest
                Complete 50

            On greeting
                Say "Bring me three."
                Topic "Right" goto close
            """);
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItemExchange(4).Returns(new ItemExchangeData
                {
                    Index = 4,
                    ExchangeItem1 = InventoryConstants.ItemExperience,
                    ExchangeCount1 = 20,
                });
                gameData.GetMaxExpForLevel(10).Returns(100);
                gameData.GetMaxExpForLevel(11).Returns(200);
                gameData.GetCoefficient((short)101).Returns(CreateBasicCoefficient(101));
            },
            settings => settings.QuestsDirectory = scripts.Directory);

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 192, accountId: 202);
        session.ZoneId = 1;
        session.X = 10;
        session.Z = 10;
        session.Name = "Quester";
        session.Hp = 100;
        session.Class = 101;
        session.Level = 10;
        session.Experience = 90;
        session.Quest.QuestMap[50] = 3;
        session.Quest.ActiveQuestId = 50;
        session.Quest.GetOrCreateQuestKillCounts(50)[0] = 3;
        session.Quest.SyncActiveQuestKillCounts();
        sessionManager.Regions.AddToRegion(session);

        var packet = new Packet(GameOpcodes.GS_QUEST);
        packet.WriteByte(4);
        packet.WriteInt(50);
        var questNpc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 910, ZoneId = 1, Hp = 100, MaxHp = 100, X = 10, Z = 10,
        });
        session.Quest.EventNpcId = questNpc.NpcId;
        session.Quest.EventNpcUniqueId = questNpc.UniqueId;

        var coordinator = provider.GetRequiredService<IQuestPacketCoordinator>();
        await coordinator.HandleQuestAsync(client, packet);

        session.Quest.QuestMap[50].Should().Be(2);
        session.Quest.KillCounts.Should().OnlyContain(count => count == 0);

        var completionPacket = sentPackets.Last(sent => sent.GetOpcode() == (byte)GameOpcodes.GS_QUEST);
        completionPacket.ResetOffset();
        completionPacket.ReadByte().Should().Be(2);
        completionPacket.ReadShort().Should().Be(50);
        completionPacket.ReadByte().Should().Be(2);

        session.Level.Should().Be(11, "90 + 20 crosses the level-10 maximum of 100");
        session.Experience.Should().Be(10, "the remainder carries into the new level");
        sentPackets.Should().Contain(sent => sent.GetOpcode() == (byte)GameOpcodes.GS_LEVEL_CHANGE);
    }

    [Fact]
    public async Task QuestPacketCoordinator_HandleNpcEventAsync_WarehouseNpcReturnsWarehouseUi()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetNpc(910, Arg.Any<bool>()).Returns(new NpcData
                {
                    Id = 910,
                    NpcType = 31
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 193, accountId: 203);
        session.ZoneId = 1;
        session.Hp = 100;
        session.X = 10;
        session.Z = 10;
        sessionManager.Regions.AddToRegion(session);

        var npc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = 910,
            ZoneId = 1,
            NpcType = 1,
            X = 10,
            Z = 10,
            MaxHp = 100,
            Hp = 100
        });

        var packet = new Packet(GameOpcodes.GS_NPC_EVENT);
        packet.WriteByte(0);
        packet.WriteInt(npc.UniqueId);
        packet.WriteInt(0);

        var coordinator = provider.GetRequiredService<IQuestPacketCoordinator>();
        await coordinator.HandleNpcEventAsync(client, packet);

        session.Quest.EventNpcId.Should().Be(910);
        session.Quest.EventNpcUniqueId.Should().Be(npc.UniqueId);
        sentPacket.Should().NotBeNull();
        sentPacket!.GetOpcode().Should().Be((byte)GameOpcodes.GS_WAREHOUSE);
        sentPacket.ResetOffset();
        sentPacket.ReadByte().Should().Be(0x10);
    }

    [Fact]
    public async Task QuestPacketCoordinator_HandleNpcEventAsync_AnvilNpcReturnsUpgradeUi()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetNpc(5001, Arg.Any<bool>()).Returns(new NpcData
                {
                    Id = 5001,
                    NpcType = 24
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 194, accountId: 204);
        session.ZoneId = 21;
        session.Hp = 100;
        session.X = 10;
        session.Z = 10;
        sessionManager.Regions.AddToRegion(session);

        var npc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = 5001,
            ZoneId = 21,
            NpcType = 24,
            X = 10,
            Z = 10,
            MaxHp = 100,
            Hp = 100
        });

        var packet = new Packet(GameOpcodes.GS_NPC_EVENT);
        packet.WriteByte(0);
        packet.WriteInt(npc.UniqueId);
        packet.WriteInt(0);

        var coordinator = provider.GetRequiredService<IQuestPacketCoordinator>();
        await coordinator.HandleNpcEventAsync(client, packet);

        sentPacket.Should().NotBeNull();
        sentPacket!.GetOpcode().Should().Be((byte)GameOpcodes.GS_ITEM_UPGRADE);
        sentPacket.ResetOffset();
        sentPacket.ReadByte().Should().Be(1);
        sentPacket.ReadInt().Should().Be(npc.UniqueId);
    }

    [Fact]
    public async Task CharacterDevelopmentPacketCoordinator_HandlePointChangeAsync_UpdatesDerivedStats()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData => gameData.GetCoefficient((short)101).Returns(CreateBasicCoefficient(101)));

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 193, accountId: 203);
        session.Class = 101;
        session.Level = 10;
        session.StatPoints = 1;
        session.Strength = 60;
        session.Stamina = 60;
        session.Dexterity = 60;
        session.Intelligence = 60;
        session.Magic = 50;

        var packet = new Packet(GameOpcodes.GS_POINT_CHANGE);
        packet.WriteByte(1);

        var coordinator = provider.GetRequiredService<ICharacterDevelopmentPacketCoordinator>();
        await coordinator.HandlePointChangeAsync(client, packet);

        session.Strength.Should().Be(61);
        session.StatPoints.Should().Be(0);
        sentPacket.Should().NotBeNull();
        sentPacket!.GetOpcode().Should().Be((byte)GameOpcodes.GS_POINT_CHANGE);
        sentPacket.ResetOffset();
        sentPacket.ReadByte().Should().Be(1);
        sentPacket.ReadShort().Should().Be(61);
    }

    [Fact]
    public async Task CharacterDevelopmentPacketCoordinator_HandleClassChangeAsync_ResetsStatsToRaceDefaults()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData => gameData.GetCoefficient((short)101).Returns(CreateBasicCoefficient(101)));

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 194, accountId: 204);
        session.Class = 101;
        session.Level = 30;
        session.Race = 1;
        session.Money = 2_000_000;
        session.Strength = 80;
        session.Stamina = 70;
        session.Dexterity = 65;
        session.Intelligence = 60;
        session.Magic = 55;

        var packet = new Packet(GameOpcodes.GS_CLASS_CHANGE);
        packet.WriteByte(2);

        var coordinator = provider.GetRequiredService<ICharacterDevelopmentPacketCoordinator>();
        await coordinator.HandleClassChangeAsync(client, packet);

        session.Strength.Should().Be(65);
        session.Stamina.Should().Be(65);
        session.Dexterity.Should().Be(60);
        session.Intelligence.Should().Be(50);
        session.Magic.Should().Be(50);
        session.StatPoints.Should().Be(97);
        sentPacket.Should().NotBeNull();
        sentPacket!.GetOpcode().Should().Be((byte)GameOpcodes.GS_CLASS_CHANGE);
        sentPacket.ResetOffset();
        sentPacket.ReadByte().Should().Be(2);
        sentPacket.ReadByte().Should().Be(1);
        sentPacket.ReadInt().Should().Be(session.Money);
    }

    private static (IClient Client, UserSession Session, Func<Packet?> Sent) CreateMasterySession(
        ServiceProvider provider, short classId, byte level, int characterId)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var session = provider.GetRequiredService<SessionManager>()
            .CreateSession(client, characterId, accountId: characterId);
        session.Class = classId;
        session.Level = level;
        session.SkillPoints[ProgressionTable.MasteryPoolSlot] = 5;

        return (client, session, () => sentPacket);
    }

    private static Packet MasteryRequest(int slot)
    {
        var packet = new Packet(GameOpcodes.GS_SKILLPT_CHANGE);
        packet.WriteByte((byte)slot);
        return packet;
    }

    [Theory]
    [InlineData(115)]
    [InlineData(215)]
    public async Task MasteredKuriansMaySpendOnTheMasterTree(short classId)
    {
        using var provider = CreateProvider(_ => { });
        var (client, session, sent) = CreateMasterySession(provider, classId, level: 70, characterId: 401);

        await provider.GetRequiredService<ICharacterDevelopmentPacketCoordinator>()
            .HandleSkillPointChangeAsync(client, MasteryRequest(ProgressionTable.MasteryMasterSlot));

        session.SkillPoints[ProgressionTable.MasteryMasterSlot].Should().Be(1,
            "the mastered tier of this job family carries an odd subtype, so any eligibility test "
            + "written as a parity check rejects exactly the characters it should accept");
        session.SkillPoints[ProgressionTable.MasteryPoolSlot].Should().Be(4);
        sent().Should().BeNull("nothing is sent when the point is accepted");
    }

    [Theory]
    [InlineData(114)]
    [InlineData(214)]
    public async Task UnmasteredKuriansMayNotSpendOnTheMasterTree(short classId)
    {
        using var provider = CreateProvider(_ => { });
        var (client, session, sent) = CreateMasterySession(provider, classId, level: 70, characterId: 402);

        await provider.GetRequiredService<ICharacterDevelopmentPacketCoordinator>()
            .HandleSkillPointChangeAsync(client, MasteryRequest(ProgressionTable.MasteryMasterSlot));

        session.SkillPoints[ProgressionTable.MasteryMasterSlot].Should().Be(0);
        session.SkillPoints[ProgressionTable.MasteryPoolSlot].Should().Be(5);
        sent().Should().NotBeNull("a refusal echoes the unchanged value back");
    }

    [Theory]
    [InlineData(113)]
    [InlineData(213)]
    public async Task BeginnersOfEveryFamilyMayNotSpendMasteryPoints(short classId)
    {
        using var provider = CreateProvider(_ => { });
        var (client, session, sent) = CreateMasterySession(provider, classId, level: 70, characterId: 403);

        await provider.GetRequiredService<ICharacterDevelopmentPacketCoordinator>()
            .HandleSkillPointChangeAsync(client, MasteryRequest(ProgressionTable.MasteryClassFirstSlot));

        session.SkillPoints[ProgressionTable.MasteryClassFirstSlot].Should().Be(0,
            "this family's beginner tier sits above the numeric range the other families use, so a "
            + "'greater than four means job-changed' test lets it through");
        session.SkillPoints[ProgressionTable.MasteryPoolSlot].Should().Be(5);
        sent().Should().NotBeNull();
    }

    [Theory]
    [InlineData(106)]
    [InlineData(108)]
    [InlineData(110)]
    [InlineData(112)]
    public async Task MasteredCharactersOfTheOtherFamiliesStillQualify(short classId)
    {
        using var provider = CreateProvider(_ => { });
        var (client, session, sent) = CreateMasterySession(provider, classId, level: 70, characterId: 404);

        await provider.GetRequiredService<ICharacterDevelopmentPacketCoordinator>()
            .HandleSkillPointChangeAsync(client, MasteryRequest(ProgressionTable.MasteryMasterSlot));

        session.SkillPoints[ProgressionTable.MasteryMasterSlot].Should().Be(1);
        sent().Should().BeNull();
    }

}
