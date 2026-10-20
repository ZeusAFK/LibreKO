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

public class MagicReachTests : GameTestBase
{
    private const int FireBlast = 110518;
    private const int Heal = 111509;
    private const int Curse = 111757;
    private const int PartyHeal = 490214;
    private const int Binding = 105630;
    private const int Mace = 1111471741;
    private const short FireBlastMana = 40;
    private const short FireBlastRecast = 50;
    private const short FireBlastRange = 20;
    private const short HealAmount = 200;
    private const byte Moradon = (byte)ZoneId.Moradon;
    private const byte Ronark = (byte)ZoneId.RonarkLand;
    private const int WormHp = 5000;
    private const short PlayerHp = 1000;
    private const float Here = 300f;

    [Fact]
    public async Task ASpellReachesAMonsterWithinItsRange()
    {
        using var provider = Provider();
        var caster = Player(provider, 770, AccountNation.Karus, Moradon, Here);
        var worm = Worm(provider, Here + FireBlastRange - 5);

        await Cast(provider, caster, FireBlast, worm.UniqueId);

        worm.Hp.Should().BeLessThan(WormHp);
    }

    [Fact]
    public async Task ASpellAtAMonsterOutOfReachCostsNothing()
    {
        using var provider = Provider();
        var caster = Player(provider, 790, AccountNation.Karus, Moradon, Here);
        var worm = Worm(provider, Here + 200);

        await Cast(provider, caster, FireBlast, worm.UniqueId);

        caster.Mp.Should().Be(caster.MaxMp);
        caster.SkillCooldowns.Should().NotContainKey(FireBlast);
    }

    [Fact]
    public async Task ASpellDoesNotReachAMonsterAcrossTheZone()
    {
        using var provider = Provider();
        var caster = Player(provider, 771, AccountNation.Karus, Moradon, Here);
        var worm = Worm(provider, Here + 200);

        await Cast(provider, caster, FireBlast, worm.UniqueId);

        worm.Hp.Should().Be(WormHp);
    }

    [Theory]
    [InlineData(Moradon, true)]
    [InlineData(Ronark, false)]
    public async Task AHealReachesAFriendOnlyInTheCastersZone(byte friendZone, bool healed)
    {
        using var provider = Provider();
        var priest = Player(provider, 772, AccountNation.Karus, Moradon, Here);
        var friend = Player(provider, 773, AccountNation.Karus, friendZone, Here + 5, hp: PlayerHp / 2);

        await Cast(provider, priest, Heal, friend.CharacterId);

        (friend.Hp > PlayerHp / 2).Should().Be(healed);
    }

    [Fact]
    public async Task AHealDoesNotLandOnAnEnemy()
    {
        using var provider = Provider();
        var priest = Player(provider, 774, AccountNation.Karus, Ronark, Here);
        var enemy = Player(provider, 775, AccountNation.ElMorad, Ronark, Here + 5, hp: PlayerHp / 2);

        await Cast(provider, priest, Heal, enemy.CharacterId);

        enemy.Hp.Should().Be(PlayerHp / 2);
    }

    [Fact]
    public async Task AHealReachesAPlayerOfTheOtherNationWhereTheNationsDoNotFight()
    {
        using var provider = Provider();
        var priest = Player(provider, 791, AccountNation.Karus, Moradon, Here);
        var visitor = Player(provider, 792, AccountNation.ElMorad, Moradon, Here + 5, hp: PlayerHp / 2);

        await Cast(provider, priest, Heal, visitor.CharacterId);

        visitor.Hp.Should().BeGreaterThan(PlayerHp / 2);
    }

