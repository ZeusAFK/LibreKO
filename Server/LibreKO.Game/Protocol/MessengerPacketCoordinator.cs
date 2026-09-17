using System.Collections.Generic;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IMessengerPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class MessengerPacketCoordinator(
    SessionManager sessionManager,
    ILogger<MessengerPacketCoordinator> logger) : IMessengerPacketCoordinator
{
    private const byte MessengerSubList = 1;
    private const byte MessengerSubSend = 2;

    // Send result codes (sub 2 reply byte).
    private const byte MessengerResultOk = 1;
    private const byte MessengerResultOffline = 0;     // target not online / not found
    private const byte MessengerResultBad = 2;         // empty target / text

    private const int MessengerMaxLog = 30;            // recent messages kept per charId

    // charId -> recent received whispers ("fromName: text"), newest last (in-memory; resets on restart).
    private static readonly Dictionary<int, List<string>> messengerLog = new();

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case MessengerSubList:
                await SendOnlineListAsync(session);
                break;
            case MessengerSubSend:
                await HandleSendAsync(session, packet);
                break;
        }
    }

    // Sub 1 — every other online session is a reachable buddy. Record per buddy:
    // int charId, sbyteString name, byte online (always 1 here since we only list online sessions).
    private async Task SendOnlineListAsync(UserSession session)
    {
        var buddies = new List<UserSession>();
        foreach (var other in sessionManager.GetAll())
        {
            if (other.CharacterId == session.CharacterId)
                continue;
            buddies.Add(other);
        }

        var entries = buddies
            .Select(buddy => new MessengerPacketWriter.Buddy(
                buddy.CharacterId, buddy.Name, MessengerPacketWriter.StatusOnline))
            .ToList();

        await session.Client.SendPacket(
            MessengerPacketWriter.OnlineList(MessengerSubList, entries));
    }

    // Sub 2 — quick whisper [sbyteString toName, sbyteString text]. Reply: [2][byte result][sbyteString toName].
    // On success the message is delivered to the target's session (as a sub-1-style push is overkill, we just
    // log it on the receiver) and echoed nowhere else; the sender UI applies optimistically.
    private async Task HandleSendAsync(UserSession session, Packet packet)
    {
        string toName = packet.RemainingBytes > 0 ? packet.ReadSByteString().Trim() : "";
        string text = packet.RemainingBytes > 0 ? packet.ReadSByteString() : "";


        if (toName.Length == 0 || text.Length == 0)
        {
            await session.Client.SendPacket(MessengerPacketWriter.WhisperResult(
                MessengerSubSend, MessengerResultBad, toName));
            return;
        }

        var target = sessionManager.GetByName(toName);
        if (target == null)
        {
            await session.Client.SendPacket(MessengerPacketWriter.WhisperResult(
                MessengerSubSend, MessengerResultOffline, toName));
            return;
        }

        AppendMessengerLog(target.CharacterId, $"{session.Name}: {text}");
        logger.LogDebug("{From} -> {To} (messenger): {Text}", session.Name, toName, text);

        await session.Client.SendPacket(MessengerPacketWriter.WhisperResult(
            MessengerSubSend, MessengerResultOk, toName));
    }

    private static void AppendMessengerLog(int charId, string line)
    {
        if (!messengerLog.TryGetValue(charId, out var log))
        {
            log = new List<string>();
            messengerLog[charId] = log;
        }
        log.Add(line);
        if (log.Count > MessengerMaxLog)
            log.RemoveRange(0, log.Count - MessengerMaxLog);
    }
}
