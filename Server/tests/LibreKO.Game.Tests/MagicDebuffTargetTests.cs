using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Tests;

public class MagicDebuffTargetTests : GameTestBase
{
    private const int TormentId = 111757;
    private const int BattleCryId = 106781;
    private const int ChillId = 110609;
    private const int ChargeId = 110703;
    private const int LightShockId = 110762;
    private const int SweepManaId = 111736;
    private const int FreezingDistanceId = 110674;
    private const int CryEchoId = 106782;
    private const int LightStaffId = 110772;
    private const int LightStaffStunId = 190772;
    private const int EnoughTries = 300;
    private const int BattleCryBuffId = 106781;
    private const byte RonarkLand = BattleZoneManager.ZONE_RONARK_LAND;
    private const short TormentAcPercent = 70;
    private const short ChillSpeed = 48;
    private const short PriestClass = 111;
    private const short MageClass = 110;

    [Fact]
    public async Task AnAreaCurseLandsOnEveryEnemyInsideTheCircleAndNobodyElse()
    {
        using var provider = CreateProvider(_ => { }, Configure);
        var sessions = provider.GetRequiredService<SessionManager>();
        var (caster, client) = Player(sessions, 700, AccountNation.Karus, 100, 100, new List<Packet>());
        var inside = Player(sessions, 701, AccountNation.ElMorad, 120, 100).Session;
        var alsoInside = Player(sessions, 702, AccountNation.ElMorad, 125, 104).Session;
        var outside = Player(sessions, 703, AccountNation.ElMorad, 150, 100).Session;
        var ally = Player(sessions, 704, AccountNation.Karus, 121, 100).Session;

        await CastAtGround(provider, client, TormentId, caster, 122, 100);

        inside.ActiveBuffs.Should().ContainKey(TormentId);
        alsoInside.ActiveBuffs.Should().ContainKey(TormentId);
        outside.ActiveBuffs.Should().NotContainKey(TormentId);
        ally.ActiveBuffs.Should().NotContainKey(TormentId);
        caster.ActiveBuffs.Should().NotContainKey(TormentId, "a curse is never turned on its own caster");
    }

    [Fact]
    public async Task AnAreaCurseLowersEachVictimsDefenceByItsPercent()
    {
        using var provider = CreateProvider(_ => { }, Configure);
        var sessions = provider.GetRequiredService<SessionManager>();
        var (caster, client) = Player(sessions, 700, AccountNation.Karus, 100, 100, new List<Packet>());
        var victim = Player(sessions, 701, AccountNation.ElMorad, 120, 100).Session;
        victim.Stamina = 100;
        victim.RecalculateStatsWithBuffs(provider.GetRequiredService<IGameDataService>());
        var bareAc = victim.Stats.TotalAc;
        bareAc.Should().BeGreaterThan(0);

        await CastAtGround(provider, client, TormentId, caster, 120, 100);

        victim.Stats.TotalAc.Should().Be((short)(bareAc * TormentAcPercent / 100));
    }

    [Fact]
    public async Task AnAreaCurseCastAtTheGroundTellsTheRegionAboutEveryVictim()
    {
        using var provider = CreateProvider(_ => { }, Configure);
        var sessions = provider.GetRequiredService<SessionManager>();
        var sent = new List<Packet>();
        var (caster, client) = Player(sessions, 700, AccountNation.Karus, 100, 100, sent);
        var victim = Player(sessions, 701, AccountNation.ElMorad, 120, 100).Session;

        await CastAtGround(provider, client, TormentId, caster, 120, 100);

        var effects = sent.Where(IsEffecting).Select(ReadTarget).ToList();
        effects.Should().Contain(victim.CharacterId);
        effects.Should().Contain(-1, "the burst on the ground is announced once");
    }

    [Fact]
    public async Task APartyBuffCastAroundTheCasterReachesEveryPartyMemberInRange()
    {
        using var provider = CreateProvider(_ => { }, Configure);
        var sessions = provider.GetRequiredService<SessionManager>();
        var (caster, client) = Player(sessions, 700, AccountNation.Karus, 100, 100, new List<Packet>());
        var member = Player(sessions, 701, AccountNation.Karus, 110, 100).Session;
        var farMember = Player(sessions, 702, AccountNation.Karus, 180, 100).Session;
        var stranger = Player(sessions, 703, AccountNation.Karus, 105, 100).Session;
        caster.PartyIndex = member.PartyIndex = farMember.PartyIndex = 3;

        await CastAtGround(provider, client, BattleCryId, caster, 100, 100);

        caster.ActiveBuffs.Should().ContainKey(BattleCryId);
        member.ActiveBuffs.Should().ContainKey(BattleCryId);
        farMember.ActiveBuffs.Should().NotContainKey(BattleCryId);
        stranger.ActiveBuffs.Should().NotContainKey(BattleCryId);
    }

