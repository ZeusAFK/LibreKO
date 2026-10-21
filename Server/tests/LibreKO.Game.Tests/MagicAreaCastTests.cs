using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class MagicAreaCastTests : GameTestBase
{
    private const int SingleCurse = 111757;
    private const int AreaCurse = 111751;
    private const int Meteor = 110556;
    private const int Slash = 105503;
    private const int Firebolt = 110503;
    private const int BurstNova = 110570;
    private const int Sweep = 105725;
    private const int Flyer = 114515;
    private const int AreaTarget = -1;
    private const byte Ronark = (byte)ZoneId.RonarkLand;
    private const byte Moradon = (byte)ZoneId.Moradon;
    private const short MeteorRange = 20;
    private const int WormHp = 100_000;
    private const float Here = 300f;

    [Theory]
    [InlineData(AreaCurse, true)]
    [InlineData(SingleCurse, false)]
    public async Task OnlyAnAreaCurseCanBeAimedAtTheArea(int curse, bool cursed)
    {
        using var provider = Provider();
        var priest = Player(provider, 780, AccountNation.Karus, Here);
        var enemy = Player(provider, 781, AccountNation.ElMorad, Here + 3);

        await Cast(provider, priest, curse, AreaTarget, Here, Here);

        enemy.ActiveBuffs.ContainsKey(curse).Should().Be(cursed);
    }

    [Theory]
    [InlineData(30f, true)]
    [InlineData(60f, false)]
    public async Task AnAreaLandsOnlyWithinTheSkillsRange(float distance, bool hurt)
    {
        using var provider = Provider();
        var mage = Player(provider, 782, AccountNation.Karus, Here, Moradon);
        var worm = Worm(provider, Here + distance);

        await Cast(provider, mage, Meteor, AreaTarget, worm.X, worm.Z);

        (worm.Hp < WormHp).Should().Be(hurt);
    }

    [Fact]
    public async Task ASingleTargetSpellWithARadiusCannotBeCentredOnACrowd()
    {
        using var provider = Provider();
        var mage = Player(provider, 784, AccountNation.Karus, Here, Moradon);
        var aimedAt = Worm(provider, Here + 10);
        var bystander = Worm(provider, Here + 25);

        await Cast(provider, mage, Firebolt, aimedAt.UniqueId, bystander.X, bystander.Z);

        bystander.Hp.Should().Be(WormHp, "the client never sends a centre with a single target");
    }

    [Theory]
    [InlineData(0f, true)]
    [InlineData(60f, false)]
    public async Task ASecondTypeAreaStaysWithinTheSkillsRange(float distance, bool hurt)
    {
        using var provider = Provider();
        var mage = Player(provider, 785, AccountNation.Karus, Here, Moradon);
        var worm = Worm(provider, Here + distance + 2);

        await Cast(provider, mage, BurstNova, AreaTarget, Here + distance, Here);

        (worm.Hp < WormHp).Should().Be(hurt);
    }

    [Theory]
    [InlineData(Sweep, true)]
    [InlineData(Slash, false)]
    [InlineData(Flyer, false)]
    public async Task OnlyAnAreaMeleeSkillSweepsTheArea(int skill, bool hurt)
    {
        using var provider = Provider();
        var warrior = Player(provider, 783, AccountNation.Karus, Here, Moradon);
        NpcInstance[] worms = [Worm(provider, Here + 1), Worm(provider, Here + 2), Worm(provider, Here + 3)];

        await Cast(provider, warrior, skill, AreaTarget, Here + 2, Here);

        worms.Any(worm => worm.Hp < WormHp).Should().Be(hurt, "each blow of a sweep can miss on its own, but not all three together");
    }

    private static ServiceProvider Provider() => CreateProvider(_ => { }, gameData =>
    {
        gameData.GetMagic(SingleCurse).Returns(new MagicData
        {
            Id = SingleCurse, Type1 = (byte)MagicSkillType.Buff, Moral = (byte)SkillMoral.Enemy, Range = 20,
        });
        gameData.GetMagic(AreaCurse).Returns(new MagicData
        {
            Id = AreaCurse, Type1 = (byte)MagicSkillType.Buff, Moral = (byte)SkillMoral.AreaEnemy, Range = 20,
        });
        gameData.MagicType5Table.Returns(new Dictionary<int, MagicType5Data>
        {
            [BurstNova] = new() { Id = BurstNova, Type = (byte)SpecialMagicType.RemoveDot },
        });
        gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data>
        {
            [SingleCurse] = new() { Id = SingleCurse, BuffType = (byte)BuffType.Ac, Ac = -50, Duration = 30 },
            [AreaCurse] = new() { Id = AreaCurse, BuffType = (byte)BuffType.Ac, Ac = -50, Duration = 30, Radius = 10 },
        });
        gameData.GetMagic(Meteor).Returns(new MagicData
        {
            Id = Meteor, Type1 = (byte)MagicSkillType.OverTime, Moral = (byte)SkillMoral.AreaEnemy, Range = MeteorRange,
        });
        gameData.GetMagic(Firebolt).Returns(new MagicData
        {
            Id = Firebolt, Type1 = (byte)MagicSkillType.OverTime, Moral = (byte)SkillMoral.Enemy, Range = MeteorRange,
        });
        gameData.GetMagic(BurstNova).Returns(new MagicData
        {
            Id = BurstNova, Type1 = (byte)MagicSkillType.Special, Type2 = (byte)MagicSkillType.OverTime,
            Moral = (byte)SkillMoral.SelfArea, Range = MeteorRange,
        });
        gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
        {
            [Meteor] = new() { Id = Meteor, DirectType = 1, FirstDamage = -120, Attribute = 1, Radius = 5 },
            [Firebolt] = new() { Id = Firebolt, DirectType = 1, FirstDamage = -120, Attribute = 1, Radius = 5 },
            [BurstNova] = new() { Id = BurstNova, DirectType = 1, FirstDamage = -120, Attribute = 1, Radius = 5 },
            [Sweep] = new() { Id = Sweep, Radius = 5 },
        });
        foreach (var (id, moral) in new[] { (Slash, SkillMoral.Enemy), (Sweep, SkillMoral.AreaEnemy), (Flyer, SkillMoral.AreaEnemy) })
            gameData.GetMagic(id).Returns(new MagicData { Id = id, Type1 = (byte)MagicSkillType.Melee, Moral = (byte)moral, Range = 5 });
        gameData.MagicType1Table.Returns(new Dictionary<int, MagicType1Data>
        {
            [Slash] = new() { Id = Slash, HitType = 0, HitRate = 1000, Hit = 300 },
            [Sweep] = new() { Id = Sweep, HitType = 0, HitRate = 1000, Hit = 300 },
            [Flyer] = new() { Id = Flyer, HitType = 0, HitRate = 1000, Hit = 300 },
        });
    });

    private static UserSession Player(ServiceProvider provider, int id, AccountNation nation, float x, byte zone = Ronark)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessions = provider.GetRequiredService<SessionManager>();
        var player = sessions.CreateSession(client, id, id + 100);
        player.Name = $"Player{id}";
        player.Level = 70;
        player.Magic = ListedSpellDamageCharisma;
        player.Nation = nation;
        player.ZoneId = zone;
        player.X = x;
        player.Z = Here;
        player.Hp = player.MaxHp = 1000;
        player.Mp = player.MaxMp = 1000;
        player.AttackAmount = 100;
        player.Stats.TotalHit = 200;
        player.Stats.TotalHitrate = 100;
        sessions.Regions.AddToRegion(player);
        return player;
    }

    private static NpcInstance Worm(ServiceProvider provider, float x) =>
        provider.GetRequiredService<SessionManager>().Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true, NpcId = 750, Name = "Worm", ZoneId = Moradon, X = x, Z = Here, SpawnX = x,
            SpawnZ = Here, Hp = WormHp, MaxHp = WormHp, Ac = 5, EvadeRate = 1,
        });

    private static async Task Cast(ServiceProvider provider, UserSession caster, int skillId, int targetId, float x, float z)
    {
        TakeTheClassOf(caster, skillId);
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Effecting);
        packet.WriteInt(skillId);
        packet.WriteInt(caster.CharacterId);
        packet.WriteInt(targetId);
        packet.WriteInt((int)x);
        packet.WriteInt(0);
        packet.WriteInt((int)z);
        for (var i = 0; i < 4; i++)
            packet.WriteInt(0);
        await provider.GetRequiredService<IMagicPacketCoordinator>().HandleAsync(caster.Client, packet);
    }
}
