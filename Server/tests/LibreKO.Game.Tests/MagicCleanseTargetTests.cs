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

public class MagicCleanseTargetTests : GameTestBase
{
    private const int CureCurse = 111525;
    private const int Dispel = 490409;
    private const int DispelScroll = 900173000;
    private const ushort ThreeDispels = 3;
    private const int Shield = 190100;
    private const int Curse = 190001;
    private const byte RemoveBuffSubtype = 2;
    private const byte RemoveBlessSubtype = 5;
    private const byte Moradon = 21;
    private const byte RonarkLand = (byte)ZoneId.RonarkLand;
    private const int PartyCure = 111526;
    private const short CureMana = 10;
    private const short CureRange = 25;
    private const short PartyCureRange = 20;
    private const int NearCure = 111528;
    private const float NoRangeCureDistance = 26f;
    private const int SidelessCure = 111529;
    private const int SidelessDispel = 111530;

    [Fact]
    public async Task ACureCannotBeAimedAtAnEnemyToStripTheirBuffs()
    {
        using var provider = CreateProvider(_ => { }, Rows);
        var sessions = provider.GetRequiredService<SessionManager>();
        var caster = CreatePlayer(sessions, 950, AccountNation.Karus, RonarkLand, x: 100, z: 100);
        var enemy = CreatePlayer(sessions, 951, AccountNation.ElMorad, RonarkLand, x: 105, z: 100);
        Give(enemy, Shield, BuffType.Ac);
        Give(enemy, Curse, BuffType.Blind);

        await Cast(provider, caster, CureCurse, enemy.CharacterId);

        enemy.ActiveBuffs.Should().ContainKeys(Shield, Curse);
    }

    [Fact]
    public async Task ACureLiftsAFriendsCursesButKeepsTheirBlessings()
    {
        using var provider = CreateProvider(_ => { }, Rows);
        var sessions = provider.GetRequiredService<SessionManager>();
        var caster = CreatePlayer(sessions, 952, AccountNation.Karus, Moradon, x: 100, z: 100);
        var friend = CreatePlayer(sessions, 953, AccountNation.Karus, Moradon, x: 110, z: 100);
        Give(friend, Shield, BuffType.Ac);
        Give(friend, Curse, BuffType.Blind);

        await Cast(provider, caster, CureCurse, friend.CharacterId);

        friend.ActiveBuffs.Should().ContainKey(Shield, "a priest's blessing is not a curse");
        friend.ActiveBuffs.Should().NotContainKey(Curse);
    }

    [Fact]
    public async Task ACureDoesNotReachAFriendOutOfRangeOrInAnotherZone()
    {
        using var provider = CreateProvider(_ => { }, Rows);
        var sessions = provider.GetRequiredService<SessionManager>();
        var caster = CreatePlayer(sessions, 954, AccountNation.Karus, Moradon, x: 100, z: 100);
        var far = CreatePlayer(sessions, 955, AccountNation.Karus, Moradon, x: 400, z: 100);
        var elsewhere = CreatePlayer(sessions, 956, AccountNation.Karus, RonarkLand, x: 100, z: 100);
        Give(far, Curse, BuffType.Blind);
        Give(elsewhere, Curse, BuffType.Blind);

        await Cast(provider, caster, CureCurse, far.CharacterId);
        await Cast(provider, caster, CureCurse, elsewhere.CharacterId);

        far.ActiveBuffs.Should().ContainKey(Curse);
        elsewhere.ActiveBuffs.Should().ContainKey(Curse);
    }

    [Fact]
    public async Task ACureReachesAFriendRunningJustPastItsRange()
    {
        using var provider = CreateProvider(_ => { }, Rows);
        var sessions = provider.GetRequiredService<SessionManager>();
        var caster = CreatePlayer(sessions, 957, AccountNation.Karus, Moradon, x: 100, z: 100);
        var runner = CreatePlayer(sessions, 958, AccountNation.Karus, Moradon, x: 100 + CureRange + 5, z: 100);
        Give(runner, Curse, BuffType.Blind);

        await Cast(provider, caster, CureCurse, runner.CharacterId);

        runner.ActiveBuffs.Should().NotContainKey(Curse, "a running friend's server position trails what the priest sees");
    }

