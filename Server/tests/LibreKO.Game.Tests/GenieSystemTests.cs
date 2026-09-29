using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class GenieSystemTests
{
    [Fact]
    public void StartAnnouncesTheRemainingMinutesAndFlagsTheCharacterActive()
    {
        var (coordinator, session, client) = Create();
        session.GenieTime.Load(90 * 60);

        coordinator.StartAsync(session).GetAwaiter().GetResult();

        session.GenieActive.Should().BeTrue();
        var start = Sent(client, 0);
        start.GetOpcode().Should().Be((byte)GameOpcodes.GS_GENIE_SYSTEM);
        start.ReadByte().Should().Be(GenieSystemPacketWriter.InfoRequest);
        start.ReadByte().Should().Be(GenieSystemPacketWriter.Start);
        start.ReadUShort().Should().Be(GenieSystemPacketWriter.Acknowledged);
        start.ReadUShort().Should().Be(90);
    }

    [Fact]
    public void StartingWithNoTimeLeftStopsInsteadOfStarting()
    {
        var (coordinator, session, client) = Create();

        coordinator.StartAsync(session).GetAwaiter().GetResult();

        session.GenieActive.Should().BeFalse();
        var stop = Sent(client, 0);
        stop.ReadByte().Should().Be(GenieSystemPacketWriter.InfoRequest);
        stop.ReadByte().Should().Be(GenieSystemPacketWriter.Stop);
        stop.ReadUShort().Should().Be(GenieSystemPacketWriter.Acknowledged);
        stop.ReadUShort().Should().Be(0);
    }

    [Fact]
    public void TheActivationStateGoesOutUnderTheCharacterId()
    {
        var packet = GenieSystemPacketWriter.ActivationState(4242, active: true);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_GENIE_SYSTEM);
        packet.ReadByte().Should().Be(GenieSystemPacketWriter.InfoRequest);
        packet.ReadByte().Should().Be(GenieSystemPacketWriter.Activated);
        packet.ReadUShort().Should().Be(4242);
        packet.ReadByte().Should().Be(GenieSystemPacketWriter.Active);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void OptionsAreAlwaysAHundredBytesWhateverTheClientSaved()
    {
        var packet = GenieSystemPacketWriter.Options([1, 2, 3]);
        packet.ResetOffset();

        packet.ReadByte().Should().Be(GenieSystemPacketWriter.InfoRequest);
        packet.ReadByte().Should().Be(GenieSystemPacketWriter.LoadOptions);
        packet.RemainingBytes.Should().Be(GenieSystemPacketWriter.OptionBytes);
        packet.ReadByte().Should().Be(1);
        packet.ReadByte().Should().Be(2);
        packet.ReadByte().Should().Be(3);
        packet.ReadByte().Should().Be(0);
    }

    [Fact]
    public void SavedOptionsSurviveTheRoundTripToTheCharacter()
    {
        var (coordinator, session, _) = Create();
        var save = new Packet(GameOpcodes.GS_GENIE_SYSTEM);
        save.WriteByte(GenieSystemPacketWriter.InfoRequest);
        save.WriteByte(GenieSystemPacketWriter.SaveOptions);
        save.WriteByte(7);
        save.WriteByte(9);
        save.ResetOffset();

        coordinator.HandleAsync(session.Client, save).GetAwaiter().GetResult();

        session.GenieOptions.Length.Should().Be(GenieSystemPacketWriter.OptionBytes);
        session.GenieOptions[0].Should().Be(7);
        session.GenieOptions[1].Should().Be(9);

        var character = new Character();
        new UserSessionCharacterMapper().ApplyToCharacter(session, character);
        character.GenieOptions.Should().Equal(session.GenieOptions);
    }

    [Fact]
    public void RemainingMinutesNeverRoundDownToNothingWhileTimeIsLeft()
    {
        new Character().GenieMinutes.Should().Be(0);
        new Character { GenieRemainingSeconds = -1 * 60 }.GenieMinutes.Should().Be(0);
        new Character { GenieRemainingSeconds = 20 * 1 }.GenieMinutes.Should().Be(1);
        new Character { GenieRemainingSeconds = 120 * 60 }.GenieMinutes.Should().Be(120);
    }

    [Fact]
    public void AnActionIsRelayedToTheHandlerThePlayersOwnPacketWouldReach()
    {
        var combat = Substitute.For<ICombatPacketCoordinator>();
        var (coordinator, session, _) = Create(combat: combat);
        session.GenieTime.Load(30 * 60);
        session.GenieActive = true;

        coordinator.HandleAsync(session.Client, Action(GenieSystemPacketWriter.MainAttack))
            .GetAwaiter().GetResult();

        combat.Received(1).HandleAttackAsync(session.Client, Arg.Any<Packet>());
    }

    [Fact]
    public void WithNoTimeLeftTheActionIsRefusedAndTheGenieIsStopped()
    {
        var combat = Substitute.For<ICombatPacketCoordinator>();
        var (coordinator, session, client) = Create(combat: combat);

        coordinator.HandleAsync(session.Client, Action(GenieSystemPacketWriter.MainAttack))
            .GetAwaiter().GetResult();

        combat.DidNotReceive().HandleAttackAsync(Arg.Any<IClient>(), Arg.Any<Packet>());
        var stop = Sent(client, 0);
        stop.ReadByte().Should().Be(GenieSystemPacketWriter.InfoRequest);
        stop.ReadByte().Should().Be(GenieSystemPacketWriter.Stop);
    }

    [Fact]
    public void EachActionReachesItsOwnHandler()
    {
        var magic = Substitute.For<IMagicPacketCoordinator>();
        var world = Substitute.For<IWorldPacketCoordinator>();
        var (coordinator, session, _) = Create(magic: magic, world: world);
        session.GenieTime.Load(30 * 60);
        session.GenieActive = true;

        coordinator.HandleAsync(session.Client, Action(GenieSystemPacketWriter.Move))
            .GetAwaiter().GetResult();
        coordinator.HandleAsync(session.Client, Action(GenieSystemPacketWriter.Rotate))
            .GetAwaiter().GetResult();
        coordinator.HandleAsync(session.Client, Action(GenieSystemPacketWriter.Magic))
            .GetAwaiter().GetResult();

        world.Received(1).HandleMoveAsync(session.Client, Arg.Any<Packet>());
        world.Received(1).HandleRotateAsync(session.Client, Arg.Any<Packet>());
        magic.Received(1).HandleAsync(session.Client, Arg.Any<Packet>());
    }

    [Fact]
    public void InactiveGenieCannotRelayActionsEvenWithRemainingTime()
    {
        var combat = Substitute.For<ICombatPacketCoordinator>();
        var (coordinator, session, _) = Create(combat: combat);
        session.GenieTime.Load(30 * 60);
        coordinator.HandleAsync(session.Client, Action(GenieSystemPacketWriter.MainAttack))
            .GetAwaiter().GetResult();
        combat.DidNotReceive().HandleAttackAsync(Arg.Any<IClient>(), Arg.Any<Packet>());
        session.GenieActive.Should().BeFalse();
    }

    [Fact]
    public void RepeatedStartAcknowledgesAnAlreadyActiveSession()
    {
        var (coordinator, session, client) = Create();
        session.GenieTime.Load(30 * 60);
        session.GenieActive = true;
        coordinator.StartAsync(session).GetAwaiter().GetResult();
        var packet = Sent(client, 0);
        packet.ReadByte().Should().Be(GenieSystemPacketWriter.InfoRequest);
        packet.ReadByte().Should().Be(GenieSystemPacketWriter.Start);
        packet.ReadUShort().Should().Be(GenieSystemPacketWriter.Acknowledged);
    }

    [Fact]
    public void DeadCharacterCannotStartGenie()
    {
        var (coordinator, session, _) = Create();
        session.GenieTime.Load(30 * 60);
        session.Hp = 0;
        coordinator.StartAsync(session).GetAwaiter().GetResult();
        session.GenieActive.Should().BeFalse();
    }

    [Fact]
    public void OnlyStartingGenieResumesTheTimerAndStoppingPausesIt()
    {
        var (coordinator, session, _) = Create();
        session.GenieTime.Load(7200);
        session.GenieTime.IsRunning.Should().BeFalse();
        coordinator.StartAsync(session).GetAwaiter().GetResult();
        session.GenieTime.IsRunning.Should().BeTrue();
        coordinator.StopAsync(session).GetAwaiter().GetResult();
        session.GenieTime.IsRunning.Should().BeFalse();
        double saved = session.GenieTime.RemainingSeconds;
        session.GenieTime.AddSeconds(120);
        session.GenieTime.IsRunning.Should().BeFalse();
        session.GenieTime.RemainingSeconds.Should().Be(saved + 120);
        coordinator.StartAsync(session).GetAwaiter().GetResult();
        session.GenieTime.IsRunning.Should().BeTrue();
        session.GenieActive = false;
        session.GenieTime.IsRunning.Should().BeFalse();
    }

    private static Packet Action(byte action)
    {
        var packet = new Packet(GameOpcodes.GS_GENIE_SYSTEM);
        packet.WriteByte(GenieSystemPacketWriter.UpdateRequest);
        packet.WriteByte(action);
        packet.WriteByte(0);
        packet.ResetOffset();
        return packet;
    }

    private static Packet Sent(IClient client, int index)
    {
        var packet = client.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IClient.SendPacket))
            .Select(call => (Packet)call.GetArguments()[0]!)
            .ElementAt(index);
        packet.ResetOffset();
        return packet;
    }

    private static (GenieSystemPacketCoordinator Coordinator, UserSession Session, IClient Client) Create(
        ICombatPacketCoordinator? combat = null,
        IMagicPacketCoordinator? magic = null,
        IWorldPacketCoordinator? world = null)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sessionManager = new SessionManager();
        var session = sessionManager.CreateSession(client, characterId: 1, accountId: 1);
        session.Hp = 100;
        var coordinator = new GenieSystemPacketCoordinator(
            sessionManager,
            Substitute.For<IMagicItemUsageService>(),
            combat ?? Substitute.For<ICombatPacketCoordinator>(),
            magic ?? Substitute.For<IMagicPacketCoordinator>(),
            world ?? Substitute.For<IWorldPacketCoordinator>(),
            Substitute.For<ILogger<GenieSystemPacketCoordinator>>(),
            Substitute.For<IGenieHammerService>());
        return (coordinator, session, client);
    }
}
