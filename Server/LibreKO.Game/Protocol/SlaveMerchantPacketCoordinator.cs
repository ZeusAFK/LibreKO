using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public interface ISlaveMerchantPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public sealed class SlaveMerchantPacketCoordinator(
    SessionManager sessionManager,
    ISlaveMerchantService slaveMerchants) : ISlaveMerchantPacketCoordinator
{
    private const int RedeemBytes = 1 + 4;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || session.Hp <= 0 || packet.RemainingBytes < 1)
            return;

        // Collecting and redeeming move items, so they wait until the owner is free.
        switch ((SlaveMerchantSub)packet.ReadByte())
        {
            case SlaveMerchantSub.Status:
                await slaveMerchants.SendStatusAsync(session);
                break;
            case SlaveMerchantSub.Collect when !session.IsBusy:
                await slaveMerchants.CollectAsync(session);
                break;
            case SlaveMerchantSub.Redeem when packet.RemainingBytes >= RedeemBytes && !session.IsBusy:
                await slaveMerchants.RedeemAsync(session, packet.ReadByte(), packet.ReadInt());
                break;
        }
    }
}
