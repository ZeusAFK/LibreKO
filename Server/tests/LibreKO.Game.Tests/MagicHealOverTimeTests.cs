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

public class MagicHealOverTimeTests : GameTestBase
{
    private const int RestoreId = 111512;
    private const int MassiveRestoreId = 111539;
    private const int HealingId = 111509;
    private const int CriticalRestoreId = 112570;
    private const byte RonarkLand = BattleZoneManager.ZONE_RONARK_LAND;
    private const byte RestoreCastTime = 15;

    [Fact]
    public async Task ASecondRestoreIsRefusedWhileOneIsStillRunning()
    {
        using var provider = CreateProvider(_ => { }, Configure);
        var sessions = provider.GetRequiredService<SessionManager>();
        var (priest, client) = Player(sessions, 700, 100, 100);
        var friend = Player(sessions, 701, 105, 100).Session;

        await CastAndRelease(provider, client, RestoreId, priest, friend.CharacterId);
        friend.ActiveOverTimeEffects.Should().ContainKey(RestoreId);

        await CastAndRelease(provider, client, MassiveRestoreId, priest, friend.CharacterId);
        friend.ActiveOverTimeEffects.Should().NotContainKey(MassiveRestoreId,
            "only one heal over time runs on a player at a time");
    }

    [Fact]
    public async Task AnInstantHealStillLandsBesideARunningRestore()
    {
        using var provider = CreateProvider(_ => { }, Configure);
        var sessions = provider.GetRequiredService<SessionManager>();
        var (priest, client) = Player(sessions, 700, 100, 100);
        var friend = Player(sessions, 701, 105, 100).Session;
        friend.Hp = 1000;

        await CastAndRelease(provider, client, RestoreId, priest, friend.CharacterId);
        await CastAndRelease(provider, client, HealingId, priest, friend.CharacterId);

        friend.Hp.Should().Be(1240);
    }

    [Fact]
    public async Task ARestoreReleasedWithoutBeingCastIsRefused()
    {
        using var provider = CreateProvider(_ => { }, Configure);
        var sessions = provider.GetRequiredService<SessionManager>();
        var (priest, client) = Player(sessions, 700, 100, 100);
        var friend = Player(sessions, 701, 105, 100).Session;

        await Send(provider, client, MagicProcessOpcode.Effecting, RestoreId, priest, friend.CharacterId);

        friend.ActiveOverTimeEffects.Should().BeEmpty();
    }

    [Fact]
    public async Task APartyRestoreReachesOnlyPartyMembersWithoutOneRunning()
    {
        using var provider = CreateProvider(_ => { }, Configure);
        var sessions = provider.GetRequiredService<SessionManager>();
        var (priest, client) = Player(sessions, 700, 100, 100);
        var member = Player(sessions, 701, 105, 100).Session;
        var healedMember = Player(sessions, 702, 106, 100).Session;
        var stranger = Player(sessions, 703, 104, 100).Session;
        priest.PartyIndex = member.PartyIndex = healedMember.PartyIndex = 2;
        healedMember.ActiveOverTimeEffects[RestoreId] = new ActiveOverTimeEffect
        {
            MagicId = RestoreId, TickAmount = 20, TickLimit = 15, NextTickTicks = DateTime.UtcNow.AddSeconds(2).Ticks,
        };

        await CastAndRelease(provider, client, CriticalRestoreId, priest, -1, priest.X, priest.Z);

        priest.ActiveOverTimeEffects.Should().ContainKey(CriticalRestoreId);
        member.ActiveOverTimeEffects.Should().ContainKey(CriticalRestoreId);
        healedMember.ActiveOverTimeEffects.Should().NotContainKey(CriticalRestoreId);
        stranger.ActiveOverTimeEffects.Should().BeEmpty();
    }

    private static void Configure(IGameDataService gameData)
    {
        var magic = new Dictionary<int, MagicData>
        {
            [RestoreId] = new() { Id = RestoreId, Type1 = 3, Moral = 2, Range = 25, CastTime = RestoreCastTime },
            [MassiveRestoreId] = new() { Id = MassiveRestoreId, Type1 = 3, Moral = 2, Range = 25, CastTime = RestoreCastTime },
            [HealingId] = new() { Id = HealingId, Type1 = 3, Moral = 2, Range = 25, CastTime = RestoreCastTime },
            [CriticalRestoreId] = new() { Id = CriticalRestoreId, Type1 = 3, Moral = 6, Range = 25, CastTime = RestoreCastTime },
        };
        gameData.GetMagic(Arg.Any<int>()).Returns(call => magic.GetValueOrDefault(call.Arg<int>()));
        gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
        {
            [RestoreId] = new() { Id = RestoreId, DirectType = (byte)MagicDirectType.Health, TimeDamage = 400, Duration = 30 },
            [MassiveRestoreId] = new() { Id = MassiveRestoreId, DirectType = (byte)MagicDirectType.Health, TimeDamage = 1500, Duration = 30 },
            [HealingId] = new() { Id = HealingId, DirectType = (byte)MagicDirectType.Health, FirstDamage = 240 },
            [CriticalRestoreId] = new() { Id = CriticalRestoreId, DirectType = (byte)MagicDirectType.Health, TimeDamage = 3000, Duration = 20, Radius = 20 },
        });
    }

    private static (UserSession Session, IClient Client) Player(SessionManager sessions, int characterId, float x, float z)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = sessions.CreateSession(client, characterId, characterId + 100);
        session.Name = $"P{characterId}";
        session.Class = 111;
        session.Level = 80;
        session.Nation = AccountNation.Karus;
        session.ZoneId = RonarkLand;
        session.X = x;
        session.Z = z;
        session.Hp = 5000;
        session.MaxHp = 9000;
        session.Mp = 9000;
        session.MaxMp = 9000;
        sessions.Regions.AddToRegion(session);
        return (session, client);
    }

    private static async Task CastAndRelease(
        ServiceProvider provider, IClient client, int skillId, UserSession caster, int targetId,
        float x = 0, float z = 0)
    {
        caster.SkillBurstTicks = 0;
        await Send(provider, client, MagicProcessOpcode.Casting, skillId, caster, targetId, x, z);
        caster.CastCommitTicks = 0;
        await Send(provider, client, MagicProcessOpcode.Effecting, skillId, caster, targetId, x, z);
    }

    private static async Task Send(
        ServiceProvider provider, IClient client, MagicProcessOpcode opcode, int skillId, UserSession caster,
        int targetId, float x = 0, float z = 0)
    {
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)opcode);
        packet.WriteInt(skillId);
        packet.WriteInt(caster.CharacterId);
        packet.WriteInt(targetId);
        packet.WriteInt((int)x);
        packet.WriteInt(0);
        packet.WriteInt((int)z);
        for (var i = 0; i < 4; i++)
            packet.WriteInt(0);

        await provider.GetRequiredService<IMagicPacketCoordinator>().HandleAsync(client, packet);
    }
}
