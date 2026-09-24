using System.Runtime.CompilerServices;
using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class DelosObjectEventTests : GameTestBase
{
    private const short Delos = (short)ZoneId.Delos;
    private const short OuterGate = 561;
    private const short OuterGateLever = 2561;
    private const short CastleClan = 77;
    private const short GateLeverType = 3;
    private const short GateType = 1;

    private static SmdFile DelosMap([CallerFilePath] string source = "") =>
        SmdFile.Load(Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Map", "war_a.smd")))!;

    [Fact]
    public void DelosLeversAreGateLeversWiredToTheirGates()
    {
        var events = DelosMap().ObjectEvents;

        events[OuterGateLever].Type.Should().Be(GateLeverType);
        events[OuterGateLever].ControlNpcId.Should().Be(OuterGate);
        events[OuterGate].Type.Should().Be(GateType);
    }

    [Fact]
    public async Task TheCastleClanClosesTheOuterGateWithItsLever()
    {
        using var provider = Provider(castleOwner: CastleClan);
        var (session, gate) = Setup(provider);
        session.KnightsId = CastleClan;
        var sent = new List<Packet>();
        session.Client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await Pull(provider, session);

        gate.GateOpen.Should().BeFalse();
        var flag = sent.Single(IsGateFlag);
        flag.ResetOffset();
        flag.ReadByte().Should().Be((byte)GateType);
        flag.ReadByte().Should().Be(1);
        flag.ReadInt().Should().Be(gate.UniqueId);
        flag.ReadByte().Should().Be(0, "the gate is now closed");
    }

    private const int GateFlagBytes = 7;

    private static bool IsGateFlag(Packet packet)
    {
        if (packet.GetOpcode() != (byte)GameOpcodes.GS_OBJECT_EVENT) return false;
        packet.ResetOffset();
        return packet.RemainingBytes >= GateFlagBytes && packet.ReadByte() == GateType;
    }

    [Fact]
    public async Task WithoutTheCastleOnlyAGameMasterWorksTheLever()
    {
        using var provider = Provider(castleOwner: 0);
        var (session, gate) = Setup(provider);

        await Pull(provider, session);
        gate.GateOpen.Should().BeTrue("a player outside the castle clan cannot close Delos' gates");

        session.IsGM = true;
        await Pull(provider, session);
        gate.GateOpen.Should().BeFalse();
    }

    private static ServiceProvider Provider(short castleOwner) =>
        CreateProvider(_ => { }, gameData =>
            gameData.SiegeWarfare.Returns(new SiegeWarfareData { MasterKnights = castleOwner }));

    private static (UserSession Session, NpcInstance Gate) Setup(ServiceProvider provider)
    {
        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.Maps = CreateMapManagerWithObjectEvent(Delos, new ObjectEvent
        {
            Index = OuterGateLever, Type = GateLeverType, ControlNpcId = OuterGate, PosX = 488, PosZ = 744,
        });
        var gate = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = OuterGate, NpcType = NpcData.TypeGate, ZoneId = (byte)Delos, X = 506, Z = 725,
            Hp = 1, MaxHp = 1, GateOpen = true, ObjectType = NpcInstance.MapObjectType,
        });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = sessionManager.CreateSession(client, characterId: 880, accountId: 980);
        session.ZoneId = (byte)Delos;
        session.Nation = AccountNation.Karus;
        session.Hp = 100;
        session.X = 488;
        session.Z = 744;
        sessionManager.Regions.AddToRegion(session);
        return (session, gate);
    }

    private static Task Pull(ServiceProvider provider, UserSession session)
    {
        var packet = new Packet(GameOpcodes.GS_OBJECT_EVENT);
        packet.WriteShort(OuterGateLever);
        packet.WriteInt(0);
        return provider.GetRequiredService<IWorldObjectEventService>().HandleObjectEventAsync(session.Client, packet);
    }
}