    [Fact]
    public async Task AHealReachesAPartyMemberInThePartyArena()
    {
        using var provider = Provider();
        var priest = Player(provider, 793, AccountNation.Karus, Moradon, Here);
        var partner = Player(provider, 794, AccountNation.Karus, Moradon, Here + 5, hp: PlayerHp / 2);
        foreach (var member in new[] { priest, partner })
        {
            member.ArenaId = ArenaZones.MoradonPartyArena;
            member.PartyIndex = 0;
        }

        await Cast(provider, priest, Heal, partner.CharacterId);

        partner.Hp.Should().BeGreaterThan(PlayerHp / 2);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task APartyHealReachesOnlyAPartyMember(bool inParty, bool healed)
    {
        using var provider = Provider();
        var priest = Player(provider, 795, AccountNation.Karus, Moradon, Here);
        var friend = Player(provider, 796, AccountNation.Karus, Moradon, Here + 5, hp: PlayerHp / 2);
        priest.PartyIndex = 0;
        friend.PartyIndex = inParty ? 0 : -1;

        await Cast(provider, priest, PartyHeal, friend.CharacterId);

        (friend.Hp > PlayerHp / 2).Should().Be(healed);
    }

    [Fact]
    public async Task AnArmedHealStrikesAnEnemy()
    {
        using var provider = Provider();
        var priest = Player(provider, 797, AccountNation.Karus, Ronark, Here);
        TakeTheClassOf(priest, Heal);
        priest.Inventory[InventoryConstants.RightHand].ItemId = Mace;
        priest.Inventory[InventoryConstants.RightHand].Count = 1;
        priest.RecalculateStats(CreateBasicCoefficient(priest.Class), provider.GetRequiredService<IGameDataService>());
        var enemy = Player(provider, 798, AccountNation.ElMorad, Ronark, Here + 5);

        await Cast(provider, priest, Heal, enemy.CharacterId);

        enemy.Hp.Should().BeLessThan(PlayerHp);
    }

    [Theory]
    [InlineData(10f, true)]
    [InlineData(200f, false)]
    public async Task ABindingReachesOnlyAMonsterWithinItsRange(float distance, bool provoked)
    {
        using var provider = Provider();
        var warrior = Player(provider, 799, AccountNation.Karus, Moradon, Here);
        var worm = Worm(provider, Here + distance);

        await Cast(provider, warrior, Binding, worm.UniqueId);

        (worm.Hp < WormHp).Should().Be(provoked);
    }

    [Theory]
    [InlineData(AccountNation.ElMorad, true)]
    [InlineData(AccountNation.Karus, false)]
    public async Task ACurseLandsOnAnEnemyButNotOnAnAlly(AccountNation victimNation, bool cursed)
    {
        using var provider = Provider();
        var priest = Player(provider, 776, AccountNation.Karus, Ronark, Here);
        var victim = Player(provider, 777, victimNation, Ronark, Here + 5);

        await Cast(provider, priest, Curse, victim.CharacterId);

        victim.ActiveBuffs.ContainsKey(Curse).Should().Be(cursed);
    }

    private static ServiceProvider Provider() => CreateProvider(_ => { }, gameData =>
    {
        gameData.GetMagic(FireBlast).Returns(new MagicData
        {
            Id = FireBlast, Type1 = (byte)MagicSkillType.OverTime, Moral = (byte)SkillMoral.Enemy, Range = FireBlastRange,
            Msp = FireBlastMana, ReCastTime = FireBlastRecast,
        });
        gameData.GetMagic(Heal).Returns(new MagicData
        {
            Id = Heal, Type1 = (byte)MagicSkillType.OverTime, Moral = (byte)SkillMoral.FriendWithMe, Range = 20,
        });
        gameData.GetMagic(PartyHeal).Returns(new MagicData
        {
            Id = PartyHeal, Type1 = (byte)MagicSkillType.OverTime, Moral = (byte)SkillMoral.Party, Range = 20,
        });
        gameData.GetMagic(Binding).Returns(new MagicData
        {
            Id = Binding, Type1 = (byte)MagicSkillType.Area, Moral = (byte)SkillMoral.Enemy, Range = 30,
        });
        gameData.MagicType7Table.Returns(new Dictionary<int, MagicType7Data>
        {
            [Binding] = new() { Id = Binding, TargetChange = (byte)MagicAreaTargetChange.Provoke, HitRate = 100, Duration = 9, Damage = 10 },
        });
        gameData.GetItemOps(Mace).Returns(
            [new ItemOpData { ItemId = Mace, TriggerType = HealStrike.ItemOpOnEquip, SkillId = HealStrike.ArmingSkill, TriggerRate = 100 }]);
        gameData.GetMagic(Curse).Returns(new MagicData
        {
            Id = Curse, Type1 = (byte)MagicSkillType.Buff, Moral = (byte)SkillMoral.Enemy, Range = 20, SuccessRate = 100,
        });
        gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
        {
            [FireBlast] = new() { Id = FireBlast, DirectType = 1, FirstDamage = -120, Attribute = 1 },
            [Heal] = new() { Id = Heal, DirectType = 1, FirstDamage = HealAmount },
            [PartyHeal] = new() { Id = PartyHeal, DirectType = 1, FirstDamage = HealAmount },
        });
        gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data>
        {
            [Curse] = new() { Id = Curse, BuffType = (byte)BuffType.Ac, Ac = -50, Duration = 30 },
        });
    });

    private static UserSession Player(
        ServiceProvider provider, int id, AccountNation nation, byte zone, float x, short hp = PlayerHp)
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
        player.MaxHp = PlayerHp;
        player.Hp = hp;
        player.Mp = player.MaxMp = 1000;
        sessions.Regions.AddToRegion(player);
        return player;
    }

    private static NpcInstance Worm(ServiceProvider provider, float x) =>
        provider.GetRequiredService<SessionManager>().Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true, NpcId = 750, Name = "Worm", ZoneId = Moradon, X = x, Z = Here, SpawnX = x,
            SpawnZ = Here, Hp = WormHp, MaxHp = WormHp, Ac = 5, EvadeRate = 1,
        });

    private static async Task Cast(ServiceProvider provider, UserSession caster, int skillId, int targetId)
    {
        TakeTheClassOf(caster, skillId);
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
