using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IPartyBbsPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class PartyBbsPacketCoordinator(SessionManager sessionManager) : IPartyBbsPacketCoordinator
{
    private const byte PartyBbsRegister = 0x01;
    private const byte PartyBbsDelete = 0x02;
    private const byte PartyBbsNeeded = 0x03;
    private const byte PartyBbsWanted = 0x04;
    private const byte PartyBbsList = 0x0B;
    private const int MaxBbsPage = 10;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 2)
            return;

        var mode = packet.ReadByte();
        if (mode != PartyBbsPacketWriter.ModeNormal && mode != PartyBbsPacketWriter.ModeRestricted)
            return;

        var subOpcode = packet.ReadByte();
        switch (subOpcode)
        {
            case PartyBbsRegister:
                await RegisterAsync(session);
                break;

            case PartyBbsDelete:
                await DeleteAsync(session);
                break;

            case PartyBbsNeeded:
                if (packet.RemainingBytes >= 2)
                    await SendNeededPageAsync(session, packet.ReadShort());
                break;

            case PartyBbsWanted:
                await UpdateWantedAsync(session, packet);
                break;
        }
    }

    private static async Task RegisterAsync(UserSession session)
    {
        if (!session.IsInParty)
            session.SeekingParty = true;

        var packet = PartyBbsPacketWriter.Result(
            PartyBbsPacketWriter.ModeNormal,
            PartyBbsRegister,
            session.IsInParty ? PartyBbsPacketWriter.Failed : PartyBbsPacketWriter.Succeeded);

        await session.Client.SendPacket(packet);
    }

    private static async Task DeleteAsync(UserSession session)
    {
        session.SeekingParty = false;
        session.PartyBbsMessage = string.Empty;
        session.WantedClass = 0;

        var packet = PartyBbsPacketWriter.Result(
            PartyBbsPacketWriter.ModeNormal, PartyBbsDelete, PartyBbsPacketWriter.Succeeded);
        await session.Client.SendPacket(packet);
    }

    private async Task SendNeededPageAsync(UserSession session, short pageIndex)
    {
        var sanitizedPageIndex = (short)Math.Max(pageIndex, (short)0);
        var entries = sessionManager
            .GetAll()
            .Where(candidate =>
                candidate.Nation == session.Nation
                && ((candidate.SeekingParty && !candidate.IsInParty)
                    || (candidate.IsPartyLeader && candidate.WantedClass > 0)))
            .Select(candidate => (
                Player: candidate,
                Type: (byte)(candidate.IsPartyLeader && candidate.WantedClass > 0 ? 3 : 2)))
            .ToList();

        var totalCount = entries.Count;
        var totalPages = (totalCount + MaxBbsPage - 1) / MaxBbsPage;
        var startIndex = sanitizedPageIndex * MaxBbsPage;
        var page = entries.Skip(startIndex).Take(MaxBbsPage).ToList();

        var boardEntries = page.Select(item => new PartyBbsPacketWriter.BoardEntry(
            item.Player.Name,
            item.Type == PartyBbsPacketWriter.EntryWantedParty
                ? item.Player.WantedClass
                : item.Player.Class,
            item.Player.Level,
            item.Type,
            item.Player.PartyBbsMessage,
            item.Player.ZoneId,
            (byte)(item.Type == PartyBbsPacketWriter.EntryWantedParty && item.Player.IsInParty
                ? sessionManager.Parties.GetParty(item.Player.PartyIndex)?.MemberCount ?? 0
                : 0),
            (byte)item.Player.Nation)).ToList();

        var packet = PartyBbsPacketWriter.Page(
            PartyBbsPacketWriter.ModeNormal, PartyBbsList,
            sanitizedPageIndex, (short)totalPages, boardEntries);
        await session.Client.SendPacket(packet);
    }

    private async Task UpdateWantedAsync(UserSession session, Packet packet)
    {
        if (!session.IsPartyLeader)
        {
            var fail = PartyBbsPacketWriter.Result(
                PartyBbsPacketWriter.ModeNormal, PartyBbsWanted, PartyBbsPacketWriter.Failed);
            await session.Client.SendPacket(fail);
            return;
        }

        if (packet.RemainingBytes < 4)
            return;

        session.WantedClass = packet.ReadShort();
        var pageIndex = packet.ReadShort();

        if (packet.RemainingBytes >= 1)
        {
            var messageLength = packet.ReadByte();
            if (messageLength > 0 && packet.RemainingBytes >= messageLength)
            {
                var bytes = new byte[messageLength];
                for (var i = 0; i < messageLength; i++)
                    bytes[i] = packet.ReadByte();
                session.PartyBbsMessage = System.Text.Encoding.ASCII.GetString(bytes);
            }
        }

        await SendNeededPageAsync(session, pageIndex);
    }
}