    [Fact]
    public async Task AnIceSpellsSlowAlsoLandsOnAMonster()
    {
        using var provider = CreateProvider(_ => { }, Configure);
        var sessions = provider.GetRequiredService<SessionManager>();
        var (caster, client) = Player(sessions, 700, AccountNation.Karus, 100, 100, new List<Packet>());
        var monster = SpawnMonster(sessions, 110, 100);

        await Cast(provider, client, ChillId, caster, monster.UniqueId);

        monster.Debuffs.Has(BuffType.Speed2, DateTime.UtcNow.Ticks).Should().BeTrue();
        monster.Debuffs.SpeedFactor(DateTime.UtcNow.Ticks).Should().BeApproximately(ChillSpeed / 100f, 0.001f);
    }

    [Fact]
    public async Task AStunnedMonsterNeitherMovesNorAttacks()
    {
        using var provider = CreateProvider(_ => { }, Configure);
        var sessions = provider.GetRequiredService<SessionManager>();
        var (caster, client) = Player(sessions, 700, AccountNation.Karus, 100, 100, new List<Packet>());
        var monster = SpawnMonster(sessions, 101, 100);

        await Cast(provider, client, ChargeId, caster, monster.UniqueId);
        monster.Debuffs.IsStunned(DateTime.UtcNow.Ticks).Should().BeTrue();

        var hpBefore = caster.Hp;
        var x = monster.X;
        monster.State = NpcState.Fighting;
        monster.TargetUserId = caster.CharacterId;
        await provider.GetRequiredService<INpcAiBehaviorService>().ProcessNpcAsync(monster, DateTime.UtcNow.Ticks);

        caster.Hp.Should().Be(hpBefore);
        monster.X.Should().Be(x);
    }

    [Fact]
    public async Task ASecondaryCurseThatIsBlockedFailsNothing()
    {
        using var provider = CreateProvider(_ => { }, Configure);
        var sessions = provider.GetRequiredService<SessionManager>();
        var sent = new List<Packet>();
        var (caster, client) = Player(sessions, 700, AccountNation.Karus, 100, 100, sent);
        var victim = Player(sessions, 701, AccountNation.ElMorad, 110, 100).Session;
        victim.BlockCurses = true;

        await Cast(provider, client, ChillId, caster, victim.CharacterId);

        victim.ActiveBuffs.Should().NotContainKey(ChillId);
        sent.Should().NotContain(p => p.GetOpcode() == (byte)GameOpcodes.GS_MAGIC_PROCESS
            && p.ReadByte() == (byte)MagicProcessOpcode.Fail,
            "the ice damage landed, so the cast as a whole did not fail");
    }