    [Fact]
    public async Task ACureWithNoRangeReachesAsFarAsItsCastDid()
    {
        using var provider = CreateProvider(_ => { }, Rows);
        var sessions = provider.GetRequiredService<SessionManager>();
        var caster = CreatePlayer(sessions, 965, AccountNation.Karus, Moradon, x: 100, z: 100);
        var friend = CreatePlayer(sessions, 966, AccountNation.Karus, Moradon, x: 100 + NoRangeCureDistance, z: 100);
        Give(friend, Curse, BuffType.Blind);

        await Cast(provider, caster, NearCure, friend.CharacterId);

        friend.ActiveBuffs.Should().NotContainKey(Curse, "the cast was allowed this far, so the cure must not refuse it after the mana is paid");
    }

    [Fact]
    public async Task ACureDoesNotReachAFriendInAnotherRoom()
    {
        using var provider = CreateProvider(_ => { }, Rows);
        var sessions = provider.GetRequiredService<SessionManager>();
        var caster = CreatePlayer(sessions, 963, AccountNation.Karus, Moradon, x: 100, z: 100);
        var elsewhere = CreatePlayer(sessions, 964, AccountNation.Karus, Moradon, x: 110, z: 100);
        elsewhere.Room = 3;
        Give(elsewhere, Curse, BuffType.Blind);

        await Cast(provider, caster, CureCurse, elsewhere.CharacterId);

        elsewhere.ActiveBuffs.Should().ContainKey(Curse);
    }

    [Fact]
    public async Task ACureAimedAtTheDeadCostsNothing()
    {
        using var provider = CreateProvider(_ => { }, Rows);
        var sessions = provider.GetRequiredService<SessionManager>();
        var caster = CreatePlayer(sessions, 959, AccountNation.Karus, Moradon, x: 100, z: 100);
        var dead = CreatePlayer(sessions, 960, AccountNation.Karus, Moradon, x: 110, z: 100);
        dead.Hp = 0;

        await Cast(provider, caster, CureCurse, dead.CharacterId);

        caster.Mp.Should().Be(caster.MaxMp, "a cure that cannot land is refused before it is paid");
    }

    [Fact]
    public async Task APartyCureReachesAMemberJustPastItsRange()
    {
        using var provider = CreateProvider(_ => { }, Rows);
        var sessions = provider.GetRequiredService<SessionManager>();
        var caster = CreatePlayer(sessions, 961, AccountNation.Karus, Moradon, x: 100, z: 100);
        var member = CreatePlayer(sessions, 962, AccountNation.Karus, Moradon, x: 100 + PartyCureRange + 2, z: 100);
        caster.PartyIndex = member.PartyIndex = 1;
        Give(member, Curse, BuffType.Blind);

        await Cast(provider, caster, PartyCure, -1);

        member.ActiveBuffs.Should().NotContainKey(Curse);
    }

    [Fact]
    public async Task ADispelStripsANearbyEnemysBlessingsOnly()
    {
        using var provider = CreateProvider(_ => { }, Rows);
        var sessions = provider.GetRequiredService<SessionManager>();
        var caster = CreatePlayer(sessions, 957, AccountNation.Karus, RonarkLand, x: 100, z: 100);
        var near = CreatePlayer(sessions, 958, AccountNation.ElMorad, RonarkLand, x: 110, z: 100);
        var far = CreatePlayer(sessions, 959, AccountNation.ElMorad, RonarkLand, x: 600, z: 100);
        var ally = CreatePlayer(sessions, 960, AccountNation.Karus, RonarkLand, x: 105, z: 100);
        HoldReagent(provider, caster, DispelScroll, count: ThreeDispels);
        foreach (var player in new[] { near, far, ally })
        {
            Give(player, Shield, BuffType.Ac);
            Give(player, Curse, BuffType.Blind);
        }

        await Cast(provider, caster, Dispel, near.CharacterId);
        await Cast(provider, caster, Dispel, far.CharacterId);
        await Cast(provider, caster, Dispel, ally.CharacterId);

        near.ActiveBuffs.Should().NotContainKey(Shield);
        near.ActiveBuffs.Should().ContainKey(Curse, "a dispel lifts blessings, not the curses on an enemy");
        far.ActiveBuffs.Should().ContainKey(Shield, "the enemy is far out of the dispel's range");
        ally.ActiveBuffs.Should().ContainKey(Shield, "a dispel is aimed at enemies only");
    }

