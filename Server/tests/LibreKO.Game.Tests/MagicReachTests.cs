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
    private const int StoneBinding = 105632;
    private const int BindingStone = 379091000;
    private const int SummonScroll = 490050;
    private const int ChamberScroll = 490088;
    private const int ScrollItem = 389050000;
    private const int ShoutRow = 492010;
    private const int ShoutItem = 900747000;
    private const ushort TwoScrolls = 2;
    private const int OddStealthRow = 490211;
    private const int CureRow = 490300;
    private const int CureItem = 389060000;
    private const int NoOne = 99_999;
    private const short StoneBindingMana = 15;
    private const int Mace = 1111471741;
    private const short FireBlastMana = 40;
    private const short FireBlastRecast = 50;
    private const int StoneSpell = 110554;
    private const int StoneOfMage = 379059000;
    private const byte StoneSpellCastTenths = 15;
    private const short StoneSpellRecast = 600;

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(int milliseconds) => Now = Now.AddMilliseconds(milliseconds);
    }
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

    [Fact]
    public async Task AnArmedHealOnAnEnemyWhoCannotBeAttackedDoesNothing()
    {
        using var provider = Provider();
        var priest = Player(provider, 800, AccountNation.Karus, Ronark, Here);
        TakeTheClassOf(priest, Heal);
        priest.Inventory[InventoryConstants.RightHand].ItemId = Mace;
        priest.Inventory[InventoryConstants.RightHand].Count = 1;
        priest.RecalculateStats(CreateBasicCoefficient(priest.Class), provider.GetRequiredService<IGameDataService>());
        var enemy = Player(provider, 801, AccountNation.ElMorad, Ronark, Here + 5, hp: PlayerHp / 2);
        enemy.BlinkUntilTicks = DateTime.UtcNow.AddMinutes(1).Ticks;

        await Cast(provider, priest, Heal, enemy.CharacterId);

        enemy.Hp.Should().Be(PlayerHp / 2, "a blinking enemy can be neither struck nor healed");
    }

    [Theory]
    [InlineData(MagicProcessOpcode.Casting)]
    [InlineData(MagicProcessOpcode.Effecting)]
    public async Task CastingStandsThePlayerUp(MagicProcessOpcode stage)
    {
        using var provider = Provider();
        var priest = Player(provider, 803, AccountNation.Karus, Moradon, Here);
        var friend = Player(provider, 804, AccountNation.Karus, Moradon, Here + 5, hp: PlayerHp / 2);
        priest.IsSitting = true;

        await Cast(provider, priest, Heal, friend.CharacterId, stage);

        priest.IsSitting.Should().BeFalse("sitting regenerates faster only while sitting");
    }

    [Fact]
    public async Task ARefusedReagentSpellCanBeCastAgainAtOnce()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var mage = Player(provider, 802, AccountNation.Karus, Moradon, Here);
        var worm = Worm(provider, Here + 200);

        await Cast(provider, mage, StoneSpell, worm.UniqueId, MagicProcessOpcode.Casting);
        clock.Advance(StoneSpellCastTenths * 100);
        await Cast(provider, mage, StoneSpell, worm.UniqueId);

        mage.SkillCooldowns.Should().NotContainKey(StoneSpell, "the client gives the timer of a refused skill back");
        mage.AcceptedCasts.Should().NotContainKey(StoneSpell);
    }

    [Theory]
    [InlineData(0, false, 0, 0)]
    [InlineData(2, true, 1, StoneBindingMana)]
    public async Task ASkillThatNeedsAReagentCastsOnlyWithItAndSpendsIt(ushort stones, bool provoked, ushort stonesLeft, short manaSpent)
    {
        using var provider = Provider();
        var warrior = Player(provider, 805, AccountNation.Karus, Moradon, Here);
        var bag = warrior.Inventory[InventoryConstants.InventoryStart];
        if (stones > 0)
        {
            bag.ItemId = BindingStone;
            bag.Count = stones;
            bag.Durability = 1;
        }
        var worm = Worm(provider, Here + 10);

        await Cast(provider, warrior, StoneBinding, worm.UniqueId);

        (worm.Hp < WormHp).Should().Be(provoked);
        bag.Count.Should().Be(stonesLeft);
        warrior.Mp.Should().Be((short)(warrior.MaxMp - manaSpent), "a skill refused for want of its reagent costs no mana");
    }

    [Theory]
    [InlineData(SummonScroll, PlayerHp, 1)]
    [InlineData(SummonScroll, 0, TwoScrolls)]
    [InlineData(ChamberScroll, PlayerHp, TwoScrolls)]
    [InlineData(ShoutRow, PlayerHp, TwoScrolls)]
    [InlineData(OddStealthRow, PlayerHp, TwoScrolls)]
    public async Task AScrollIsSpentOnlyWhenItsCastTakesEffect(int skillId, short friendHp, ushort scrollsLeft)
    {
        using var provider = Provider();
        var caster = Player(provider, 806, AccountNation.Karus, Moradon, Here);
        var friend = Player(provider, 807, AccountNation.Karus, Moradon, Here + 30, friendHp);
        var reagent = skillId == ShoutRow ? ShoutItem : ScrollItem;
        var bag = caster.Inventory[InventoryConstants.InventoryStart];
        bag.ItemId = reagent;
        bag.Count = TwoScrolls;
        bag.Durability = 1;

        await Cast(provider, caster, skillId, friend.CharacterId);

        bag.Count.Should().Be(scrollsLeft);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, TwoScrolls)]
    public async Task ACurePotionIsSpentOnlyOnSomeoneThere(bool friendThere, ushort potionsLeft)
    {
        using var provider = Provider();
        var caster = Player(provider, 808, AccountNation.Karus, Moradon, Here);
        var friend = Player(provider, 809, AccountNation.Karus, Moradon, Here + 5);
        var bag = caster.Inventory[InventoryConstants.InventoryStart];
        bag.ItemId = CureItem;
        bag.Count = TwoScrolls;
        bag.Durability = 1;

        await Cast(provider, caster, CureRow, friendThere ? friend.CharacterId : NoOne);

        bag.Count.Should().Be(potionsLeft);
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

    private static ServiceProvider Provider(Clock? clock = null) => CreateProvider(_ => { }, gameData =>
    {
        gameData.GetMagic(StoneSpell).Returns(new MagicData
        {
            Id = StoneSpell, Type1 = (byte)MagicSkillType.OverTime, Moral = (byte)SkillMoral.Enemy, Range = FireBlastRange,
            CastTime = StoneSpellCastTenths, ReCastTime = StoneSpellRecast, UseItem = StoneOfMage,
        });
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
        gameData.GetMagic(StoneBinding).Returns(new MagicData
        {
            Id = StoneBinding, Type1 = (byte)MagicSkillType.Area, Moral = (byte)SkillMoral.Enemy, Range = 30, UseItem = BindingStone,
            Msp = StoneBindingMana,
        });
        gameData.GetItem(BindingStone).Returns(new ItemData { Num = BindingStone, Countable = 1, Duration = 1 });
        foreach (var (row, item, type) in new[]
                 {
                     (SummonScroll, ScrollItem, MagicSkillType.Warp), (ChamberScroll, ScrollItem, MagicSkillType.Warp),
                     (ShoutRow, ShoutItem, MagicSkillType.None), (OddStealthRow, ScrollItem, MagicSkillType.Stealth),
                 })
        {
            gameData.GetMagic(row).Returns(new MagicData
            {
                Id = row, Type1 = (byte)type, Moral = (byte)SkillMoral.FriendWithMe, UseItem = item, ItemGroup = 9,
            });
            gameData.GetItem(item).Returns(new ItemData { Num = item, Countable = 1, Duration = 1 });
        }
        gameData.MagicType8Table.Returns(new Dictionary<int, MagicType8Data>
        {
            [SummonScroll] = new() { Id = SummonScroll, WarpType = (byte)MagicWarpType.SummonInZone },
            [ChamberScroll] = new() { Id = ChamberScroll, WarpType = (byte)MagicWarpType.ChamberScroll },
        });
        gameData.MagicType9Table.Returns(new Dictionary<int, MagicType9Data>
        {
            [OddStealthRow] = new() { Id = OddStealthRow, StateChange = (byte)MagicStealthType.Absoluteness },
        });
        gameData.GetMagic(CureRow).Returns(new MagicData
        {
            Id = CureRow, Type1 = (byte)MagicSkillType.Special, Moral = (byte)SkillMoral.FriendWithMe, Range = 20,
            UseItem = CureItem, ItemGroup = 9,
        });
        gameData.GetItem(CureItem).Returns(new ItemData { Num = CureItem, Countable = 1, Duration = 1 });
        gameData.MagicType5Table.Returns(new Dictionary<int, MagicType5Data>
        {
            [CureRow] = new() { Id = CureRow, Type = (byte)SpecialMagicType.RemoveDot },
        });
        gameData.MagicType7Table.Returns(new Dictionary<int, MagicType7Data>
        {
            [Binding] = new() { Id = Binding, TargetChange = (byte)MagicAreaTargetChange.Provoke, HitRate = 100, Duration = 9, Damage = 10 },
            [StoneBinding] = new() { Id = StoneBinding, TargetChange = (byte)MagicAreaTargetChange.Provoke, HitRate = 100, Duration = 9, Damage = 10 },
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
            [StoneSpell] = new() { Id = StoneSpell, DirectType = 1, FirstDamage = -120, Attribute = 1 },
            [Heal] = new() { Id = Heal, DirectType = 1, FirstDamage = HealAmount },
            [PartyHeal] = new() { Id = PartyHeal, DirectType = 1, FirstDamage = HealAmount },
        });
        gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data>
        {
            [Curse] = new() { Id = Curse, BuffType = (byte)BuffType.Ac, Ac = -50, Duration = 30 },
        });
    }, configureServices: services =>
    {
        if (clock != null)
            services.AddSingleton<TimeProvider>(clock);
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

    private static async Task Cast(
        ServiceProvider provider, UserSession caster, int skillId, int targetId,
        MagicProcessOpcode stage = MagicProcessOpcode.Effecting)
    {
        TakeTheClassOf(caster, skillId);
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)stage);
        packet.WriteInt(skillId);
        packet.WriteInt(caster.CharacterId);
        packet.WriteInt(targetId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);
        await provider.GetRequiredService<IMagicPacketCoordinator>().HandleAsync(caster.Client, packet);
    }
}
