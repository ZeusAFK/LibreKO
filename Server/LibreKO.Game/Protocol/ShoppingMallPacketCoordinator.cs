using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public interface IShoppingMallPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
    Task SendUnreadAsync(UserSession session);
}

public class ShoppingMallPacketCoordinator(
    SessionManager sessionManager,
    IShoppingMallLetterService shoppingMallLetterService,
    IShoppingMallStoreService shoppingMallStoreService) : IShoppingMallPacketCoordinator
{
    private const byte StoreOpen = 1;
    private const byte StoreClose = 2;
    private const byte StoreLetter = 6;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
            return;

        if (packet.RemainingBytes < 1)
            return;

        var subOpcode = packet.ReadByte();
        switch (subOpcode)
        {
            case StoreOpen:
                await shoppingMallStoreService.HandleOpenAsync(session);
                break;

            case StoreClose:
                await shoppingMallStoreService.HandleCloseAsync(session);
                break;

            case StoreLetter:
                await shoppingMallLetterService.HandleAsync(session, packet);
                break;
        }
    }

    public Task SendUnreadAsync(UserSession session) => shoppingMallLetterService.SendUnreadAsync(session);
}
