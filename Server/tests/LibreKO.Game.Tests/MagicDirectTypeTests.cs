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

public class MagicDirectTypeTests : GameTestBase
{
    private const int VampiricTouchId = 208650;
    private const int BulletId = 490099;
    private const int MagicHammerId = 490153;
    private const int AngerExplosionId = 205255;
    private const byte RonarkLand = BattleZoneManager.ZONE_RONARK_LAND;
    private const byte FullAngerGauge = 5;

    [Fact]
    public async Task VampiricTouchTakesAShareOfTheTargetsHealthAndGivesItToTheCaster()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(VampiricTouchId).Returns(new MagicData
                {
                    Id = VampiricTouchId,
                    Type1 = 3,
                    Moral = 7,
                    Range = 25
                });
                gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
                {
                    [VampiricTouchId] = new()
                    {
                        Id = VampiricTouchId,
                        DirectType = (byte)MagicDirectType.PercentDrain,
                        FirstDamage = 10
                    }
                });
            });

        var (sessionManager, caster, client) = CreateCaster(provider);
        caster.Hp = 100;
        caster.MaxHp = 500;

        var victim = CreateVictim(sessionManager, nation: AccountNation.ElMorad);
        victim.Hp = 200;
        victim.MaxHp = 400;

        await Cast(provider, client, VampiricTouchId, caster, victim.CharacterId);

        victim.Hp.Should().Be(180, "a ten percent drain takes a tenth of the target's current health");
        caster.Hp.Should().Be(120, "the caster absorbs exactly what the target lost");
    }

    [Fact]
    public async Task AWarriorsDirectDamageDoesNotScaleWithCharisma()
    {
        const int bladeOfHell = 105760;
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(bladeOfHell).Returns(new MagicData { Id = bladeOfHell, Type1 = 3, Moral = 7, Range = 25 });
                gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
                {
                    [bladeOfHell] = new() { Id = bladeOfHell, DirectType = (byte)MagicDirectType.Health, FirstDamage = -500 },
                });
            });

        var (sessionManager, caster, client) = CreateCaster(provider);
        caster.Class = 101;
        caster.Magic = 30;
        var victim = CreateVictim(sessionManager, nation: AccountNation.ElMorad);
        victim.Hp = 2000;
        victim.MaxHp = 2000;

        await Cast(provider, client, bladeOfHell, caster, victim.CharacterId);

        (2000 - victim.Hp).Should().BeGreaterThan(90,
            "a warrior's stomp uses the row's damage as written; charisma scaling is a mage rule");
    }

    [Fact]
    public async Task AnItemGrantedSkillDealsTheFlatDamageInItsColumn()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(BulletId).Returns(new MagicData
                {
                    Id = BulletId,
                    Type1 = 3,
                    Moral = 7,
                    Range = 25
                });
                gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
                {
                    [BulletId] = new()
                    {
                        Id = BulletId,
                        DirectType = (byte)MagicDirectType.RawDamage,
                        FirstDamage = -650
                    }
                });
            });

        var (sessionManager, caster, client) = CreateCaster(provider);
        var victim = CreateVictim(sessionManager, nation: AccountNation.ElMorad);
        victim.Hp = 1000;
        victim.MaxHp = 1000;

        await Cast(provider, client, BulletId, caster, victim.CharacterId);

        victim.Hp.Should().Be(350, "an item-granted skill applies its column unscaled by magic attack");
    }

    [Fact]
    public async Task ADurabilitySkillLeavesHealthAlone()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(MagicHammerId).Returns(new MagicData
                {
                    Id = MagicHammerId,
                    Type1 = 3,
                    Moral = 1,
                    Range = 25
                });
                gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
                {
                    [MagicHammerId] = new()
                    {
                        Id = MagicHammerId,
                        DirectType = (byte)MagicDirectType.Durability,
                        FirstDamage = 2000
                    }
                });
            });

        var (_, caster, client) = CreateCaster(provider);
        caster.Hp = 50;
        caster.MaxHp = 500;

        await Cast(provider, client, MagicHammerId, caster, caster.CharacterId);

        caster.Hp.Should().Be(50, "a repair skill spends its column on item durability, never on health");
    }

    [Fact]
    public async Task AngerExplosionIsRefusedUntilTheGaugeIsFull()
    {
        using var provider = CreateProvider(_ => { }, ConfigureAngerExplosion);

        var sentPackets = new List<Packet>();
        var (sessionManager, caster, client) = CreateCaster(provider, sentPackets);
        caster.AngerGauge = FullAngerGauge - 1;

        var victim = CreateVictim(sessionManager, nation: AccountNation.ElMorad);
        victim.Hp = 1000;
        victim.MaxHp = 1000;

        await Cast(provider, client, AngerExplosionId, caster, victim.CharacterId);

        caster.AngerGauge.Should().Be(FullAngerGauge - 1, "a refused cast must not spend the gauge");
        victim.ActiveOverTimeEffects.Should().BeEmpty("a refused cast applies nothing to the target");
    }

    [Fact]
    public async Task AngerExplosionSpendsTheWholeGauge()
    {
        using var provider = CreateProvider(_ => { }, ConfigureAngerExplosion);

        var sentPackets = new List<Packet>();
        var (sessionManager, caster, client) = CreateCaster(provider, sentPackets);
        caster.AngerGauge = FullAngerGauge;

        var victim = CreateVictim(sessionManager, nation: AccountNation.ElMorad);
        victim.Hp = 1000;
        victim.MaxHp = 1000;

        await Cast(provider, client, AngerExplosionId, caster, victim.CharacterId);

        caster.AngerGauge.Should().Be(0, "the gauge is consumed by the skill that requires it");
        victim.ActiveOverTimeEffects.Should().ContainKey(AngerExplosionId);
    }

    private static void ConfigureAngerExplosion(IGameDataService gameData)
    {
        gameData.GetMagic(AngerExplosionId).Returns(new MagicData
        {
            Id = AngerExplosionId,
            Type1 = 3,
            Moral = 7,
            Range = 25
        });
        gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
        {
            [AngerExplosionId] = new()
            {
                Id = AngerExplosionId,
                DirectType = (byte)MagicDirectType.AngerExplosion,
                FirstDamage = 0,
                TimeDamage = -200,
                Duration = 10
            }
        });
    }

    [Fact]
    public async Task ABladeSkillCastOnTheGroundStrikesEveryEnemyWithinReach()
    {
        const int bladeOfHate = 105725;
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(bladeOfHate).Returns(new MagicData { Id = bladeOfHate, Type1 = 1, Moral = 10, Range = 5 });
                gameData.MagicType1Table.Returns(new Dictionary<int, MagicType1Data>
                {
                    [bladeOfHate] = new() { Id = bladeOfHate, HitType = 0, HitRate = 100, Hit = 150, AddDamage = 100 }
                });
            });
        var sent = new List<Packet>();
        var (sessionManager, caster, client) = CreateCaster(provider, sent);
        caster.Stats.TotalHit = 500;
        caster.Stats.TotalHitrate = 100;
        var near = SpawnWorm(sessionManager, 5000, caster.X + 3, caster.Z);
        var alsoNear = SpawnWorm(sessionManager, 5001, caster.X, caster.Z - 4);
        var far = SpawnWorm(sessionManager, 5002, caster.X + 12, caster.Z);

        await CastAtGround(provider, client, bladeOfHate, caster, (int)caster.X, (int)caster.Z);

        (near.Hp < near.MaxHp || alsoNear.Hp < alsoNear.MaxHp).Should().BeTrue("the blade sweeps every enemy within six units");
        far.Hp.Should().Be(far.MaxHp, "twelve units is outside the sweep");
        sent.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_MAGIC_PROCESS, "the effecting stage is echoed with the ground target");
    }

    private static NpcInstance SpawnWorm(SessionManager sessionManager, int npcId, float x, float z) =>
        sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = npcId,
            Name = "Worm",
            ZoneId = RonarkLand,
            X = x,
            Z = z,
            SpawnX = x,
            SpawnZ = z,
            Hp = 100000,
            MaxHp = 100000,
            Ac = 5,
            EvadeRate = 1,
            AttackRange = 3,
            SearchRange = 8,
            TracingRange = 20
        });

    private static async Task CastAtGround(
        ServiceProvider provider, IClient client, int skillId, UserSession caster, int koX, int koZ)
    {
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Effecting);
        packet.WriteInt(skillId);
        packet.WriteInt(caster.CharacterId);
        packet.WriteInt(-1);
        packet.WriteInt(koX);
        packet.WriteInt(0);
        packet.WriteInt(koZ);
        for (var i = 0; i < 4; i++)
            packet.WriteInt(0);

        await provider.GetRequiredService<IMagicPacketCoordinator>().HandleAsync(client, packet);
    }

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
        caster.Class = 105;
        caster.Level = 60;
        caster.Nation = AccountNation.Karus;
        caster.ZoneId = RonarkLand;
        caster.X = 100;
        caster.Z = 100;
        caster.Hp = 500;
        caster.MaxHp = 500;
        caster.MaxMp = 500;
        caster.Mp = 500;
        sessionManager.Regions.AddToRegion(caster);
        return (sessionManager, caster, client);
    }

    private static UserSession CreateVictim(SessionManager sessionManager, AccountNation nation)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var victim = sessionManager.CreateSession(client, characterId: 701, accountId: 801);
        victim.Name = "Victim";
        victim.Class = 105;
        victim.Level = 60;
        victim.Nation = nation;
        victim.ZoneId = RonarkLand;
        victim.X = 101;
        victim.Z = 100;
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
