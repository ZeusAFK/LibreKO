using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class NpcSpellTests : GameTestBase
{
    private const int IceComet = 210651;
    private const int ApostleAttack = 3152;
    private const short ApostleEvasion = 85;
    private const short KarusWarrior = 101;
    private const short PlayerHp = 4901;
    private const short HighResistance = 300;
    private const int Casts = 200;
    private const byte Zone = 1;
    private const float Spot = 50f;
    private const byte EnemyMoral = (byte)SkillMoral.Enemy;
    private const byte AreaEnemyMoral = (byte)SkillMoral.AreaEnemy;
    private const byte FriendMoral = (byte)SkillMoral.FriendWithMe;
    private const byte OverTimeType = (byte)MagicSkillType.OverTime;
    private const byte BuffSkillType = (byte)MagicSkillType.Buff;
    private const byte RawDamage = (byte)MagicDirectType.RawDamage;
    private const short RawSpellDamage = -500;
    private const byte NpcTypeUnderTest = 7;
    private const int SlaveId = 704;

    [Fact]
    public async Task TheApostlesIceCometHitsForItsSpellDamageNotItsAttack()
    {
        using var provider = CreateProvider(_ => { }, SpellData(new MagicType3Data
        {
            Id = IceComet, DirectType = (byte)MagicDirectType.Health, FirstDamage = -882, Attribute = (byte)MagicAttribute.Cold,
        }));
        var (player, apostle) = Arena(provider);

        var losses = await CastRepeatedly(provider, player, apostle);

        losses.Should().OnlyContain(loss => loss == 0 || (loss >= 217 && loss <= 230));
        losses.Should().Contain(loss => loss > 0);
    }

    [Fact]
    public async Task AColdResistantPlayerTakesLessFromTheIceComet()
    {
        using var provider = CreateProvider(_ => { }, SpellData(new MagicType3Data
        {
            Id = IceComet, DirectType = (byte)MagicDirectType.Health, FirstDamage = -882, Attribute = (byte)MagicAttribute.Cold,
        }));
        var (player, apostle) = Arena(provider);
        player.Stats.ColdR = 150;

        var losses = await CastRepeatedly(provider, player, apostle);

        losses.Should().OnlyContain(loss => loss == 0 || (loss >= 168 && loss <= 178));
    }

    [Fact]
    public async Task APoisonSpellPoisonsWithoutAnImmediateHit()
    {
        using var provider = CreateProvider(_ => { }, SpellData(new MagicType3Data
        {
            Id = IceComet, FirstDamage = 0, TimeDamage = -120, Duration = 20, Attribute = (byte)MagicAttribute.Poison,
        }));
        var (player, apostle) = Arena(provider);
        var combat = provider.GetRequiredService<INpcAiCombatService>();

        for (var cast = 0; cast < Casts && !player.ActiveOverTimeEffects.ContainsKey(IceComet); cast++)
        {
            ArmCast(apostle, player);
            await combat.HandleCastingAsync(apostle, DateTime.UtcNow.Ticks);
        }

        player.Hp.Should().Be(PlayerHp);
        var poison = player.ActiveOverTimeEffects[IceComet];
        poison.TickLimit.Should().Be(10);
        poison.TickAmount.Should().BeNegative();
        poison.CasterId.Should().Be(-apostle.UniqueId);
        poison.PartyStatusCode.Should().Be((byte)PartyStatusIcon.Poison);
    }

    [Fact]
    public async Task ARawDamageSpellIgnoresResistance()
    {
        using var provider = CreateProvider(_ => { }, SpellData(new MagicType3Data
        {
            Id = IceComet, DirectType = RawDamage, FirstDamage = RawSpellDamage, Attribute = (byte)MagicAttribute.Cold,
        }));
        var (player, apostle) = Arena(provider);
        player.Stats.ColdR = HighResistance;

        ArmCast(apostle, player);
        await provider.GetRequiredService<INpcAiCombatService>().HandleCastingAsync(apostle, DateTime.UtcNow.Ticks);

        player.Hp.Should().Be((short)(PlayerHp + RawSpellDamage));
    }

    [Fact]
    public async Task AMonsterNeverCastsAFriendlyHealAtAPlayer()
    {
        using var provider = CreateProvider(_ => { }, SpellData(new MagicType3Data
        {
            Id = IceComet, DirectType = (byte)MagicDirectType.Health, FirstDamage = 1152,
        }, moral: FriendMoral));
        var (player, apostle) = Arena(provider);
        apostle.State = NpcState.Fighting;

        var cast = await provider.GetRequiredService<INpcAiMagicService>()
            .TryStartAttackCastAsync(apostle, player, DateTime.UtcNow.Ticks);

        cast.Should().BeFalse();
        apostle.State.Should().Be(NpcState.Fighting);
        player.Hp.Should().Be(PlayerHp);
    }

    [Fact]
    public async Task AnAreaSpellHitsEveryPlayerWithinItsRadius()
    {
        using var provider = CreateProvider(_ => { }, SpellData(new MagicType3Data
        {
            Id = IceComet, DirectType = RawDamage, FirstDamage = RawSpellDamage, Radius = 5,
        }, moral: AreaEnemyMoral));
        var (player, apostle) = Arena(provider);
        var neighbour = Player(provider, characterId: 702, x: Spot + 3);
        var faraway = Player(provider, characterId: 703, x: Spot + 10);

        ArmCast(apostle, player);
        await provider.GetRequiredService<INpcAiCombatService>().HandleCastingAsync(apostle, DateTime.UtcNow.Ticks);

        player.Hp.Should().Be((short)(PlayerHp + RawSpellDamage));
        neighbour.Hp.Should().Be((short)(PlayerHp + RawSpellDamage));
        faraway.Hp.Should().Be(PlayerHp);
    }

    [Fact]
    public async Task AnAreaSpellPassesOverASlaveMerchant()
    {
        using var provider = CreateProvider(_ => { }, SpellData(new MagicType3Data
        {
            Id = IceComet, DirectType = RawDamage, FirstDamage = RawSpellDamage, Radius = 5,
        }, moral: AreaEnemyMoral));
        var (player, apostle) = Arena(provider);
        var sessions = provider.GetRequiredService<SessionManager>();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var slave = new SlaveMerchantBotSession(client, SlaveId, SlaveId)
        {
            Name = "Slave", Nation = AccountNation.ElMorad, ZoneId = Zone, X = Spot + 3, Z = Spot, MaxHp = PlayerHp, Hp = PlayerHp,
        };
        sessions.RegisterSession(slave);
        sessions.Regions.AddToRegion(slave);

        ArmCast(apostle, player);
        await provider.GetRequiredService<INpcAiCombatService>().HandleCastingAsync(apostle, DateTime.UtcNow.Ticks);

        player.Hp.Should().Be((short)(PlayerHp + RawSpellDamage));
        slave.Hp.Should().Be(PlayerHp, "a slave's stall is not something a monster can kill");
    }

    [Fact]
    public async Task TheIceCometSlowLandsAfterTheHit()
    {
        using var provider = CreateProvider(_ => { }, SpellData(new MagicType3Data
        {
            Id = IceComet, DirectType = RawDamage, FirstDamage = RawSpellDamage, Attribute = (byte)MagicAttribute.Cold,
        }, secondType: BuffSkillType, type4: new MagicType4Data
        {
            Id = IceComet, BuffType = (byte)BuffType.Speed2, Duration = 19, Speed = 32,
        }));
        var (player, apostle) = Arena(provider);

        ArmCast(apostle, player);
        await provider.GetRequiredService<INpcAiCombatService>().HandleCastingAsync(apostle, DateTime.UtcNow.Ticks);

        player.Hp.Should().Be((short)(PlayerHp + RawSpellDamage));
        player.ActiveBuffs.Should().ContainKey(IceComet);
        player.ActiveBuffs[IceComet].CasterId.Should().Be(-apostle.UniqueId);
    }

    [Fact]
    public async Task ACasterOutsideMeleeReachCastsEveryAttack()
    {
        using var provider = CreateProvider(_ => { }, SpellData(new MagicType3Data
        {
            Id = IceComet, DirectType = (byte)MagicDirectType.Health, FirstDamage = -882, Attribute = (byte)MagicAttribute.Cold,
        }, castTime: 15));
        var (player, apostle) = Arena(provider);
        apostle.X = Spot + 10;
        apostle.State = NpcState.Fighting;
        var combat = provider.GetRequiredService<INpcAiCombatService>();

        for (var attack = 0; attack < 20; attack++)
        {
            apostle.State = NpcState.Fighting;
            await combat.ExecuteAttackAsync(apostle, player, DateTime.UtcNow.Ticks + attack);
            apostle.State.Should().Be(NpcState.Casting);
        }
    }

    [Fact]
    public async Task AMonstersDamageOverTimeKillIsCreditedToTheMonster()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
        {
            gameData.GetMaxExpForLevel(Arg.Any<byte>()).Returns(1000L);
            gameData.PremiumItemTable.Returns(new Dictionary<byte, PremiumItemData>());
        });
        var (player, apostle) = Arena(provider);
        apostle.NpcType = NpcTypeUnderTest;
        player.Hp = 5;
        player.ActiveOverTimeEffects[IceComet] = new ActiveOverTimeEffect
        {
            MagicId = IceComet,
            CasterId = -apostle.UniqueId,
            TickAmount = -10,
            TickLimit = 5,
            NextTickTicks = DateTime.UtcNow.Ticks - 1,
        };
        var expiry = new BuffExpiryService(
            provider.GetRequiredService<SessionManager>(),
            provider.GetRequiredService<IMagicExecutionService>(),
            provider.GetRequiredService<ICombatLifecycleService>(),
            provider.GetRequiredService<ICombatNotificationService>(),
            provider.GetRequiredService<IControlStateService>(),
            provider.GetRequiredService<IRespawnBlinkService>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BuffExpiryService>>(),
            npcAiDeathService: provider.GetRequiredService<INpcAiDeathService>());

        await expiry.ProcessTickAsync(DateTime.UtcNow.Ticks);

        player.Hp.Should().Be(0);
        player.KillerNpcType.Should().Be(NpcTypeUnderTest);
    }

    [Theory]
    [InlineData(0, 2, 3, 2)]
    [InlineData(29, 2, 3, 2)]
    [InlineData(30, 2, 3, 3)]
    [InlineData(59, 2, 3, 3)]
    [InlineData(60, 2, 3, 1)]
    [InlineData(10, 0, 3, 3)]
    [InlineData(45, 2, 0, 1)]
    [InlineData(400, 2, 3, 1)]
    public void AMonsterPicksItsSecondAndThirdSpellsByRoll(int roll, int magic2, int magic3, int expected)
    {
        NpcSpellChoice.Pick(1, magic2, magic3, roll).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(999, true)]
    [InlineData(1000, false)]
    [InlineData(3000, false)]
    public void AMonsterInMeleeReachCastsAThirdOfTheTime(int roll, bool casts)
    {
        NpcSpellChoice.CastsInMelee(roll).Should().Be(casts);
    }

    private static Action<IGameDataService> SpellData(
        MagicType3Data type3, byte moral = EnemyMoral, byte secondType = 0, MagicType4Data? type4 = null, byte castTime = 0) =>
        gameData =>
        {
            gameData.GetMagic(IceComet).Returns(new MagicData
            {
                Id = IceComet, Type1 = OverTimeType, Type2 = secondType, Moral = moral, Range = 25, CastTime = castTime,
            });
            gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data> { [IceComet] = type3 });
            gameData.MagicType4Table.Returns(type4 == null
                ? new Dictionary<int, MagicType4Data>()
                : new Dictionary<int, MagicType4Data> { [IceComet] = type4 });
        };

    private static (UserSession Player, NpcInstance Apostle) Arena(ServiceProvider provider)
    {
        var player = Player(provider, characterId: 701, x: Spot);
        var apostle = provider.GetRequiredService<SessionManager>().Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = Zone,
            X = Spot + 2,
            Z = Spot,
            SpawnX = Spot + 2,
            SpawnZ = Spot,
            Hp = 50000,
            MaxHp = 50000,
            Attack1 = ApostleAttack,
            EvadeRate = ApostleEvasion,
            MagicAttack = 1,
            Magic1 = IceComet,
        });
        apostle.TargetUserId = player.CharacterId;
        return (player, apostle);
    }

    private static UserSession Player(ServiceProvider provider, int characterId, float x)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sessions = provider.GetRequiredService<SessionManager>();
        var player = sessions.CreateSession(client, characterId, accountId: characterId + 1000);
        player.Class = KarusWarrior;
        player.Nation = AccountNation.Karus;
        player.ZoneId = Zone;
        player.X = x;
        player.Z = Spot;
        player.MaxHp = PlayerHp;
        player.Hp = PlayerHp;
        player.MaxMp = 1000;
        player.Mp = 1000;
        sessions.Regions.AddToRegion(player);
        return player;
    }

    private static void ArmCast(NpcInstance apostle, UserSession player)
    {
        apostle.State = NpcState.Casting;
        apostle.ActiveSkillId = IceComet;
        apostle.ActiveTargetId = player.CharacterId;
        apostle.CastEndTicks = DateTime.UtcNow.Ticks - 1;
    }

    private static async Task<List<int>> CastRepeatedly(ServiceProvider provider, UserSession player, NpcInstance apostle)
    {
        var combat = provider.GetRequiredService<INpcAiCombatService>();
        var losses = new List<int>();
        for (var cast = 0; cast < Casts; cast++)
        {
            player.Hp = PlayerHp;
            ArmCast(apostle, player);
            await combat.HandleCastingAsync(apostle, DateTime.UtcNow.Ticks);
            losses.Add(PlayerHp - player.Hp);
        }

        return losses;
    }
}
