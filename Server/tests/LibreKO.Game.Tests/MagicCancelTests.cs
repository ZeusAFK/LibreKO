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

public class MagicCancelTests : GameTestBase
{
    private const int ArcShot = 102010;
    private const int MultipleShot = 102040;
    private const int Healing = 211509;
    private const int FireBlast = 110518;

    [Fact]
    public async Task CancellingADrawFreesTheCasterForTheNextSkill()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(ArcShot).Returns(Ranged(ArcShot, castTenths: 13));
                gameData.GetMagic(MultipleShot).Returns(Ranged(MultipleShot, castTenths: 10));
            });
        var (caster, client, sent) = CreateArcher(provider);
        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();

        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Casting, ArcShot, caster, 0));
        Replies(sent).Should().Equal((MagicProcessOpcode.Casting, ArcShot));
        sent.Clear();

        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Fail, ArcShot, caster, caster.CharacterId));
        sent.Clear();

        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Casting, MultipleShot, caster, 0));
        Replies(sent).Should().Equal(new[] { (MagicProcessOpcode.Casting, MultipleShot) },
            "a cancelled draw must not leave the caster marked as still casting");
        sent.Clear();

        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Fail, MultipleShot, caster, caster.CharacterId));
        sent.Clear();
        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Casting, ArcShot, caster, 0));
        Replies(sent).Should().Equal(new[] { (MagicProcessOpcode.Casting, ArcShot) },
            "cancelling refunds the skill's own cooldown too");
    }

    [Fact]
    public async Task CancellingAHealMidCastAbortsItInsteadOfLandingIt()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData => gameData.GetMagic(Healing).Returns(new MagicData
            {
                Id = Healing, Type1 = 3, Moral = 2, Range = 20, CastTime = 15, ReCastTime = 0,
            }));
        var (caster, client, sent) = CreateArcher(provider);
        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();

        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Casting, Healing, caster, caster.CharacterId));
        caster.CastingSkillId.Should().Be(Healing);
        sent.Clear();

        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Fail, Healing, caster, caster.CharacterId));

        caster.CastingSkillId.Should().Be(0, "moving during the cast cancels the heal");
        Replies(sent).Should().BeEmpty("a cancelled heal must not execute or echo an effecting stage");
    }

    [Fact]
    public async Task MovingAfterASpellIsReleasedDoesNotTurnItOnTheCaster()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(FireBlast).Returns(new MagicData
                {
                    Id = FireBlast, Type1 = 3, Moral = 7, Range = 20, CastTime = 2, ReCastTime = 50,
                });
                gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
                {
                    [FireBlast] = new()
                    {
                        Id = FireBlast, DirectType = 1, FirstDamage = -120, TimeDamage = -60, Duration = 6,
                        Attribute = 1,
                    },
                });
            });
        var (caster, client, sent) = CreateArcher(provider);
        var sessions = provider.GetRequiredService<SessionManager>();
        var worm = sessions.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true, NpcId = 750, Name = "Worm", ZoneId = 21, X = 104, Z = 100, SpawnX = 104,
            SpawnZ = 100, Hp = 5000, MaxHp = 5000, Ac = 5, EvadeRate = 1,
        });
        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();

        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Casting, FireBlast, caster, worm.UniqueId));
        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Effecting, FireBlast, caster, worm.UniqueId));
        worm.Hp.Should().BeLessThan(5000);
        sent.Clear();

        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Fail, FireBlast, caster, caster.CharacterId));

        caster.Hp.Should().Be(500, "the fail the client sends on movement names the caster, and must not execute");
        caster.ActiveOverTimeEffects.Should().BeEmpty("the burn belongs to the monster");
        worm.ActiveOverTimeEffects.Should().ContainKey(FireBlast);
        Replies(sent).Should().NotContain((MagicProcessOpcode.Effecting, FireBlast));
        caster.SkillCooldowns.Should().ContainKey(FireBlast, "a fail after the release must not refund the cooldown");
    }

    [Fact]
    public async Task ASpellInFlightLandsAndDoesNotBlockTheNextCast()
    {
        const int fireBall = 110515;
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(fireBall).Returns(new MagicData
                {
                    Id = fireBall, Type1 = 3, Moral = 7, Range = 35, CastTime = 2, ReCastTime = 0,
                });
                gameData.GetMagic(FireBlast).Returns(new MagicData
                {
                    Id = FireBlast, Type1 = 3, Moral = 7, Range = 35, CastTime = 2, ReCastTime = 50,
                });
                gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
                {
                    [fireBall] = new() { Id = fireBall, DirectType = 1, FirstDamage = -120, Attribute = 1 },
                    [FireBlast] = new() { Id = FireBlast, DirectType = 1, FirstDamage = -120, Attribute = 1 },
                });
            });
        var (caster, client, sent) = CreateArcher(provider);
        var worm = provider.GetRequiredService<SessionManager>().Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true, NpcId = 750, Name = "Worm", ZoneId = 21, X = 104, Z = 100, SpawnX = 104,
            SpawnZ = 100, Hp = 5000, MaxHp = 5000, Ac = 5, EvadeRate = 1,
        });
        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();

        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Casting, fireBall, caster, worm.UniqueId));
        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Flying, fireBall, caster, worm.UniqueId));
        await Task.Delay(300);
        sent.Clear();

        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Casting, FireBlast, caster, worm.UniqueId));
        Replies(sent).Should().Equal(new[] { (MagicProcessOpcode.Casting, FireBlast) },
            "the fireball has left the caster, the next spell may start while it flies");

        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Effecting, fireBall, caster, worm.UniqueId));
        worm.Hp.Should().BeLessThan(5000, "the fireball lands when it arrives");
    }

    [Fact]
    public async Task MissedVolleyArrowsReleaseTheDrawWithoutRefundingIt()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(MultipleShot).Returns(Ranged(MultipleShot, castTenths: 2));
                gameData.MagicType2Table.Returns(new Dictionary<int, MagicType2Data>
                {
                    [MultipleShot] = new() { Id = MultipleShot, NeedArrow = 3, HitRate = 100, AddDamage = 100 },
                });
            });
        var (caster, client, _) = CreateArcher(provider);
        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();

        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Casting, MultipleShot, caster, 0));
        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Flying, MultipleShot, caster, 0));
        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Effecting, MultipleShot, caster, 0));
        caster.CastingSkillId.Should().Be(MultipleShot, "two arrows of the volley are still in the air");

        await coordinator.HandleAsync(client, ArrowMissed(MultipleShot, caster));
        await coordinator.HandleAsync(client, ArrowMissed(MultipleShot, caster));

        caster.CastingSkillId.Should().Be(0, "every arrow has landed or missed, the draw is over");
        caster.SkillCooldowns.Should().ContainKey(MultipleShot, "a missed arrow is not a cancelled cast");
    }

    private static Packet ArrowMissed(int skillId, UserSession caster)
    {
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Fail);
        packet.WriteInt(skillId);
        packet.WriteInt(caster.CharacterId);
        packet.WriteInt(-1);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(i == 3 ? -101 : 0);
        return packet;
    }

    [Fact]
    public async Task AResistanceBuffOnYourselfIsAnnouncedWithItsDuration()
    {
        const int resistFire = 110506;
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(resistFire).Returns(new MagicData
                {
                    Id = resistFire, Type1 = 4, Moral = 2, Range = 20, CastTime = 2, ReCastTime = 0,
                });
                gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data>
                {
                    [resistFire] = new()
                    {
                        Id = resistFire, BuffType = 8, Duration = 300, AttackSpeed = 100, Speed = 100, AcPct = 100,
                        Attack = 100, MagicAttack = 100, MaxHPPct = 100, MaxMPPct = 100, HitRate = 100, AvoidRate = 100,
                        FireR = 20, ExpPct = 100,
                    },
                });
            });
        var (caster, client, sent) = CreateArcher(provider);
        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();

        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Casting, resistFire, caster, caster.CharacterId));
        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Effecting, resistFire, caster, caster.CharacterId));

        caster.ActiveBuffs.Should().ContainKey(resistFire);
        var effecting = sent.Select(p => { p.ResetOffset(); return p; })
            .Where(p => p.GetOpcode() == (byte)GameOpcodes.GS_MAGIC_PROCESS)
            .Select(p => (Sub: p.ReadByte(), Skill: p.ReadInt(), Caster: p.ReadInt(), Target: p.ReadInt(),
                D0: p.ReadInt(), D1: p.ReadInt(), D2: p.ReadInt(), Duration: p.ReadInt()))
            .Where(x => x.Sub == (byte)MagicProcessOpcode.Effecting)
            .ToList();
        effecting.Should().ContainSingle();
        effecting[0].Target.Should().Be(caster.CharacterId);
        effecting[0].Duration.Should().Be(300);
    }

    [Fact]
    public async Task GuardSummonRaisesAGuardOfTheCastersNationThatLeavesWithTheBuff()
    {
        const int guardSummon = 110826;
        const int guardNpc = 8850;
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(guardSummon).Returns(new MagicData
                {
                    Id = guardSummon, Type1 = 9, Moral = 1, CastTime = 0, ReCastTime = 0,
                });
                gameData.MagicType9Table.Returns(new Dictionary<int, MagicType9Data>
                {
                    [guardSummon] = new() { Id = guardSummon, MonsterNum = guardNpc, StateChange = 9, Duration = 50 },
                });
                gameData.GetNpc(guardNpc).Returns(new NpcData { Id = guardNpc, Name = "Guard Summon", IsMonster = true, Hp = 30000 });
            });
        var (caster, client, _) = CreateArcher(provider);
        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();

        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Effecting, guardSummon, caster, caster.CharacterId));

        var guard = caster.SummonedGuard;
        guard.Should().NotBeNull();
        guard!.IsGuardSummon.Should().BeTrue();
        guard.OwnerCharId.Should().Be(caster.CharacterId);
        guard.Nation.Should().Be((EntityNation)caster.Nation);
        NpcHostility.IsAttackableBy(guard, caster).Should().BeFalse("the caster's own nation cannot strike its guard");
        caster.ActiveBuffs.Should().ContainKey(guardSummon);

        await provider.GetRequiredService<IMagicStatusEffectService>().CancelAsync(caster, guardSummon);

        caster.SummonedGuard.Should().BeNull("the guard leaves when the summon ends");
    }

    [Fact]
    public async Task InstantMagicSpendsItselfOnTheNextSpellWhichGetsNoCooldown()
    {
        const int instantMagic = 110820;
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(instantMagic).Returns(new MagicData { Id = instantMagic, Type1 = 4, Moral = 1 });
                gameData.GetMagic(FireBlast).Returns(new MagicData
                {
                    Id = FireBlast, Type1 = 3, Moral = 7, Range = 35, CastTime = 2, ReCastTime = 50,
                });
                gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data>
                {
                    [instantMagic] = new() { Id = instantMagic, BuffType = (byte)BuffType.InstantMagic, Duration = 180 },
                });
                gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
                {
                    [FireBlast] = new() { Id = FireBlast, DirectType = 1, FirstDamage = -120, Attribute = 1 },
                });
            });
        var (caster, client, _) = CreateArcher(provider);
        caster.ActiveBuffs[instantMagic] = new ActiveBuff
        {
            MagicId = instantMagic, BuffType = BuffType.InstantMagic, Duration = 180,
            ExpireTicks = DateTime.UtcNow.AddMinutes(3).Ticks,
        };
        var worm = provider.GetRequiredService<SessionManager>().Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true, NpcId = 750, Name = "Worm", ZoneId = 21, X = 104, Z = 100, SpawnX = 104,
            SpawnZ = 100, Hp = 5000, MaxHp = 5000, Ac = 5, EvadeRate = 1,
        });
        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();

        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Casting, FireBlast, caster, worm.UniqueId));
        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Effecting, FireBlast, caster, worm.UniqueId));

        worm.Hp.Should().BeLessThan(5000);
        caster.SkillCooldowns.Should().NotContainKey(FireBlast, "the spell cast under instant magic goes on no cooldown");
        caster.ActiveBuffs.Should().NotContainKey(instantMagic, "instant magic is spent on that spell");
    }

    private static MagicData Ranged(int id, int castTenths) => new()
    {
        Id = id,
        Type1 = 2,
        Moral = 7,
        Range = 40,
        CastTime = (byte)castTenths,
        ReCastTime = 30,
    };

    private static (UserSession Caster, IClient Client, List<Packet> Sent) CreateArcher(ServiceProvider provider)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessions = provider.GetRequiredService<SessionManager>();
        var caster = sessions.CreateSession(client, characterId: 710, accountId: 810);
        caster.Name = "Archer";
        caster.Class = 102;
        caster.Level = 60;
        caster.Nation = AccountNation.Karus;
        caster.ZoneId = 21;
        caster.X = 100;
        caster.Z = 100;
        caster.Hp = 500;
        caster.MaxHp = 500;
        caster.Mp = 500;
        caster.MaxMp = 500;
        sessions.Regions.AddToRegion(caster);
        return (caster, client, sent);
    }

    private static Packet Magic(MagicProcessOpcode sub, int skillId, UserSession caster, int targetId)
    {
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)sub);
        packet.WriteInt(skillId);
        packet.WriteInt(caster.CharacterId);
        packet.WriteInt(targetId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);
        return packet;
    }

    private static List<(MagicProcessOpcode Sub, int SkillId)> Replies(List<Packet> packets)
    {
        var list = new List<(MagicProcessOpcode, int)>();
        foreach (var p in packets)
        {
            if (p.GetOpcode() != (byte)GameOpcodes.GS_MAGIC_PROCESS) continue;
            p.ResetOffset();
            list.Add(((MagicProcessOpcode)p.ReadByte(), p.ReadInt()));
        }
        return list;
    }
}
