using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class MessengerTests : GameTestBase
{
    private const byte Send = 2;
    private const byte Offline = 0;
    private const byte Delivered = 1;
    private const int Senders = 32;

    [Fact]
    public async Task MessagesSentAtOnceFromManyPlayersAreEachAnswered()
    {
        using var provider = CreateProvider(_ => { });
        var sessions = provider.GetRequiredService<SessionManager>();
        Player(sessions, 1, "Reader");
        var senders = Enumerable.Range(2, Senders).Select(id => Player(sessions, id, $"Writer{id}")).ToList();

        await Task.WhenAll(senders.Select(sender => Task.Run(() =>
            provider.GetRequiredService<IMessengerPacketCoordinator>().HandleAsync(sender.Session.Client, Message("Reader")))));

        senders.Should().OnlyContain(sender => Result(sender.Sent) == Delivered);
    }

    [Fact]
    public async Task AMessageToSomeoneOfflineSaysSo()
    {
        using var provider = CreateProvider(_ => { });
        var sender = Player(provider.GetRequiredService<SessionManager>(), 1, "Writer");

        await provider.GetRequiredService<IMessengerPacketCoordinator>().HandleAsync(sender.Session.Client, Message("Nobody"));

        Result(sender.Sent).Should().Be(Offline);
    }

    private static (UserSession Session, List<Packet> Sent) Player(SessionManager sessions, int id, string name)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => { lock (sent) sent.Add(packet); }), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var session = sessions.CreateSession(client, id, id);
        session.Name = name;
        return (session, sent);
    }

    private static Packet Message(string to)
    {
        var packet = new Packet(GameOpcodes.GS_MESSENGER);
        packet.WriteByte(Send);
        packet.WriteSByteString(to);
        packet.WriteSByteString("hello");
        packet.ResetOffset();
        return packet;
    }

    private static byte Result(List<Packet> sent)
    {
        var reply = sent.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_MESSENGER);
        reply.ResetOffset();
        reply.ReadByte();
        return reply.ReadByte();
    }
}
