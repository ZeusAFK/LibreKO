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

public class TargetStateTests : GameTestBase
{
    private const int SwiftSkill = 107725;
    private const int SlowSkill = 109756;
    private const byte Zone = 21;
    private const float FarAcrossTheZone = 1500f;

    private static (UserSession Session, IClient Client, List<Packet> Sent) Join(SessionManager sessions, int id, byte zone = Zone)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = sessions.CreateSession(client, id, accountId: id + 100);
        session.ZoneId = zone;
        session.Hp = 100;
        sessions.Regions.AddToRegion(session);
        return (session, client, sent);
    }

    private static async Task Ask(ServiceProvider provider, IClient client, int targetId)
    {
        var packet = new Packet(GameOpcodes.GS_TARGET_STATE);
        packet.WriteByte(TargetStatePacketWriter.SubEffects);
        packet.WriteInt(targetId);
        await provider.GetRequiredService<ITargetStatePacketCoordinator>().HandleAsync(client, packet);
    }

    private static List<(int Skill, ushort Seconds)> Read(Packet packet, int targetId)
    {
        packet.ResetOffset();
        packet.ReadByte().Should().Be(TargetStatePacketWriter.SubEffects);
        packet.ReadInt().Should().Be(targetId);
        var list = new List<(int, ushort)>();
        int count = packet.ReadByte();
        for (var i = 0; i < count; i++)
            list.Add((packet.ReadInt(), packet.ReadUShort()));
        packet.RemainingBytes.Should().Be(0);
        return list;
    }

    [Fact]
    public async Task AnotherPlayersBuffsComeWithTheirSecondsLeft()
    {
        using var provider = CreateProvider(_ => { });
        var sessions = provider.GetRequiredService<SessionManager>();
        var (_, viewerClient, sent) = Join(sessions, 801);
        var (target, _, _) = Join(sessions, 802);
        target.SetBuff((int)BuffType.Speed, new ActiveBuff
        {
            MagicId = SwiftSkill,
            BuffType = BuffType.Speed,
            ExpireTicks = DateTime.UtcNow.AddSeconds(30).Ticks,
        });

        await Ask(provider, viewerClient, target.CharacterId);

        var effects = Read(sent.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_TARGET_STATE), target.CharacterId);
        effects.Should().ContainSingle();
        effects[0].Skill.Should().Be(SwiftSkill);
        effects[0].Seconds.Should().BeInRange(29, 30);
    }

    [Fact]
    public async Task AMonstersDebuffsAreListed()
    {
        using var provider = CreateProvider(_ => { });
        var sessions = provider.GetRequiredService<SessionManager>();
        var (viewer, viewerClient, sent) = Join(sessions, 803);
        var npc = sessions.Regions.SpawnNpc(new NpcInstance { ZoneId = Zone, X = viewer.X, Z = viewer.Z });
        npc.Debuffs.TryApply(SlowSkill, new MagicType4Data { BuffType = (byte)BuffType.Speed, Duration = 12, Speed = 50 },
            DateTime.UtcNow.Ticks);

        await Ask(provider, viewerClient, npc.UniqueId);

        var effects = Read(sent.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_TARGET_STATE), npc.UniqueId);
        effects.Should().ContainSingle().Which.Skill.Should().Be(SlowSkill);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task APlayerOutOfSightAnswersNothing(bool stealthedEnemy)
    {
        using var provider = CreateProvider(_ => { });
        var sessions = provider.GetRequiredService<SessionManager>();
        var (viewer, viewerClient, sent) = Join(sessions, 807);
        var (target, _, _) = Join(sessions, 808);
        if (stealthedEnemy)
        {
            target.Nation = viewer.Nation == AccountNation.Karus ? AccountNation.ElMorad : AccountNation.Karus;
            target.Invisibility = InvisibilityType.DispelOnMove;
        }
        else
        {
            sessions.Regions.RemoveFromRegion(target);
            target.X = viewer.X + FarAcrossTheZone;
            sessions.Regions.AddToRegion(target);
        }

        await Ask(provider, viewerClient, target.CharacterId);

        sent.Should().NotContain(p => p.GetOpcode() == (byte)GameOpcodes.GS_TARGET_STATE, "the zone is no radar");
    }

    [Fact]
    public async Task ATargetInAnotherZoneAnswersNothing()
    {
        using var provider = CreateProvider(_ => { });
        var sessions = provider.GetRequiredService<SessionManager>();
        var (_, viewerClient, sent) = Join(sessions, 805);
        var (elsewhere, _, _) = Join(sessions, 806, zone: 1);

        await Ask(provider, viewerClient, elsewhere.CharacterId);

        sent.Should().NotContain(p => p.GetOpcode() == (byte)GameOpcodes.GS_TARGET_STATE);
    }
}
