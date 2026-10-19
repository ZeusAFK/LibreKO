using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class WeaponElementSkillTests : GameTestBase
{
    private const int MeleeSkill = 101001;
    private const int ArrowSkill = 101002;
    private const int HellBreaker = 149101228;
    private const short HellBreakerFire = 155;
    private const int DrainBlade = 190000020;
    private const short DrainBladeHpDrain = 10;
    private const int MirrorShield = 190000021;
    private const short MirrorShieldMirror = 30;
    private const int SwordKind = 1;
    private const int AlwaysHits = 101;
    private const short MonsterFireResistance = 50;
    private const int ResistedFire = 117;
    private const int MonsterHp = 10000;
    private const short PlayerHp = 1000;

    [Fact]
    public async Task AMeleeSkillOnAMonsterAddsTheResistedWeaponFire()
    {
        using var provider = CreateProvider(_ => { }, SkillData);
        var warrior = Warrior(provider, HellBreaker);
        var monster = Monster(provider, MonsterFireResistance);

        await Cast(provider, warrior, MeleeSkill, monster.UniqueId);

        monster.Hp.Should().Be(MonsterHp - ResistedFire);
    }

    [Fact]
    public async Task AnArcherySkillOnAMonsterAddsTheResistedWeaponFire()
    {
        using var provider = CreateProvider(_ => { }, SkillData);
        var warrior = Warrior(provider, HellBreaker);
        var monster = Monster(provider, MonsterFireResistance);
        warrior.PendingArrowHits[ArrowSkill] = 1;

        await Cast(provider, warrior, ArrowSkill, monster.UniqueId);

        monster.Hp.Should().Be(MonsterHp - ResistedFire);
    }

    [Fact]
    public async Task AMeleeSkillOnAPlayerAddsHalfTheResistedWeaponFire()
    {
        using var provider = CreateProvider(_ => { }, SkillData);
        var warrior = Warrior(provider, HellBreaker);
        var enemy = Enemy(provider);
        enemy.Stats.FireR = MonsterFireResistance;

        await Cast(provider, warrior, MeleeSkill, enemy.CharacterId);

        enemy.Hp.Should().Be((short)(PlayerHp - ResistedFire / ItemCombatBonus.PlayerSkillElementDivisor));
    }

    [Fact]
    public async Task AnHpDrainWeaponHealsTheAttackerFromTheHit()
    {
        using var provider = CreateProvider(_ => { }, SkillData);
        var warrior = Warrior(provider, DrainBlade);
        warrior.Hp = 50;
        Monster(provider, fireResistance: 0);
        var monster = provider.GetRequiredService<SessionManager>().Regions.GetAllNpcs().Single();

        await Cast(provider, warrior, MeleeSkill, monster.UniqueId);

        warrior.Hp.Should().Be((short)(50 + HellBreakerFire * DrainBladeHpDrain / 100));
    }

    [Fact]
    public async Task AMirrorShieldReflectsPartOfTheHitOntoTheAttacker()
    {
        using var provider = CreateProvider(_ => { }, SkillData);
        var warrior = Warrior(provider, HellBreaker);
        var enemy = Enemy(provider);
        enemy.Inventory[InventoryConstants.LeftHand].ItemId = MirrorShield;
        enemy.Inventory[InventoryConstants.LeftHand].Count = 1;

        await Cast(provider, warrior, MeleeSkill, enemy.CharacterId);

        warrior.Hp.Should().Be((short)(PlayerHp - HellBreakerFire * MirrorShieldMirror / 300));
    }

    private static void SkillData(IGameDataService gameData)
    {
        gameData.GetMagic(MeleeSkill).Returns(new MagicData { Id = MeleeSkill, Type1 = (byte)MagicSkillType.Melee, Moral = 7 });
        gameData.GetMagic(ArrowSkill).Returns(new MagicData { Id = ArrowSkill, Type1 = (byte)MagicSkillType.Ranged, Moral = 7 });
        gameData.MagicType1Table.Returns(new Dictionary<int, MagicType1Data>
        {
            [MeleeSkill] = new() { Id = MeleeSkill, HitType = 1, HitRate = AlwaysHits, Hit = 100 },
        });
        gameData.MagicType2Table.Returns(new Dictionary<int, MagicType2Data>
        {
            [ArrowSkill] = new() { Id = ArrowSkill, HitType = 1, HitRate = AlwaysHits },
        });
        gameData.GetItem(HellBreaker).Returns(new ItemData { Num = HellBreaker, Kind = SwordKind, FireDamage = HellBreakerFire });
        gameData.GetItem(DrainBlade).Returns(new ItemData
        {
            Num = DrainBlade, Kind = SwordKind, FireDamage = HellBreakerFire, HpDrain = DrainBladeHpDrain,
        });
        gameData.GetItem(MirrorShield).Returns(new ItemData
        {
            Num = MirrorShield, Kind = (byte)ItemKind.Shield, MirrorDamage = MirrorShieldMirror,
        });
    }

    private static UserSession Warrior(ServiceProvider provider, int weapon)
    {
        var warrior = Session(provider, characterId: 5510, AccountNation.Karus, x: 20);
        warrior.Class = 101;
        warrior.Stats = new DerivedStats { TotalHit = 1, TotalHitrate = 1f };
        warrior.Inventory[InventoryConstants.RightHand].ItemId = weapon;
        warrior.Inventory[InventoryConstants.RightHand].Count = 1;
        return warrior;
    }

    private static UserSession Enemy(ServiceProvider provider)
    {
        var enemy = Session(provider, characterId: 5511, AccountNation.ElMorad, x: 21);
        enemy.Class = 201;
        enemy.Stats = new DerivedStats { TotalAc = 0, TotalEvasionrate = 1f };
        return enemy;
    }

    private static UserSession Session(ServiceProvider provider, int characterId, AccountNation nation, float x)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var sessions = provider.GetRequiredService<SessionManager>();
        var session = sessions.CreateSession(client, characterId, accountId: characterId + 1000);
        session.Level = 60;
        session.ZoneId = BattleZoneManager.ZONE_RONARK_LAND;
        session.Nation = nation;
        session.X = x;
        session.Z = 20;
        session.MaxHp = PlayerHp;
        session.Hp = PlayerHp;
        session.MaxMp = 100;
        session.Mp = 100;
        sessions.Regions.AddToRegion(session);
        return session;
    }

    private static NpcInstance Monster(ServiceProvider provider, short fireResistance) =>
        provider.GetRequiredService<SessionManager>().Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = BattleZoneManager.ZONE_RONARK_LAND,
            X = 21,
            Z = 20,
            SpawnX = 21,
            SpawnZ = 20,
            Hp = MonsterHp,
            MaxHp = MonsterHp,
            EvadeRate = 1,
            FireR = fireResistance,
        });

    private static async Task Cast(ServiceProvider provider, UserSession caster, int skillId, int targetId)
    {
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Effecting);
        packet.WriteInt(skillId);
        packet.WriteInt(caster.CharacterId);
        packet.WriteInt(targetId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);

        await provider.GetRequiredService<IMagicPacketCoordinator>().HandleAsync(caster.Client, packet);
    }
}
