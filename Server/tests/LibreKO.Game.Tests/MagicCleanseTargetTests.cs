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
    private const int Dispel = 300133;
    private const int Shield = 190100;
    private const int Curse = 190001;
    private const byte RemoveBuffSubtype = 2;
    private const byte RemoveBlessSubtype = 5;
    private const byte Moradon = 21;
    private const byte RonarkLand = (byte)ZoneId.RonarkLand;

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
    public async Task ADispelStripsANearbyEnemysBlessingsOnly()
    {
        using var provider = CreateProvider(_ => { }, Rows);
        var sessions = provider.GetRequiredService<SessionManager>();
        var caster = CreatePlayer(sessions, 957, AccountNation.Karus, RonarkLand, x: 100, z: 100);
        var near = CreatePlayer(sessions, 958, AccountNation.ElMorad, RonarkLand, x: 110, z: 100);
        var far = CreatePlayer(sessions, 959, AccountNation.ElMorad, RonarkLand, x: 600, z: 100);
        var ally = CreatePlayer(sessions, 960, AccountNation.Karus, RonarkLand, x: 105, z: 100);
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

    private static void Rows(IGameDataService gameData)
    {
        gameData.GetMagic(CureCurse).Returns(new MagicData
        {
            Id = CureCurse, Type1 = 5, Moral = (byte)SkillMoral.FriendWithMe, Range = 25,
        });
        gameData.GetMagic(Dispel).Returns(new MagicData
        {
            Id = Dispel, Type1 = 5, Moral = (byte)SkillMoral.Enemy, Range = 25,
        });
        gameData.GetMagic(Shield).Returns(new MagicData { Id = Shield, Type1 = 4, Moral = (byte)SkillMoral.FriendWithMe });
        gameData.MagicType5Table.Returns(new Dictionary<int, MagicType5Data>
        {
            [CureCurse] = new() { Id = CureCurse, Type = RemoveBuffSubtype },
            [Dispel] = new() { Id = Dispel, Type = RemoveBlessSubtype },
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