    [Theory]
    [InlineData(SkillMoral.SelfArea)]
    [InlineData(SkillMoral.AreaAll)]
    [InlineData(SkillMoral.All)]
    public async Task ACureWhoseMoralNamesNoSideDoesNotHelpAnEnemy(SkillMoral moral)
    {
        using var provider = CreateProvider(_ => { }, gameData => Sideless(gameData, moral));
        var sessions = provider.GetRequiredService<SessionManager>();
        var caster = CreatePlayer(sessions, 967, AccountNation.Karus, RonarkLand, x: 100, z: 100);
        var enemy = CreatePlayer(sessions, 968, AccountNation.ElMorad, RonarkLand, x: 105, z: 100);
        Give(enemy, Curse, BuffType.Blind);

        await Cast(provider, caster, SidelessCure, enemy.CharacterId);

        enemy.ActiveBuffs.Should().ContainKey(Curse, "a cure lifts curses from friends only, whatever its moral");
        caster.Mp.Should().Be(caster.MaxMp, "the cast is refused before it is paid");
    }

    [Fact]
    public async Task ACureWhoseMoralNamesNoSideStillHelpsAFriend()
    {
        using var provider = CreateProvider(_ => { }, gameData => Sideless(gameData, SkillMoral.AreaAll));
        var sessions = provider.GetRequiredService<SessionManager>();
        var caster = CreatePlayer(sessions, 969, AccountNation.Karus, RonarkLand, x: 100, z: 100);
        var friend = CreatePlayer(sessions, 970, AccountNation.Karus, RonarkLand, x: 105, z: 100);
        Give(friend, Curse, BuffType.Blind);

        await Cast(provider, caster, SidelessCure, friend.CharacterId);

        friend.ActiveBuffs.Should().NotContainKey(Curse);
    }

    [Theory]
    [InlineData(SkillMoral.SelfArea)]
    [InlineData(SkillMoral.AreaAll)]
    [InlineData(SkillMoral.All)]
    public async Task ADispelWhoseMoralNamesNoSideDoesNotStripAFriend(SkillMoral moral)
    {
        using var provider = CreateProvider(_ => { }, gameData => Sideless(gameData, moral));
        var sessions = provider.GetRequiredService<SessionManager>();
        var caster = CreatePlayer(sessions, 971, AccountNation.Karus, RonarkLand, x: 100, z: 100);
        var ally = CreatePlayer(sessions, 972, AccountNation.Karus, RonarkLand, x: 105, z: 100);
        Give(ally, Shield, BuffType.Ac);

        await Cast(provider, caster, SidelessDispel, ally.CharacterId);

        ally.ActiveBuffs.Should().ContainKey(Shield, "a dispel strips enemies only, whatever its moral");
    }

    [Fact]
    public async Task ADispelWhoseMoralNamesNoSideStillStripsAnEnemy()
    {
        using var provider = CreateProvider(_ => { }, gameData => Sideless(gameData, SkillMoral.AreaAll));
        var sessions = provider.GetRequiredService<SessionManager>();
        var caster = CreatePlayer(sessions, 973, AccountNation.Karus, RonarkLand, x: 100, z: 100);
        var enemy = CreatePlayer(sessions, 974, AccountNation.ElMorad, RonarkLand, x: 105, z: 100);
        Give(enemy, Shield, BuffType.Ac);

        await Cast(provider, caster, SidelessDispel, enemy.CharacterId);

        enemy.ActiveBuffs.Should().NotContainKey(Shield);
    }

