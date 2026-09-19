using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class ClientSettingsTests : GameTestBase
{
    private const byte SubGetLanguage = 1;
    private const byte SubSetLanguage = 2;

    [Fact]
    public async Task SetLanguage_StoresItOnTheSessionAndEchoesAcceptance()
    {
        var (coordinator, session, client, sent) = CreateHarness();

        await coordinator.HandleAsync(client, Request(SubSetLanguage, (byte)GameLanguage.Spanish));

        session.Language.Should().Be(GameLanguage.Spanish);
        var reply = sent.Should().ContainSingle().Subject;
        reply.GetOpcode().Should().Be((byte)GameOpcodes.GS_CLIENT_SETTINGS);
        reply.ReadByte().Should().Be(SubSetLanguage);
        reply.ReadByte().Should().Be(1);
        reply.ReadByte().Should().Be((byte)GameLanguage.Spanish);
    }

    [Fact]
    public async Task SetLanguage_RejectsALanguageTheServerDoesNotHaveAndKeepsTheCurrentOne()
    {
        var (coordinator, session, client, sent) = CreateHarness();
        session.Language = GameLanguage.Spanish;

        await coordinator.HandleAsync(client, Request(SubSetLanguage, 200));

        session.Language.Should().Be(GameLanguage.Spanish);
        var reply = sent.Should().ContainSingle().Subject;
        reply.ReadByte().Should().Be(SubSetLanguage);
        reply.ReadByte().Should().Be(0);
        reply.ReadByte().Should().Be((byte)GameLanguage.Spanish);
    }

    [Fact]
    public async Task GetLanguage_ReportsWhatTheSessionCarries()
    {
        var (coordinator, session, client, sent) = CreateHarness();
        session.Language = GameLanguage.Spanish;

        await coordinator.HandleAsync(client, Request(SubGetLanguage));

        var reply = sent.Should().ContainSingle().Subject;
        reply.ReadByte().Should().Be(SubSetLanguage);
        reply.ReadByte().Should().Be(1);
        reply.ReadByte().Should().Be((byte)GameLanguage.Spanish);
    }

    [Fact]
    public void ApplyToAccount_CarriesTheLanguageBackSoItSurvivesARestart()
    {
        var (_, session, _, _) = CreateHarness();
        session.Language = GameLanguage.Spanish;
        var account = new Account { Login = "tester", Password = "x" };

        new UserSessionCharacterMapper().ApplyToAccount(session, account);

        account.Language.Should().Be(GameLanguage.Spanish);
    }

    private static Packet Request(params byte[] payload)
    {
        var packet = new Packet(GameOpcodes.GS_CLIENT_SETTINGS);
        foreach (var value in payload)
            packet.WriteByte(value);
        return packet;
    }

    private static (
        IClientSettingsPacketCoordinator Coordinator,
        UserSession Session,
        IClient Client,
        List<Packet> Sent) CreateHarness()
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());

        var sent = new List<Packet>();
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                sent.Add(ClonePacket(callInfo.Arg<Packet>()));
                return Task.CompletedTask;
            });

        var sessionManager = new SessionManager();
        var session = sessionManager.CreateSession(client, characterId: 1, accountId: 1);

        var coordinator = new ClientSettingsPacketCoordinator(
            sessionManager,
            Substitute.For<ICharacterStatePersister>(),
            Substitute.For<ILogger<ClientSettingsPacketCoordinator>>());

        return (coordinator, session, client, sent);
    }
}
