using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class NpcAiTests : GameTestBase
{
    private const int AlwaysHits = 101;

    [Fact]
    public void NpcAiTargetingService_FindAggressiveTarget_GuardIgnoresSameNation()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var targetingService = provider.GetRequiredService<INpcAiTargetingService>();

        var allyClient = Substitute.For<IClient>();
        allyClient.Id.Returns(Guid.NewGuid());
        var ally = sessionManager.CreateSession(allyClient, characterId: 601, accountId: 701);
        ally.ZoneId = 1;
        ally.X = 50;
        ally.Z = 50;
        ally.Nation = AccountNation.ElMorad;
        ally.Hp = 100;
        sessionManager.Regions.AddToRegion(ally);

        var enemyClient = Substitute.For<IClient>();
        enemyClient.Id.Returns(Guid.NewGuid());
        var enemy = sessionManager.CreateSession(enemyClient, characterId: 602, accountId: 702);
        enemy.ZoneId = 1;
        enemy.X = 52;
        enemy.Z = 50;
        enemy.Nation = AccountNation.Karus;
        enemy.Hp = 100;
        sessionManager.Regions.AddToRegion(enemy);

        var guard = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            ZoneId = 1,
            X = 50,
            Z = 50,
            SpawnX = 50,
            SpawnZ = 50,
            SearchRange = 10,
            NpcType = 11,
            Nation = EntityNation.ElMorad,
            Hp = 100,
            MaxHp = 100
        });

        var target = targetingService.FindAggressiveTarget(guard);

        target.Should().NotBeNull();
        target!.CharacterId.Should().Be(enemy.CharacterId);
    }

    [Fact]
    public void NpcAiTargetingService_FindAggressiveTarget_GuardRespectsNonHostileZoneRule()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var targetingService = provider.GetRequiredService<INpcAiTargetingService>();

        var playerClient = Substitute.For<IClient>();
        playerClient.Id.Returns(Guid.NewGuid());
        var player = sessionManager.CreateSession(playerClient, characterId: 610, accountId: 710);
        player.ZoneId = 21;
        player.X = 818;
        player.Z = 534;
        player.Nation = AccountNation.ElMorad;
        player.Hp = 100;
        sessionManager.Regions.AddToRegion(player);

        var guard = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            ZoneId = 21,
            X = 813,
            Z = 505,
            SpawnX = 813,
            SpawnZ = 505,
            SearchRange = 25,
            AttackRange = 7,
            NpcType = 11,
            Nation = EntityNation.Karus,
            Hp = 100,
            MaxHp = 100
        });

        var target = targetingService.FindAggressiveTarget(guard);

        target.Should().BeNull();
    }

    [Fact]
    public void NpcAiTargetingService_FindTarget_MonsterAttacksFirstWhenActTypeIsFive()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var targetingService = provider.GetRequiredService<INpcAiTargetingService>();

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = sessionManager.CreateSession(client, characterId: 606, accountId: 706);
        session.ZoneId = 21;
        session.X = 63.8f;
        session.Z = 41.6f;
        session.Hp = 100;
        sessionManager.Regions.AddToRegion(session);

        var worm = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = 21,
            X = 63.9f,
            Z = 41.9f,
            SpawnX = 63.9f,
            SpawnZ = 41.9f,
            SearchRange = 5,
            AttackRange = 5,
            NpcType = 0,
            ActType = 5,
            IsAggressive = NpcInstance.AttacksFirst(5),
            Hp = 100,
            MaxHp = 100
        });

        var target = targetingService.FindTarget(worm);

        target.Should().NotBeNull();
        target!.CharacterId.Should().Be(session.CharacterId);
    }

    [Fact]
    public void NpcAiTargetingService_FindTarget_RetaliateMonsterOnlyNoticesUserFromAcrossTheField()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var targetingService = provider.GetRequiredService<INpcAiTargetingService>();

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = sessionManager.CreateSession(client, characterId: 640, accountId: 740);
        session.ZoneId = 21;
        session.X = 51f;
        session.Z = 41.6f;
        session.Hp = 100;
        sessionManager.Regions.AddToRegion(session);

        var kecoon = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = 21,
            X = 63.9f,
            Z = 41.6f,
            SpawnX = 63.9f,
            SpawnZ = 41.6f,
            SearchRange = 15,
            AttackRange = 7,
            NpcType = 0,
            ActType = 1,
            Hp = 100,
            MaxHp = 100
        });

        targetingService.FindTarget(kecoon).Should().BeNull();

        kecoon.RecordDamage(session.CharacterId, 10);

        targetingService.FindTarget(kecoon)!.CharacterId.Should().Be(session.CharacterId);
    }

    [Fact]
    public void NpcAiTargetingService_FindTarget_AggressiveFlagMakesMonsterAttackOnSight()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var targetingService = provider.GetRequiredService<INpcAiTargetingService>();

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = sessionManager.CreateSession(client, characterId: 645, accountId: 745);
        session.ZoneId = 31;
        session.X = 51f;
        session.Z = 41.6f;
        session.Hp = 100;
        sessionManager.Regions.AddToRegion(session);

        var mob = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = 31,
            X = 63.9f,
            Z = 41.6f,
            SpawnX = 63.9f,
            SpawnZ = 41.6f,
            SearchRange = 15,
            AttackRange = 7,
            NpcType = 0,
            ActType = 1,
            IsAggressive = true,
            Hp = 100,
            MaxHp = 100
        });

        targetingService.FindTarget(mob)!.CharacterId.Should().Be(session.CharacterId);
    }

    [Fact]
    public void MonsterAggressionPolicy_Apply_FlagsBossesAndConfiguredZones()
    {
        using var provider = CreateProvider(
            _ => { },
            configureSettings: s => s.Monsters.AggressiveZones = [31]);
        var policy = provider.GetRequiredService<IMonsterAggressionPolicy>();

        var fieldMob = new NpcInstance { IsMonster = true, NpcType = 0, ZoneId = 1, ActType = 1 };
        var boss = new NpcInstance { IsMonster = true, NpcType = 3, ZoneId = 1, ActType = 1 };
        var dungeonMob = new NpcInstance { IsMonster = true, NpcType = 0, ZoneId = 31, ActType = 1 };

        policy.Apply(fieldMob);
        policy.Apply(boss);
        policy.Apply(dungeonMob);

        fieldMob.IsAggressive.Should().BeFalse();
        boss.IsAggressive.Should().BeTrue();
        dungeonMob.IsAggressive.Should().BeTrue();
    }

    [Fact]
    public void NpcAiTargetingService_FindTarget_RetaliateMonsterIgnoresUserStandingOnTopOfIt()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var targetingService = provider.GetRequiredService<INpcAiTargetingService>();

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = sessionManager.CreateSession(client, characterId: 644, accountId: 744);
        session.ZoneId = 21;
        session.X = 63.8f;
        session.Z = 41.6f;
        session.Hp = 100;
        sessionManager.Regions.AddToRegion(session);

        var kecoon = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = 21,
            X = 63.9f,
            Z = 41.9f,
            SpawnX = 63.9f,
            SpawnZ = 41.9f,
            SearchRange = 15,
            AttackRange = 7,
            NpcType = 0,
            ActType = 1,
            Hp = 100,
            MaxHp = 100
        });

        targetingService.FindTarget(kecoon).Should().BeNull();
    }

    [Fact]
    public void NpcAiTargetingService_CallFamilyAllies_RetaliateMonsterDoesNotPullNeighbours()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var targetingService = provider.GetRequiredService<INpcAiTargetingService>();

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = sessionManager.CreateSession(client, characterId: 641, accountId: 741);
        session.ZoneId = 21;
        session.X = 100f;
        session.Z = 100f;
        session.Hp = 100;
        sessionManager.Regions.AddToRegion(session);

        var caller = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = 21,
            X = 102f,
            Z = 100f,
            SpawnX = 102f,
            SpawnZ = 100f,
            NpcType = 0,
            ActType = 1,
            Family = 1,
            SearchRange = 15,
            TracingRange = 30,
            Hp = 100,
            MaxHp = 100
        });

        var neighbour = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = 21,
            X = 106f,
            Z = 100f,
            SpawnX = 106f,
            SpawnZ = 100f,
            NpcType = 0,
            ActType = 1,
            Family = 1,
            SearchRange = 15,
            TracingRange = 30,
            Hp = 100,
            MaxHp = 100
        });

        targetingService.CallFamilyAllies(caller, session.CharacterId);

        neighbour.TargetUserId.Should().Be(0);
    }

    [Fact]
    public async Task NpcAiBehaviorService_ProcessNpcAsync_DropsTargetOnceChasePassesTracingRange()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var behaviorService = provider.GetRequiredService<INpcAiBehaviorService>();

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = sessionManager.CreateSession(client, characterId: 642, accountId: 742);
        session.ZoneId = 21;
        session.X = 200f;
        session.Z = 200f;
        session.Hp = 100;
        sessionManager.Regions.AddToRegion(session);

        var monster = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = 21,
            X = 205f,
            Z = 200f,
            SpawnX = 205f,
            SpawnZ = 200f,
            NpcType = 0,
            ActType = 1,
            SearchRange = 15,
            TracingRange = 30,
            AttackRange = 7,
            Speed1 = 2,
            Speed2 = 6,
            Hp = 100,
            MaxHp = 100,
            State = NpcState.Attacking,
            TargetUserId = session.CharacterId
        });
        monster.RecordDamage(session.CharacterId, 10);

        session.X = 400f;
        await behaviorService.ProcessNpcAsync(monster, DateTime.UtcNow.Ticks);

        monster.TargetUserId.Should().Be(0);
        monster.IsTracing.Should().BeFalse();
    }

    [Fact]
    public void RegionManager_DropAggroOn_ReleasesChasersAndSendsThemHome()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var monster = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = 21,
            X = 300f,
            Z = 300f,
            SpawnX = 290f,
            SpawnZ = 300f,
            NpcType = 0,
            ActType = 1,
            Hp = 100,
            MaxHp = 100,
            State = NpcState.Attacking,
            TargetUserId = 643,
            IsTracing = true
        });
        sessionManager.Regions.MarkNpcEngaged(monster);

        sessionManager.Regions.DropAggroOn(643);

        monster.TargetUserId.Should().Be(0);
        monster.IsTracing.Should().BeFalse();
        monster.State.Should().Be(NpcState.Returning);
    }

    [Fact]
    public void NpcInstance_AttackDistance_UsesRetailMeleeRangeForDirectAttackers()
    {
        var melee = new NpcInstance { IsMonster = true, AttackRange = 25, DirectAttack = 0, Bulk = 70, Size = 100 };
        var ranged = new NpcInstance { IsMonster = true, AttackRange = 25, DirectAttack = 1, Bulk = 70, Size = 100 };

        melee.AttackDistance.Should().BeApproximately(NpcInstance.MeleeAttackRange + 0.7f, 0.001f);
        ranged.AttackDistance.Should().Be(25f);
    }

    [Fact]
    public void NpcAiTargetingService_FindTarget_ScarecrowNeverAggrosNearbyUser()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var targetingService = provider.GetRequiredService<INpcAiTargetingService>();

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = sessionManager.CreateSession(client, characterId: 611, accountId: 711);
        session.ZoneId = 21;
        session.X = 758;
        session.Z = 406;
        session.Hp = 100;
        sessionManager.Regions.AddToRegion(session);

        var scarecrow = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            ZoneId = 21,
            X = 758,
            Z = 406,
            SpawnX = 758,
            SpawnZ = 406,
            NpcId = 19070,
            NpcType = 171,
            SpawnActType = 102,
            SearchRange = 10,
            AttackRange = 5,
            Hp = 100,
            MaxHp = 100,
            TargetUserId = session.CharacterId
        });

        var target = targetingService.FindTarget(scarecrow);

        target.Should().BeNull();
        scarecrow.TargetUserId.Should().Be(0);
    }

    [Fact]
    public void SessionManager_GetActiveAiNpcsSnapshot_FiltersToNearbyOrAlreadyActiveNpcs()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = sessionManager.CreateSession(client, characterId: 607, accountId: 707);
        session.ZoneId = 1;
        session.X = 50;
        session.Z = 50;
        session.Hp = 100;
        sessionManager.Regions.AddToRegion(session);

        var nearbyNpc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = 1,
            X = 52,
            Z = 50,
            SpawnX = 52,
            SpawnZ = 50,
            NpcType = 0,
            Hp = 100,
            MaxHp = 100
        });

        var distantStandingNpc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = 1,
            X = 500,
            Z = 500,
            SpawnX = 500,
            SpawnZ = 500,
            NpcType = 0,
            Hp = 100,
            MaxHp = 100
        });

        var distantReturningNpc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = 1,
            X = 650,
            Z = 650,
            SpawnX = 600,
            SpawnZ = 600,
            NpcType = 0,
            Hp = 80,
            MaxHp = 100,
            State = NpcState.Returning,
            IsMoving = true
        });
        sessionManager.Regions.MarkNpcEngaged(distantReturningNpc);

        var activeNpcIds = sessionManager.GetActiveAiNpcsSnapshot().Select(npc => npc.UniqueId).ToHashSet();

        activeNpcIds.Should().Contain(nearbyNpc.UniqueId);
        activeNpcIds.Should().Contain(distantReturningNpc.UniqueId);
        activeNpcIds.Should().NotContain(distantStandingNpc.UniqueId);
    }

    [Fact]
    public void SessionManager_GetActiveAiNpcsSnapshot_IncludesPatrolScarecrowsWithNpcStyleActType()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = sessionManager.CreateSession(client, characterId: 612, accountId: 712);
        session.ZoneId = 21;
        session.X = 758;
        session.Z = 406;
        session.Hp = 100;
        sessionManager.Regions.AddToRegion(session);

        var patrolScarecrow = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            ZoneId = 21,
            X = 758,
            Z = 406,
            SpawnX = 758,
            SpawnZ = 406,
            NpcId = 19070,
            NpcType = 171,
            SpawnActType = 102,
            MoveType = NpcMoveType.PatrolLoop,
            InitMoveType = NpcMoveType.PatrolLoop,
            Waypoints = [(776, 406), (758, 406)],
            Speed1 = 6,
            Speed2 = 6,
            Hp = 100,
            MaxHp = 100
        });

        var activeNpcIds = sessionManager.GetActiveAiNpcsSnapshot().Select(npc => npc.UniqueId).ToHashSet();

        patrolScarecrow.UsesNpcSpawnStyle.Should().BeTrue();
        patrolScarecrow.HasAi.Should().BeTrue();
        activeNpcIds.Should().Contain(patrolScarecrow.UniqueId);
    }

    [Fact]
    public async Task NpcAiBehaviorService_ProcessNpcAsync_MovesPatrolScarecrowWithoutAggro()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var behaviorService = provider.GetRequiredService<INpcAiBehaviorService>();

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var session = sessionManager.CreateSession(client, characterId: 613, accountId: 713);
        session.ZoneId = 21;
        session.X = 758;
        session.Z = 406;
        session.Hp = 100;
        sessionManager.Regions.AddToRegion(session);

        var nowTicks = DateTime.UtcNow.Ticks;
        var scarecrow = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            ZoneId = 21,
            X = 758,
            Z = 406,
            SpawnX = 758,
            SpawnZ = 406,
            NpcId = 19070,
            NpcType = 171,
            SpawnActType = 102,
            MoveType = NpcMoveType.PatrolLoop,
            InitMoveType = NpcMoveType.PatrolLoop,
            Waypoints = [(776, 406), (758, 406)],
            Speed1 = 6,
            Speed2 = 6,
            SearchRange = 10,
            AttackRange = 5,
            Hp = 100,
            MaxHp = 100,
            State = NpcState.Standing,
            TargetUserId = session.CharacterId,
            StateChangeTicks = nowTicks - TimeSpan.TicksPerSecond * 10
        });

        await behaviorService.ProcessNpcAsync(scarecrow, nowTicks);

        scarecrow.State.Should().Be(NpcState.Moving);
        scarecrow.TargetUserId.Should().Be(0);
        scarecrow.TargetX.Should().Be(776);
        scarecrow.TargetZ.Should().Be(406);
        scarecrow.X.Should().BeApproximately(759.5f, 0.01f);
        scarecrow.Z.Should().BeApproximately(406f, 0.01f);
        sentPackets.Should().ContainSingle(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_NPC_MOVE);
    }

    [Fact]
    public async Task NpcAiBehaviorService_ProcessNpcAsync_MovesAScriptedPathObjectAlongItsWaypoints()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var behaviorService = provider.GetRequiredService<INpcAiBehaviorService>();

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = sessionManager.CreateSession(client, characterId: 921, accountId: 922);
        session.ZoneId = 66;
        session.X = 195;
        session.Z = 614;
        session.Hp = 100;
        sessionManager.Regions.AddToRegion(session);

        var nowTicks = DateTime.UtcNow.Ticks;
        var log = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            ZoneId = 66,
            X = 195,
            Z = 614,
            SpawnX = 195,
            SpawnZ = 614,
            NpcId = 30200,
            Name = "Burns Log",
            NpcType = NpcData.TypeObjectWood,
            SpawnActType = 105,
            MoveType = NpcMoveType.ScriptedPath,
            InitMoveType = NpcMoveType.ScriptedPath,
            Waypoints = [(225, 614), (190, 578)],
            Speed1 = 6,
            Speed2 = 6,
            Hp = 100,
            MaxHp = 100,
            State = NpcState.Standing,
            StateChangeTicks = nowTicks - TimeSpan.TicksPerSecond * 10
        });

        log.HasAi.Should().BeTrue("an object that follows an authored path needs AI ticks to walk it");

        await behaviorService.ProcessNpcAsync(log, nowTicks);

        log.State.Should().Be(NpcState.Moving);
        log.TargetX.Should().Be(225);
        log.TargetZ.Should().Be(614);
        log.TargetUserId.Should().Be(0, "a drifting object must not aggro the player standing next to it");
    }

    [Fact]
    public void SessionManager_GetActiveAiNpcsSnapshot_ExcludesEngagedNpcWithoutAi()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = sessionManager.CreateSession(client, characterId: 608, accountId: 708);
        session.ZoneId = 21;
        session.X = 815;
        session.Z = 530;
        session.Hp = 100;
        sessionManager.Regions.AddToRegion(session);

        var merchant = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            ZoneId = 21,
            X = 816,
            Z = 530,
            SpawnX = 816,
            SpawnZ = 530,
            NpcId = 32756,
            Name = "[PUS] SRGame",
            NpcType = 21,
            Group = 3,
            Family = 1,
            Hp = 100,
            MaxHp = 100,
            State = NpcState.Attacking,
            TargetUserId = session.CharacterId
        });
        sessionManager.Regions.MarkNpcEngaged(merchant);

        var guard = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            ZoneId = 21,
            X = 817,
            Z = 530,
            SpawnX = 817,
            SpawnZ = 530,
            NpcId = 11021,
            Name = "Guard",
            NpcType = 11,
            Group = 2,
            Nation = EntityNation.ElMorad,
            Family = 1,
            Hp = 100,
            MaxHp = 100,
            State = NpcState.Attacking,
            TargetUserId = session.CharacterId
        });
        sessionManager.Regions.MarkNpcEngaged(guard);

        var activeNpcIds = sessionManager.GetActiveAiNpcsSnapshot().Select(npc => npc.UniqueId).ToHashSet();

        activeNpcIds.Should().Contain(guard.UniqueId);
        activeNpcIds.Should().NotContain(merchant.UniqueId);
    }

    [Fact]
    public void NpcAiTargetingService_CallFamilyAllies_RecruitsOnlyCompatibleGuardAllies()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var targetingService = provider.GetRequiredService<INpcAiTargetingService>();

        var playerClient = Substitute.For<IClient>();
        playerClient.Id.Returns(Guid.NewGuid());
        var player = sessionManager.CreateSession(playerClient, characterId: 609, accountId: 709);
        player.ZoneId = 21;
        player.X = 818;
        player.Z = 534;
        player.Nation = AccountNation.Karus;
        player.Hp = 100;
        sessionManager.Regions.AddToRegion(player);

        var caller = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            ZoneId = 21,
            X = 834,
            Z = 542,
            SpawnX = 834,
            SpawnZ = 542,
            NpcId = 11021,
            Name = "Guard",
            NpcType = 11,
            Group = 2,
            Nation = EntityNation.ElMorad,
            Family = 1,
            SearchRange = 25,
            TracingRange = 25,
            Hp = 100,
            MaxHp = 100
        });

        var alliedGuard = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            ZoneId = 21,
            X = 832,
            Z = 525,
            SpawnX = 832,
            SpawnZ = 525,
            NpcId = 11021,
            Name = "Guard",
            NpcType = 11,
            Group = 2,
            Nation = EntityNation.ElMorad,
            Family = 1,
            Hp = 100,
            MaxHp = 100
        });

        var enemyNationGuard = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            ZoneId = 21,
            X = 820,
            Z = 535,
            SpawnX = 820,
            SpawnZ = 535,
            NpcId = 21021,
            Name = "Guard",
            NpcType = 11,
            Group = 1,
            Nation = EntityNation.Karus,
            Family = 1,
            Hp = 100,
            MaxHp = 100
        });

        var neutralVendor = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            ZoneId = 21,
            X = 816,
            Z = 550,
            SpawnX = 816,
            SpawnZ = 550,
            NpcId = 32756,
            Name = "[PUS] SRGame",
            NpcType = 21,
            Group = 3,
            Nation = EntityNation.None,
            Family = 1,
            Hp = 100,
            MaxHp = 100
        });

        targetingService.CallFamilyAllies(caller, player.CharacterId);

        alliedGuard.TargetUserId.Should().Be(player.CharacterId);
        alliedGuard.State.Should().Be(NpcState.Attacking);
        enemyNationGuard.TargetUserId.Should().Be(0);
        neutralVendor.TargetUserId.Should().Be(0);
    }

    [Fact]
    public async Task NpcAiMovementService_HandleReturningAsync_ResetsNpcAtSpawnAndHeals()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var movementService = provider.GetRequiredService<INpcAiMovementService>();

        var npc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = 1,
            X = 55,
            Y = 3,
            Z = 60,
            SpawnX = 40,
            SpawnY = 1,
            SpawnZ = 45,
            State = NpcState.Returning,
            Hp = 120,
            MaxHp = 300,
            ActType = 0
        });

        npc.X = npc.SpawnX;
        npc.Y = npc.SpawnY;
        npc.Z = npc.SpawnZ;

        await movementService.HandleReturningAsync(npc, DateTime.UtcNow.Ticks);

        npc.State.Should().Be(NpcState.Standing);
        npc.IsMoving.Should().BeFalse();
        npc.Hp.Should().Be(npc.MaxHp);
        npc.X.Should().Be(npc.SpawnX);
        npc.Y.Should().Be(npc.SpawnY);
        npc.Z.Should().Be(npc.SpawnZ);
    }

    [Fact]
    public async Task NpcAiCombatService_HandleCastingAsync_HealsNpcTarget()
    {
        const int healSkillId = 5001;

        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(healSkillId).Returns(new MagicData
                {
                    Id = healSkillId,
                    Type1 = 3,
                    CastTime = 0
                });
                gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
                {
                    [healSkillId] = new()
                    {
                        Id = healSkillId,
                        FirstDamage = 120
                    }
                });
            });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var combatService = provider.GetRequiredService<INpcAiCombatService>();

        var observerClient = Substitute.For<IClient>();
        observerClient.Id.Returns(Guid.NewGuid());
        var observer = sessionManager.CreateSession(observerClient, characterId: 603, accountId: 703);
        observer.ZoneId = 1;
        observer.X = 50;
        observer.Z = 50;
        observer.Hp = 100;
        sessionManager.Regions.AddToRegion(observer);

        var healer = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = 1,
            X = 50,
            Z = 50,
            SpawnX = 50,
            SpawnZ = 50,
            Hp = 200,
            MaxHp = 200,
            Magic3 = healSkillId,
            ActiveSkillId = healSkillId,
            State = NpcState.Casting
        });

        var ally = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = 1,
            X = 50,
            Z = 51,
            SpawnX = 50,
            SpawnZ = 51,
            Hp = 80,
            MaxHp = 250
        });

        healer.ActiveTargetId = ally.UniqueId;
        healer.HealTargetIsNpc = true;
        healer.CastEndTicks = DateTime.UtcNow.Ticks - 1;

        await combatService.HandleCastingAsync(healer, DateTime.UtcNow.Ticks);

        ally.Hp.Should().Be(200);
        healer.State.Should().Be(NpcState.Standing);
        healer.ActiveSkillId.Should().Be(0);
        healer.ActiveTargetId.Should().Be(0);
        healer.HealTargetIsNpc.Should().BeFalse();
        await observerClient.Received().SendPacket(Arg.Any<Packet>());
    }

    [Fact]
    public async Task NpcAiDeathService_HandlePlayerKilledByNpcAsync_ClearsTransientState()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMaxExpForLevel(10).Returns(1000L);
                gameData.PremiumItemTable.Returns(new Dictionary<byte, PremiumItemData>());
            });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var deathService = provider.GetRequiredService<INpcAiDeathService>();

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = sessionManager.CreateSession(client, characterId: 604, accountId: 704);
        session.Level = 10;
        session.Experience = 500;
        session.Nation = AccountNation.Karus;
        session.ZoneId = 1;
        session.X = 50;
        session.Z = 50;
        session.Hp = 0;
        session.MaxHp = 100;
        session.Trade.ExchangeUser = 999;
        session.Trade.ExchangeOk = true;
        session.Trade.ExchangeItemList.Add(new ExchangeItem
        {
            SrcPos = 0,
            ItemId = 123,
            Count = 1,
            Durability = 100
        });
        session.Trade.MerchantState = MerchantMode.Selling;
        session.IsMining = true;
        sessionManager.Regions.AddToRegion(session);

        var observerClient = Substitute.For<IClient>();
        observerClient.Id.Returns(Guid.NewGuid());
        var observer = sessionManager.CreateSession(observerClient, characterId: 605, accountId: 705);
        observer.ZoneId = 1;
        observer.X = 51;
        observer.Z = 50;
        observer.Hp = 100;
        sessionManager.Regions.AddToRegion(observer);

        var npc = new NpcInstance
        {
            NpcType = 12
        };

        await deathService.HandlePlayerKilledByNpcAsync(session, npc);

        session.Trade.IsTrading.Should().BeFalse();
        session.Trade.ExchangeOk.Should().BeFalse();
        session.Trade.ExchangeItemList.Should().BeEmpty();
        session.Trade.IsMerchanting.Should().BeFalse();
        session.IsMining.Should().BeFalse();
        session.KillerNpcType.Should().Be(12);
        session.Experience.Should().Be(490);
        await client.Received().SendPacket(Arg.Any<Packet>());
        await observerClient.Received().SendPacket(Arg.Any<Packet>());
    }

    [Fact]
    public async Task NpcAiDeathService_HandlePlayerKilledByNpcAsync_AppliesFivePercentExpLossForMonsters()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMaxExpForLevel(10).Returns(1000L);
                gameData.PremiumItemTable.Returns(new Dictionary<byte, PremiumItemData>());
            });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var deathService = provider.GetRequiredService<INpcAiDeathService>();

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = sessionManager.CreateSession(client, characterId: 606, accountId: 706);
        session.Level = 10;
        session.Experience = 500;
        session.Nation = AccountNation.Karus;
        session.ZoneId = 1;
        session.X = 50;
        session.Z = 50;
        session.Hp = 0;
        session.MaxHp = 100;
        sessionManager.Regions.AddToRegion(session);

        var npc = new NpcInstance
        {
            IsMonster = true,
            NpcType = 0
        };

        await deathService.HandlePlayerKilledByNpcAsync(session, npc);

        session.Experience.Should().Be(450);
        session.KillerNpcType.Should().Be(0);
        await client.Received().SendPacket(Arg.Any<Packet>());
    }

    [Fact]
    public async Task NpcAiCombatService_ExecuteAttackAsync_BroadcastsFailPacketWhenPhysicalDamageIsBlocked()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var combatService = provider.GetRequiredService<INpcAiCombatService>();

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var session = sessionManager.CreateSession(client, characterId: 607, accountId: 707);
        session.ZoneId = 21;
        session.X = 63.8f;
        session.Z = 41.6f;
        session.Hp = 100;
        session.MaxHp = 100;
        session.BlockPhysical = true;
        sessionManager.Regions.AddToRegion(session);

        var worm = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = 21,
            X = 63.9f,
            Z = 41.9f,
            SpawnX = 63.9f,
            SpawnZ = 41.9f,
            Attack1 = 15,
            AttackRange = 5,
            HitRate = AlwaysHits,
            Hp = 100,
            MaxHp = 100,
            NpcType = 0
        });

        await combatService.ExecuteAttackAsync(worm, session, DateTime.UtcNow.Ticks);

        session.Hp.Should().Be(100);
        var attackPacket = sentPackets.Should().ContainSingle(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ATTACK).Subject;
        attackPacket.ResetOffset();
        attackPacket.ReadByte().Should().Be(1);
        attackPacket.ReadByte().Should().Be(0);
        attackPacket.ReadInt().Should().Be(worm.UniqueId);
        attackPacket.ReadInt().Should().Be(session.CharacterId);
    }

    [Fact]
    public async Task NpcAiCombatService_ExecuteAttackAsync_DoesNotForceOneDamageWhenAcFullyNegatesHitBase()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var combatService = provider.GetRequiredService<INpcAiCombatService>();

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = sessionManager.CreateSession(client, characterId: 608, accountId: 708);
        session.ZoneId = 21;
        session.X = 63.8f;
        session.Z = 41.6f;
        session.Hp = 100;
        session.MaxHp = 100;
        session.Stats.TotalAc = short.MaxValue;
        session.Stats.TotalEvasionrate = 1;
        sessionManager.Regions.AddToRegion(session);

        var worm = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = 21,
            X = 63.9f,
            Z = 41.9f,
            SpawnX = 63.9f,
            SpawnZ = 41.9f,
            Attack1 = 15,
            AttackRange = 5,
            HitRate = short.MaxValue,
            Hp = 100,
            MaxHp = 100,
            NpcType = 0
        });

        for (var i = 0; i < 64; i++)
            await combatService.ExecuteAttackAsync(worm, session, DateTime.UtcNow.Ticks + i);

        session.Hp.Should().Be(100);
    }

    [Fact]
    public async Task NpcAiCombatService_ExecuteAttackAsync_UsesNpcPhysicalScaling()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var combatService = provider.GetRequiredService<INpcAiCombatService>();

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = sessionManager.CreateSession(client, characterId: 609, accountId: 709);
        session.ZoneId = 21;
        session.X = 63.8f;
        session.Z = 41.6f;
        session.Hp = 1000;
        session.MaxHp = 1000;
        session.Stats.TotalAc = 531;
        session.Stats.TotalEvasionrate = 1;
        sessionManager.Regions.AddToRegion(session);

        var monster = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = 21,
            X = 63.9f,
            Z = 41.9f,
            SpawnX = 63.9f,
            SpawnZ = 41.9f,
            Attack1 = 285,
            AttackRange = 5,
            HitRate = short.MaxValue,
            Hp = 100,
            MaxHp = 100,
            NpcType = 0
        });

        for (var i = 0; i < 32 && session.Hp == session.MaxHp; i++)
            await combatService.ExecuteAttackAsync(monster, session, DateTime.UtcNow.Ticks + i);

        session.Hp.Should().BeLessThan(session.MaxHp);
    }

    [Fact]
    public async Task NpcAiMovementService_MoveTowardTargetAsync_UsesCombatSpeedDuringChase()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var movementService = provider.GetRequiredService<INpcAiMovementService>();

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = sessionManager.CreateSession(client, characterId: 610, accountId: 710);
        session.ZoneId = 21;
        session.X = 20;
        session.Z = 0;
        session.Hp = 100;
        sessionManager.Regions.AddToRegion(session);

        var npc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = 21,
            X = 0,
            Z = 0,
            SpawnX = 0,
            SpawnZ = 0,
            AttackRange = 5,
            Speed2 = 7,
            State = NpcState.Attacking,
            Hp = 100,
            MaxHp = 100,
            NpcType = 0
        });

        await movementService.MoveTowardTargetAsync(npc, session, DateTime.UtcNow.Ticks);

        npc.X.Should().BeApproximately(1.75f, 0.01f);
        npc.Z.Should().BeApproximately(0f, 0.01f);
        npc.IsMoving.Should().BeTrue();
    }

    [Fact]
    public async Task NpcAiMovementService_MoveTowardPointAsync_FallsBackToDirectStepWhenMapHasNoPath()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var movementService = provider.GetRequiredService<INpcAiMovementService>();

        sessionManager.Maps = CreateMapManagerWithTiles(zoneId: 21, mapSize: 8, unitDistance: 1, defaultEventId: 1);

        var npc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = 21,
            X = 0,
            Z = 0,
            SpawnX = 0,
            SpawnZ = 0,
            Speed2 = 7,
            State = NpcState.Attacking,
            Hp = 100,
            MaxHp = 100,
            NpcType = 0
        });

        await movementService.MoveTowardPointAsync(npc, 10, 0, DateTime.UtcNow.Ticks);

        npc.X.Should().BeApproximately(1.75f, 0.01f);
        npc.Z.Should().BeApproximately(0f, 0.01f);
        npc.TargetX.Should().Be(10);
        npc.TargetZ.Should().Be(0);
        npc.IsMoving.Should().BeTrue();
    }

    [Fact]
    public async Task NpcAiMovementService_MoveTowardPointAsync_UsesActualWalkRateInMovePacket()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var movementService = provider.GetRequiredService<INpcAiMovementService>();

        var observerClient = Substitute.For<IClient>();
        observerClient.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        observerClient.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var observer = sessionManager.CreateSession(observerClient, characterId: 611, accountId: 711);
        observer.ZoneId = 21;
        observer.X = 0;
        observer.Z = 0;
        observer.Hp = 100;
        sessionManager.Regions.AddToRegion(observer);

        var npc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = 21,
            X = 0,
            Z = 0,
            SpawnX = 0,
            SpawnZ = 0,
            Speed1 = 2,
            Speed2 = 7,
            State = NpcState.Moving,
            Hp = 100,
            MaxHp = 100,
            NpcType = 0
        });

        await movementService.MoveTowardPointAsync(npc, 10, 0, DateTime.UtcNow.Ticks);

        var movePacket = sentPackets.Should().ContainSingle(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_NPC_MOVE).Subject;
        movePacket.ResetOffset();
        movePacket.ReadByte().Should().Be(1);
        movePacket.ReadInt().Should().Be(npc.UniqueId);
        movePacket.ReadShort().Should().Be(npc.GetPosX);
        movePacket.ReadShort().Should().Be(npc.GetPosZ);
        movePacket.ReadShort().Should().Be(npc.GetPosY);
        movePacket.ReadUShort().Should().Be(20);
    }

}