    [Fact]
    public async Task ADispelWithNoSideCastOnTheAreaSparesFriendsAndStripsEnemies()
    {
        using var provider = CreateProvider(_ => { }, gameData => Sideless(gameData, SkillMoral.SelfArea));
        var sessions = provider.GetRequiredService<SessionManager>();
        var caster = CreatePlayer(sessions, 975, AccountNation.Karus, RonarkLand, x: 100, z: 100);
        var ally = CreatePlayer(sessions, 976, AccountNation.Karus, RonarkLand, x: 105, z: 100);
        var enemy = CreatePlayer(sessions, 977, AccountNation.ElMorad, RonarkLand, x: 105, z: 105);
        Give(ally, Shield, BuffType.Ac);
        Give(enemy, Shield, BuffType.Ac);

        await Cast(provider, caster, SidelessDispel, -1);

        ally.ActiveBuffs.Should().ContainKey(Shield, "a dispel cast on the area strips enemies only, whatever its moral");
        enemy.ActiveBuffs.Should().NotContainKey(Shield);
    }

    private static void Sideless(IGameDataService gameData, SkillMoral moral)
    {
        Rows(gameData);
        gameData.GetMagic(SidelessCure).Returns(new MagicData
        {
            Id = SidelessCure, Type1 = 5, Moral = (byte)moral, Range = CureRange, Msp = CureMana,
        });
        gameData.GetMagic(SidelessDispel).Returns(new MagicData
        {
            Id = SidelessDispel, Type1 = 5, Moral = (byte)moral, Range = CureRange,
        });
    }

    private static void Rows(IGameDataService gameData)
    {
        gameData.GetMagic(CureCurse).Returns(new MagicData
        {
            Id = CureCurse, Type1 = 5, Moral = (byte)SkillMoral.FriendWithMe, Range = CureRange, Msp = CureMana,
        });
        gameData.GetMagic(PartyCure).Returns(new MagicData
        {
            Id = PartyCure, Type1 = 5, Moral = (byte)SkillMoral.PartyAll, Range = PartyCureRange,
        });
        gameData.GetMagic(NearCure).Returns(new MagicData
        {
            Id = NearCure, Type1 = 5, Moral = (byte)SkillMoral.FriendWithMe, Msp = CureMana,
        });
        gameData.GetMagic(Dispel).Returns(new MagicData
        {
            Id = Dispel, Type1 = 5, Moral = (byte)SkillMoral.Enemy, Range = 25, UseItem = DispelScroll,
        });
        gameData.GetMagic(Shield).Returns(new MagicData { Id = Shield, Type1 = 4, Moral = (byte)SkillMoral.FriendWithMe });
        gameData.MagicType5Table.Returns(new Dictionary<int, MagicType5Data>
        {
            [CureCurse] = new() { Id = CureCurse, Type = RemoveBuffSubtype },
            [PartyCure] = new() { Id = PartyCure, Type = RemoveBuffSubtype },
            [NearCure] = new() { Id = NearCure, Type = RemoveBuffSubtype },
            [Dispel] = new() { Id = Dispel, Type = RemoveBlessSubtype },
            [SidelessCure] = new() { Id = SidelessCure, Type = RemoveBuffSubtype },
            [SidelessDispel] = new() { Id = SidelessDispel, Type = RemoveBlessSubtype },
        });
        gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data>
        {
            [Shield] = new() { Id = Shield, BuffType = (byte)BuffType.Ac, Ac = 50 },
        });
    }

    private static void Give(UserSession player, int magicId, BuffType type) =>
        player.SetBuff(magicId, new ActiveBuff
        {
            MagicId = magicId, CasterId = 777, BuffType = type,
            ExpireTicks = DateTime.UtcNow.AddMinutes(1).Ticks,
        });

    private static UserSession CreatePlayer(
        SessionManager sessions, int characterId, AccountNation nation, byte zone, float x, float z)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var player = sessions.CreateSession(client, characterId, characterId + 100);
        player.Name = $"Player{characterId}";
        player.Class = 204;
        player.Level = 70;
        player.Nation = nation;
        player.ZoneId = zone;
        player.X = x;
        player.Z = z;
        player.Hp = 500;
        player.MaxHp = 500;
        player.Mp = 500;
        player.MaxMp = 500;
        sessions.Regions.AddToRegion(player);
        return player;
    }

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
