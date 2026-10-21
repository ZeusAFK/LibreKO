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

public class MagicClassSkillTests : GameTestBase
{
    private const int FireBlast = 110518;
    private const int MonsterBlast = 300101;
    private const int EventBlast = 491200;
    private const short Mage = 110;
    private const short Warrior = 105;
    private const short Cost = 300;
    private const short CapCost = 10001;

    [Fact]
    public async Task AMageCastsTheirOwnClassesSkill()
    {
        using var provider = Provider();
        var (caster, client) = Caster(provider, Mage);
        var worm = Worm(provider);

        await Cast(provider, client, caster, FireBlast, worm.UniqueId);

        worm.Hp.Should().BeLessThan(worm.MaxHp);
    }

    [Fact]
    public async Task AWarriorCannotCastAMagesSkill()
    {
        using var provider = Provider();
        var (caster, client) = Caster(provider, Warrior);
        var worm = Worm(provider);

        await Cast(provider, client, caster, FireBlast, worm.UniqueId);

        worm.Hp.Should().Be(worm.MaxHp);
    }

    [Fact]
    public async Task AMonstersSkillIsNotForPlayers()
    {
        using var provider = Provider();
        var (caster, client) = Caster(provider, Mage);
        var worm = Worm(provider);

        await Cast(provider, client, caster, MonsterBlast, worm.UniqueId);

        worm.Hp.Should().Be(worm.MaxHp);
    }

    [Fact]
    public async Task ASkillOfNoClassWithoutAnItemIsNotForPlayers()
    {
        using var provider = Provider();
        var (caster, client) = Caster(provider, Warrior);
        var worm = Worm(provider);

        await Cast(provider, client, caster, EventBlast, worm.UniqueId);

        worm.Hp.Should().Be(worm.MaxHp);
    }

    [Fact]
    public async Task AnEventTreeSkillIsRefusedOutsideItsZone()
    {
        using var provider = Provider(tree: ChaosRules.DungeonDefenseSkillTree);
        var (caster, client) = Caster(provider, Warrior);
        var worm = Worm(provider);

        await Cast(provider, client, caster, EventBlast, worm.UniqueId);

        worm.Hp.Should().Be(worm.MaxHp);
    }

    [Fact]
    public async Task AGameMasterMayCastAnySkill()
    {
        using var provider = Provider();
        var (caster, client) = Caster(provider, Warrior);
        caster.IsGM = true;
        caster.GmModeEnabled = false;
        var worm = Worm(provider);

        await Cast(provider, client, caster, MonsterBlast, worm.UniqueId);

        worm.Hp.Should().BeLessThan(worm.MaxHp);
    }

    [Theory]
    [InlineData(400, Cost, true)]
    [InlineData(200, Cost, false)]
    [InlineData(200, CapCost, true)]
    public async Task ASkillThatCostsHpNeedsThatMuchHp(short hp, short hpCost, bool cast)
    {
        using var provider = Provider(hpCost: hpCost);
        var (caster, client) = Caster(provider, Mage);
        caster.Hp = hp;
        var worm = Worm(provider);

        await Cast(provider, client, caster, FireBlast, worm.UniqueId);

        (worm.Hp < worm.MaxHp).Should().Be(cast);
    }

    private static ServiceProvider Provider(short tree = 0, short hpCost = 0) => CreateProvider(_ => { }, gameData =>
    {
        var blasts = new[] { FireBlast, MonsterBlast, EventBlast };
        foreach (var id in blasts)
            gameData.GetMagic(id).Returns(new MagicData
            {
                Id = id, Type1 = (byte)MagicSkillType.OverTime, Moral = 7, Range = 20, Skill = id == EventBlast ? tree : (short)0,
                Hp = hpCost,
            });
        gameData.MagicType3Table.Returns(blasts.ToDictionary(id => id, id => new MagicType3Data
        {
            Id = id, DirectType = 1, FirstDamage = -120, Attribute = 1,
        }));
    });

    private static (UserSession Caster, IClient Client) Caster(ServiceProvider provider, short classId)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessions = provider.GetRequiredService<SessionManager>();
        var caster = sessions.CreateSession(client, characterId: 740, accountId: 840);
        caster.Name = "Caster";
        caster.Class = classId;
        caster.Level = 60;
        caster.Magic = ListedSpellDamageCharisma;
        caster.Nation = AccountNation.Karus;
        caster.ZoneId = 21;
        caster.X = 100;
        caster.Z = 100;
        caster.Hp = caster.MaxHp = 500;
        caster.Mp = caster.MaxMp = 500;
        sessions.Regions.AddToRegion(caster);
        return (caster, client);
    }

    private static NpcInstance Worm(ServiceProvider provider) =>
        provider.GetRequiredService<SessionManager>().Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true, NpcId = 750, Name = "Worm", ZoneId = 21, X = 104, Z = 100, SpawnX = 104,
            SpawnZ = 100, Hp = 5000, MaxHp = 5000, Ac = 5, EvadeRate = 1,
        });

    private static async Task Cast(ServiceProvider provider, IClient client, UserSession caster, int skillId, int targetId)
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
