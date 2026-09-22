using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public interface IMailPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class MailPacketCoordinator(
    SessionManager sessionManager,
    IMailService mailService) : IMailPacketCoordinator
{
    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case MailPacketWriter.SubList:
                await mailService.SendInboxAsync(session);
                break;
            case MailPacketWriter.SubRead when packet.RemainingBytes >= 4:
                await mailService.ReadAsync(session, packet.ReadInt());
                break;
            case MailPacketWriter.SubSend:
                await HandleSendAsync(session, packet);
                break;
            case MailPacketWriter.SubDelete when packet.RemainingBytes >= 4:
                await mailService.DeleteAsync(session, packet.ReadInt());
                break;
            case MailPacketWriter.SubClaim when packet.RemainingBytes >= 4:
                await mailService.ClaimAsync(session, packet.ReadInt());
                break;
            case MailPacketWriter.SubUnread:
                await mailService.SendUnreadAsync(session);
                break;
        }
    }

    private async Task HandleSendAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 3)
            return;

        var recipient = packet.ReadSByteString();
        var subject = packet.ReadSByteString();
        var body = packet.ReadSByteString();
        if (packet.RemainingBytes < 5)
            return;

        var gold = packet.ReadInt();
        var itemCount = packet.ReadByte();
        var items = new List<MailItemPick>(itemCount);
        for (var i = 0; i < itemCount; i++)
        {
            if (packet.RemainingBytes < 3)
                return;
            items.Add(new MailItemPick(packet.ReadByte(), packet.ReadUShort()));
        }

        await mailService.SendAsync(session, recipient, subject, body, gold, items);
    }
}
