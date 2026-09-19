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

public class MagicAreaTests : GameTestBase
{
    private const int BindingId = 105630;
    private const int SleepWingId = 111730;
    private const int SleepCarpetId = 111751;
    private const byte RonarkLand = BattleZoneManager.ZONE_RONARK_LAND;
    private const byte AreaRadius = 30;
    private const short SleepSeconds = 20;

    [Fact]
    public async Task SleepWingPutsTheMonsterToSleepAndTellsEveryoneNearby()
    {
        using var provider = CreateProvider(_ => { }, gameData => Type7(gameData, SleepWingId,
            new MagicType7Data { Id = SleepWingId, TargetChange = (byte)MagicAreaTargetChange.Sleep, Duration = SleepSeconds }));

        var sent = new List<Packet>();
        var (sessionManager, caster, client) = CreateCaster(provider, sent);
        var monster = SpawnMonster(sessionManager, x: 101, z: 100);

        await Cast(provider, client, SleepWingId, caster, monster.UniqueId);

        monster.State.Should().Be(NpcState.Sleeping);
        monster.WakeTicks.Should().BeGreaterThan(DateTime.UtcNow.Ticks);

        var pose = sent.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_STATE_CHANGE);
        pose.ReadInt().Should().Be(monster.UniqueId);
        pose.ReadByte().Should().Be((byte)StateChangeType.Pose);
        pose.ReadInt().Should().Be((byte)NpcPoseState.Asleep);
    }

    [Fact]
    public async Task ASleepingMonsterWakesWhenItsTimeIsUp()
    {
        using var provider = CreateProvider(_ => { }, gameData => Type7(gameData, SleepWingId,
            new MagicType7Data { Id = SleepWingId, TargetChange = (byte)MagicAreaTargetChange.Sleep, Duration = SleepSeconds }));

        var sent = new List<Packet>();
        var (sessionManager, caster, client) = CreateCaster(provider, sent);
        var monster = SpawnMonster(sessionManager, x: 101, z: 100);

        await Cast(provider, client, SleepWingId, caster, monster.UniqueId);
        sent.Clear();

        await provider.GetRequiredService<INpcAiBehaviorService>()
            .ProcessNpcAsync(monster, monster.WakeTicks - 1);
        monster.State.Should().Be(NpcState.Sleeping, "the skill has not run its course yet");

        await provider.GetRequiredService<INpcAiBehaviorService>()
            .ProcessNpcAsync(monster, monster.WakeTicks);

        monster.State.Should().NotBe(NpcState.Sleeping);
        var pose = sent.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_STATE_CHANGE);
        pose.ReadInt().Should().Be(monster.UniqueId);
        pose.ReadByte().Should().Be((byte)StateChangeType.Pose);
        pose.ReadInt().Should().Be((byte)NpcPoseState.Awake);
    }

    [Fact]
    public async Task BindingHurtsAMonsterAndTakesItsAttention()
    {
        using var provider = CreateProvider(_ => { }, gameData => Type7(gameData, BindingId,
            new MagicType7Data { Id = BindingId, TargetChange = (byte)MagicAreaTargetChange.Provoke, Damage = 10, Duration = 9 }));

        var (sessionManager, caster, client) = CreateCaster(provider);
        var monster = SpawnMonster(sessionManager, x: 101, z: 100);

        await Cast(provider, client, BindingId, caster, monster.UniqueId);

        monster.Hp.Should().Be(990, "the column is applied flat, never through the magic formula");
        monster.TargetUserId.Should().Be(caster.CharacterId);
    }

    [Fact]
    public async Task BindingCanPullAMonsterOffAnotherPlayer()
    {
        using var provider = CreateProvider(_ => { }, gameData => Type7(gameData, BindingId,
            new MagicType7Data { Id = BindingId, TargetChange = (byte)MagicAreaTargetChange.Provoke, Damage = 10, Duration = 9 }));

        var (sessionManager, caster, client) = CreateCaster(provider);
        var ally = CreateVictim(sessionManager);
        ally.Nation = AccountNation.Karus;

        int switched = 0;
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var monster = SpawnMonster(sessionManager, x: 101, z: 100);
            monster.TargetUserId = ally.CharacterId;
            monster.State = NpcState.Fighting;

            await Cast(provider, client, BindingId, caster, monster.UniqueId);

            if (monster.TargetUserId == caster.CharacterId)
                switched++;
        }

        switched.Should().BeGreaterThan(0, "a taunt has to be able to take a monster off a party member");
    }

    [Fact]
    public async Task BindingLeavesAnEnemyPlayerAlone()
    {
        using var provider = CreateProvider(_ => { }, gameData => Type7(gameData, BindingId,
            new MagicType7Data { Id = BindingId, TargetChange = (byte)MagicAreaTargetChange.Provoke, Damage = 10, Duration = 9 }));

        var (sessionManager, caster, client) = CreateCaster(provider);
        var victim = CreateVictim(sessionManager);

        await Cast(provider, client, BindingId, caster, victim.CharacterId);

        victim.Hp.Should().Be(400, "these skills reach monsters only");
    }

    [Fact]
    public async Task SleepCarpetReachesOnlyAsFarAsItsRadius()
    {
        using var provider = CreateProvider(_ => { }, gameData => Type7(gameData, SleepCarpetId,
            new MagicType7Data
            {
                Id = SleepCarpetId,
                TargetChange = (byte)MagicAreaTargetChange.Sleep,
                Duration = SleepSeconds,
                Radius = AreaRadius
            }));

        var (sessionManager, caster, client) = CreateCaster(provider);
        var near = SpawnMonster(sessionManager, x: 120, z: 100);
        var far = SpawnMonster(sessionManager, x: 140, z: 100);

        await Cast(provider, client, SleepCarpetId, caster, targetId: -1);

        near.State.Should().Be(NpcState.Sleeping);
        far.State.Should().NotBe(NpcState.Sleeping, "the radius is in world units, not tenths of one");
    }

    private static void Type7(IGameDataService gameData, int skillId, MagicType7Data row)
    {
        gameData.GetMagic(skillId).Returns(new MagicData
        {
            Id = skillId,
            Type1 = 7,
            Moral = row.Radius > 0 ? (byte)10 : (byte)7,
            Range = 25
        });
        gameData.MagicType7Table.Returns(new Dictionary<int, MagicType7Data> { [skillId] = row });
    }

    private static NpcInstance SpawnMonster(SessionManager sessionManager, float x, float z) =>
        sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 750,
            Name = "Worm",
            NpcType = NpcData.TypeMonster,
            ZoneId = RonarkLand,
            X = x,
            Z = z,
            SpawnX = x,
            SpawnZ = z,
            Hp = 1000,
            MaxHp = 1000,
            AttackRange = 3,
            SearchRange = 8,
            TracingRange = 20
        });

    private static (SessionManager Sessions, UserSession Caster, IClient Client) CreateCaster(ServiceProvider provider)
        => CreateCaster(provider, new List<Packet>());

    private static (SessionManager Sessions, UserSession Caster, IClient Client) CreateCaster(
        ServiceProvider provider, List<Packet> sentPackets)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Do<Packet>(sentPackets.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var caster = sessionManager.CreateSession(client, characterId: 700, accountId: 800);
        caster.Name = "Caster";
        caster.Class = 111;
        caster.Level = 60;
        caster.Nation = AccountNation.Karus;
        caster.ZoneId = RonarkLand;
        caster.X = 100;
        caster.Z = 100;
        caster.Hp = 500;
        caster.MaxHp = 500;
        caster.Mp = 500;
        caster.MaxMp = 500;
        sessionManager.Regions.AddToRegion(caster);
        return (sessionManager, caster, client);
    }

    private static UserSession CreateVictim(SessionManager sessionManager)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var victim = sessionManager.CreateSession(client, characterId: 701, accountId: 801);
        victim.Name = "Victim";
        victim.Class = 105;
        victim.Level = 60;
        victim.Nation = AccountNation.ElMorad;
        victim.ZoneId = RonarkLand;
        victim.X = 101;
        victim.Z = 100;
        victim.Hp = 400;
        victim.MaxHp = 400;
        sessionManager.Regions.AddToRegion(victim);
        return victim;
    }

    private static async Task Cast(
        ServiceProvider provider, IClient client, int skillId, UserSession caster, int targetId)
    {
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Effecting);
        packet.WriteInt(skillId);
        packet.WriteInt(caster.CharacterId);
        packet.WriteInt(targetId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);

        await provider.GetRequiredService<IMagicPacketCoordinator>().HandleAsync(client, packet);
    }
}
