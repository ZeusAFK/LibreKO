using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public interface IShoppingMallLetterService
{
    Task HandleAsync(UserSession session, Packet packet);
    Task SendUnreadAsync(UserSession session);
}

public class ShoppingMallLetterService(
    IShoppingMallLetterQueryService shoppingMallLetterQueryService,
    IShoppingMallLetterMutationService shoppingMallLetterMutationService) : IShoppingMallLetterService
{
    public async Task HandleAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;

        var subOpcode = packet.ReadByte();
        switch (subOpcode)
        {
            case ShoppingMallLetterProtocol.LetterUnread:
                await shoppingMallLetterQueryService.SendUnreadAsync(session);
                break;

            case ShoppingMallLetterProtocol.LetterList:
                await shoppingMallLetterQueryService.HandleListAsync(session, history: false);
                break;

            case ShoppingMallLetterProtocol.LetterHistory:
                await shoppingMallLetterQueryService.HandleListAsync(session, history: true);
                break;

            case ShoppingMallLetterProtocol.LetterRead:
                await shoppingMallLetterQueryService.HandleReadAsync(session, packet);
                break;

            case ShoppingMallLetterProtocol.LetterSend:
                await shoppingMallLetterMutationService.HandleSendAsync(session, packet);
                break;

            case ShoppingMallLetterProtocol.LetterDelete:
                await shoppingMallLetterMutationService.HandleDeleteAsync(session, packet);
                break;

            case ShoppingMallLetterProtocol.LetterGetItem:
                await shoppingMallLetterMutationService.HandleGetItemAsync(session, packet);
                break;
        }
    }

    public Task SendUnreadAsync(UserSession session) => shoppingMallLetterQueryService.SendUnreadAsync(session);
}