    [Fact]
    public async Task ACurseThatDoesNotApplyToMonstersFailsInsteadOfVanishing()
    {
        using var provider = CreateProvider(_ => { }, Configure);
        var sessions = provider.GetRequiredService<SessionManager>();
        var sent = new List<Packet>();
        var (caster, client) = Player(sessions, 700, AccountNation.Karus, 100, 100, sent);
        var monster = SpawnMonster(sessions, 110, 100);

        await Cast(provider, client, LightShockId, caster, monster.UniqueId);

        sent.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_MAGIC_PROCESS
            && p.ReadByte() == (byte)MagicProcessOpcode.Fail);
    }

    [Fact]
    public async Task AManaCurseTakesManaAndLeavesHealthAlone()
    {
        using var provider = CreateProvider(_ => { }, Configure);
        var sessions = provider.GetRequiredService<SessionManager>();
        var (caster, client) = Player(sessions, 700, AccountNation.Karus, 100, 100, new List<Packet>());
        var victim = Player(sessions, 701, AccountNation.ElMorad, 110, 100).Session;
        victim.Mp = 2000;
        victim.MaxMp = 2000;
        var hp = victim.Hp;

        await Cast(provider, client, SweepManaId, caster, victim.CharacterId);

        victim.Mp.Should().Be(2000 - 960);
        victim.Hp.Should().Be(hp);
    }

    [Fact]
    public async Task AFrozenPlayerCanNeitherCastNorBeHurt()
    {
        using var provider = CreateProvider(_ => { }, Configure);
        var sessions = provider.GetRequiredService<SessionManager>();
        var (caster, client) = Player(sessions, 700, AccountNation.Karus, 100, 100, new List<Packet>());
        var victim = Player(sessions, 701, AccountNation.ElMorad, 110, 100).Session;

        await Cast(provider, client, FreezingDistanceId, caster, victim.CharacterId);

        victim.ActiveBuffs.Should().ContainKey(FreezingDistanceId);
        victim.CanUseSkills.Should().BeFalse();
        victim.BlockMagic.Should().BeTrue();
        victim.BlockPhysical.Should().BeTrue();
    }

    [Fact]
    public async Task CryEchoNeedsBattleCryToBeRunning()
    {
        using var provider = CreateProvider(_ => { }, Configure);
        var sessions = provider.GetRequiredService<SessionManager>();
        var (warrior, client) = Player(sessions, 700, AccountNation.Karus, 100, 100, new List<Packet>());
        warrior.Stats.TotalHit = 2000;
        var monster = SpawnMonster(sessions, 101, 100);

        await Cast(provider, client, CryEchoId, warrior, monster.UniqueId);
        monster.Hp.Should().Be(monster.MaxHp, "Cry Echo cannot be used without Battle Cry");

        warrior.ActiveBuffs[BattleCryBuffId] = new ActiveBuff
        {
            MagicId = BattleCryBuffId, BuffType = BuffType.BattleCry, ExpireTicks = DateTime.UtcNow.AddMinutes(1).Ticks,
        };
        await Cast(provider, client, CryEchoId, warrior, monster.UniqueId);
        monster.Hp.Should().BeLessThan(monster.MaxHp);
    }

    [Fact]
    public async Task ALightStaffHitSometimesStunsAPlayer()
    {
        using var provider = CreateProvider(_ => { }, Configure);
        var sessions = provider.GetRequiredService<SessionManager>();
        var (mage, _) = Player(sessions, 700, AccountNation.Karus, 100, 100, new List<Packet>());
        var victim = Player(sessions, 701, AccountNation.ElMorad, 102, 100).Session;
        victim.MaxHp = victim.Hp = 30000;
        var execution = provider.GetRequiredService<IMagicExecutionService>();
        var staff = provider.GetRequiredService<IGameDataService>().GetMagic(LightStaffId)!;

        for (var i = 0; i < EnoughTries && !victim.ActiveBuffs.ContainsKey(LightStaffStunId); i++)
        {
            victim.Hp = victim.MaxHp;
            await execution.ExecuteAsync(mage, staff, LightStaffId, victim.CharacterId, new int[7]);
        }

        victim.ActiveBuffs.Should().ContainKey(LightStaffStunId);
        victim.ActiveBuffs[LightStaffStunId].BuffType.Should().Be(BuffType.Stun);
    }

    [Fact]
    public async Task ALightStaffNeverStunsAMonster()
    {
        using var provider = CreateProvider(_ => { }, Configure);
        var sessions = provider.GetRequiredService<SessionManager>();
        var (mage, _) = Player(sessions, 700, AccountNation.Karus, 100, 100, new List<Packet>());
        var monster = SpawnMonster(sessions, 101, 100);
        var execution = provider.GetRequiredService<IMagicExecutionService>();
        var staff = provider.GetRequiredService<IGameDataService>().GetMagic(LightStaffId)!;

        for (var i = 0; i < EnoughTries; i++)
            await execution.ExecuteAsync(mage, staff, LightStaffId, monster.UniqueId, new int[7]);

        monster.Debuffs.IsStunned(DateTime.UtcNow.Ticks).Should().BeFalse();
    }

    [Theory]
    [InlineData(100, 0, 25)]
    [InlineData(255, 0, 40)]
    [InlineData(255, 30, 25)]
    [InlineData(255, 77, 5)]
    [InlineData(50, 0, 20)]
    public void AMageStatusChanceGrowsWithIntelligenceAndShrinksWithResistance(int intelligence, int resistance, int percent) =>
        StatusEffectChance.MageChance(intelligence, resistance).Should().Be(percent);

    [Fact]
    public async Task AMageStunLeavesTheVictimImmuneForAFewSeconds()
    {
        using var provider = CreateProvider(_ => { }, Configure);
        var sessions = provider.GetRequiredService<SessionManager>();
        var mage = Player(sessions, 700, AccountNation.Karus, 100, 100).Session;
        mage.Class = MageClass;
        mage.Intelligence = 255;
        var victim = Player(sessions, 701, AccountNation.ElMorad, 102, 100).Session;
        victim.MaxHp = 30000;
        var execution = provider.GetRequiredService<IMagicExecutionService>();
        var charge = provider.GetRequiredService<IGameDataService>().GetMagic(ChargeId)!;

        for (var i = 0; i < EnoughTries && !victim.ActiveBuffs.ContainsKey(ChargeId); i++)
        {
            victim.Hp = victim.MaxHp;
            await execution.ExecuteAsync(mage, charge, ChargeId, victim.CharacterId, new int[7]);
        }
        victim.ActiveBuffs.Should().ContainKey(ChargeId);

        victim.ActiveBuffs.Clear();
        for (var i = 0; i < EnoughTries; i++)
        {
            victim.Hp = victim.MaxHp;
            await execution.ExecuteAsync(mage, charge, ChargeId, victim.CharacterId, new int[7]);
        }
        victim.ActiveBuffs.Should().NotContainKey(ChargeId);
    }

    [Fact]
    public async Task ASlowFromACasterWhoIsNotAMageLandsOnlySometimes()
    {
        using var provider = CreateProvider(_ => { }, Configure);
        var sessions = provider.GetRequiredService<SessionManager>();
        var caster = Player(sessions, 700, AccountNation.Karus, 100, 100).Session;
        var victim = Player(sessions, 701, AccountNation.ElMorad, 102, 100).Session;
        victim.MaxHp = 30000;
        var execution = provider.GetRequiredService<IMagicExecutionService>();
        var chill = provider.GetRequiredService<IGameDataService>().GetMagic(ChillId)!;

        var landed = 0;
        for (var i = 0; i < EnoughTries; i++)
        {
            victim.Hp = victim.MaxHp;
            victim.ActiveBuffs.Clear();
            await execution.ExecuteAsync(caster, chill, ChillId, victim.CharacterId, new int[7]);
            if (victim.ActiveBuffs.ContainsKey(ChillId))
                landed++;
        }

        landed.Should().BeInRange(1, EnoughTries - 1);
    }

    [Fact]
    public void AMonsterDebuffScalesDefenceAndAttackUntilItRunsOut()
    {
        var debuffs = new NpcDebuffs();
        var now = DateTime.UtcNow.Ticks;
        debuffs.TryApply(TormentId, new MagicType4Data { BuffType = (byte)BuffType.Ac, AcPct = 70, Duration = 90 }, now);
        debuffs.TryApply(1, new MagicType4Data { BuffType = (byte)BuffType.Damage, Attack = 80, Duration = 90 }, now);

        debuffs.ScaleAc(1000, now).Should().Be(700);
        debuffs.ScaleAttack(1000, now).Should().Be(800);

        var later = now + 91 * TimeSpan.TicksPerSecond;
        debuffs.ScaleAc(1000, later).Should().Be(1000);
        debuffs.ScaleAttack(1000, later).Should().Be(1000);
    }

    private static void Configure(IGameDataService gameData)
    {
        var magic = new Dictionary<int, MagicData>
        {
            [TormentId] = new() { Id = TormentId, Type1 = 4, Moral = 10, Range = 25, SuccessRate = 100 },
            [BattleCryId] = new() { Id = BattleCryId, Type1 = 4, Moral = 6, SuccessRate = 100 },
            [ChillId] = new() { Id = ChillId, Type1 = 3, Type2 = 4, Moral = 7, Range = 25, SuccessRate = 100 },
            [ChargeId] = new() { Id = ChargeId, Type1 = 3, Type2 = 4, Moral = 7, Range = 5, SuccessRate = 100 },
            [LightShockId] = new() { Id = LightShockId, Type1 = 4, Moral = 10, Range = 25, SuccessRate = 100 },
            [SweepManaId] = new() { Id = SweepManaId, Type1 = 3, Moral = 7, Range = 25, SuccessRate = 100 },
            [FreezingDistanceId] = new() { Id = FreezingDistanceId, Type1 = 3, Type2 = 4, Moral = 7, Range = 25, SuccessRate = 100 },
            [CryEchoId] = new() { Id = CryEchoId, Type1 = 1, Moral = 7, SuccessRate = 100 },
            [LightStaffId] = new() { Id = LightStaffId, Type1 = 1, Type2 = 3, Moral = 7, Range = 5, SuccessRate = 100 },
            [LightStaffStunId] = new() { Id = LightStaffStunId, Type1 = 4, Moral = 7, Range = 5, SuccessRate = 100 },
        };
        gameData.MagicType1Table.Returns(new Dictionary<int, MagicType1Data>
        {
            [LightStaffId] = new() { Id = LightStaffId, HitType = 0, HitRate = 100, Hit = 100 },
            [CryEchoId] = new()
            {
                Id = CryEchoId, HitType = 1, HitRate = 101, Hit = 300, AddDamage = 100,
                RequiredBuffType = (byte)BuffType.BattleCry, RequiredBuffSkill = BattleCryBuffId,
            },
        });
        gameData.GetMagic(Arg.Any<int>()).Returns(call => magic.GetValueOrDefault(call.Arg<int>()));
        gameData.GetCoefficient(Arg.Any<short>()).Returns(CreateBasicCoefficient(PriestClass));

        gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
        {
            [ChillId] = new() { Id = ChillId, DirectType = (byte)MagicDirectType.Health, TimeDamage = -196, Duration = 20, Attribute = 2 },
            [ChargeId] = new() { Id = ChargeId, DirectType = (byte)MagicDirectType.Health, FirstDamage = -118, Attribute = 3 },
            [SweepManaId] = new() { Id = SweepManaId, DirectType = (byte)MagicDirectType.Mana, FirstDamage = -960 },
            [FreezingDistanceId] = new() { Id = FreezingDistanceId, DirectType = (byte)MagicDirectType.Health, Duration = 2 },
            [LightStaffId] = new() { Id = LightStaffId, DirectType = (byte)MagicDirectType.Health, FirstDamage = -883, Attribute = 3 },
        });
        gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data>
        {
            [TormentId] = new() { Id = TormentId, BuffType = (byte)BuffType.Ac, Radius = 10, Duration = 90, AcPct = TormentAcPercent, Speed = 100 },
            [BattleCryId] = new() { Id = BattleCryId, BuffType = (byte)BuffType.Stats, Radius = 30, Duration = 60, Sta = 15, Speed = 100 },
            [ChillId] = new() { Id = ChillId, BuffType = (byte)BuffType.Speed2, Duration = 11, Speed = ChillSpeed },
            [ChargeId] = new() { Id = ChargeId, BuffType = (byte)BuffType.Stun, Duration = 3, Speed = 100 },
            [LightShockId] = new() { Id = LightShockId, BuffType = (byte)BuffType.DisableTargeting, Radius = 10, Duration = 5, Speed = 100 },
            [FreezingDistanceId] = new() { Id = FreezingDistanceId, BuffType = (byte)BuffType.Freeze, Duration = 15, Speed = 1 },
            [LightStaffStunId] = new() { Id = LightStaffStunId, BuffType = (byte)BuffType.Stun, Duration = 3, Speed = 100 },
        });
    }

    private static NpcInstance SpawnMonster(SessionManager sessions, float x, float z) =>
        sessions.Regions.SpawnNpc(new NpcInstance
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
            Hp = 100000,
            MaxHp = 100000,
            Attack1 = 500,
            AttackRange = 3,
            SearchRange = 8,
            TracingRange = 20,
            Speed1 = 2,
            Speed2 = 7,
        });

    private static (UserSession Session, IClient Client) Player(
        SessionManager sessions, int characterId, AccountNation nation, float x, float z, List<Packet>? sent = null)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        if (sent != null)
            client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        else
            client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = sessions.CreateSession(client, characterId, characterId + 100);
        session.Name = $"P{characterId}";
        session.Class = PriestClass;
        session.Level = 80;
        session.Nation = nation;
        session.ZoneId = RonarkLand;
        session.X = x;
        session.Z = z;
        session.Hp = 5000;
        session.MaxHp = 5000;
        session.Mp = 5000;
        session.MaxMp = 5000;
        session.Stats.TotalAc = 1000;
        sessions.Regions.AddToRegion(session);
        return (session, client);
    }

    private static bool IsEffecting(Packet packet)
    {
        if (packet.GetOpcode() != (byte)GameOpcodes.GS_MAGIC_PROCESS)
            return false;
        return packet.ReadByte() == (byte)MagicProcessOpcode.Effecting;
    }

    private static int ReadTarget(Packet packet)
    {
        packet.ReadInt();
        packet.ReadInt();
        return packet.ReadInt();
    }

    private static Task Cast(ServiceProvider provider, IClient client, int skillId, UserSession caster, int targetId) =>
        Send(provider, client, skillId, caster, targetId, 0, 0);

    private static Task CastAtGround(
        ServiceProvider provider, IClient client, int skillId, UserSession caster, int x, int z) =>
        Send(provider, client, skillId, caster, -1, x, z);

    private static async Task Send(
        ServiceProvider provider, IClient client, int skillId, UserSession caster, int targetId, int x, int z)
    {
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Effecting);
        packet.WriteInt(skillId);
        packet.WriteInt(caster.CharacterId);
        packet.WriteInt(targetId);
        packet.WriteInt(x);
        packet.WriteInt(0);
        packet.WriteInt(z);
        for (var i = 0; i < 4; i++)
            packet.WriteInt(0);

        await provider.GetRequiredService<IMagicPacketCoordinator>().HandleAsync(client, packet);
    }
}
