using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class NoahKnightsChatTests : GameTestBase
{
    private const byte NoahChannel = (byte)ChatType.NoahKnights;

    private static (List<Packet> Sent, UserSession Session) Player(SessionManager sessions, int id, string name, int level, AccountNation nation)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sent.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var session = sessions.CreateSession(client, characterId: id, accountId: id + 1000);
        session.Name = name;
        session.ZoneId = (byte)ZoneId.Moradon;
        session.Nation = nation;
        session.Level = (byte)level;
        return (sent, session);
    }

    private static int ChatLines(List<Packet> sent) => sent.Count(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_CHAT);

    [Fact]
    public async Task ANoahKnightReachesEveryPlayerUpToLevelFiftyOfBothNations()
    {
        using var provider = CreateProvider(_ => { });
        var sessions = provider.GetRequiredService<SessionManager>();
        var (speakerLines, speaker) = Player(sessions, 400, "Rookie", 30, AccountNation.Karus);
        var (peerLines, _) = Player(sessions, 401, "Squire", NoahKnightsLevel, AccountNation.ElMorad);
        var (veteranLines, _) = Player(sessions, 402, "Veteran", NoahKnightsLevel + 1, AccountNation.Karus);

        await provider.GetRequiredService<IChatPacketCoordinator>().HandleAsync(speaker, NoahChannel, "Where is the Moradon blacksmith?");

        ChatLines(speakerLines).Should().Be(1);
        ChatLines(peerLines).Should().Be(1);
        ChatLines(veteranLines).Should().Be(0);
    }

    [Fact]
    public async Task APlayerPastLevelFiftyCannotSpeakOnIt()
    {
        using var provider = CreateProvider(_ => { });
        var sessions = provider.GetRequiredService<SessionManager>();
        var (peerLines, _) = Player(sessions, 410, "Squire", 12, AccountNation.Karus);
        var (_, veteran) = Player(sessions, 411, "Veteran", NoahKnightsLevel + 1, AccountNation.Karus);

        await provider.GetRequiredService<IChatPacketCoordinator>().HandleAsync(veteran, NoahChannel, "Hello");

        ChatLines(peerLines).Should().Be(0);
    }

    [Fact]
    public async Task TheZoneChatTheClientNeverSendsReachesNoOne()
    {
        using var provider = CreateProvider(_ => { });
        var sessions = provider.GetRequiredService<SessionManager>();
        var (_, speaker) = Player(sessions, 420, "Caller", 30, AccountNation.Karus);
        var (listenerLines, _) = Player(sessions, 421, "Listener", 30, AccountNation.Karus);

        await provider.GetRequiredService<IChatPacketCoordinator>().HandleAsync(speaker, (byte)ChatType.SeekingParty, "Anyone?");

        ChatLines(listenerLines).Should().Be(0, "type 19 would be a free zone-wide shout");
    }

    private const int NoahKnightsLevel = ChatPacketCoordinator.NoahKnightsMaxLevel;
}
