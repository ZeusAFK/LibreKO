using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol;

public interface IItemInventoryService
{
    Task HandleMoveAsync(IClient client, Packet packet);
    Task HandleRemoveAsync(IClient client, Packet packet);
}

public class ItemInventoryService(
    IItemMoveService itemMoveService,
    IItemRemoveService itemRemoveService) : IItemInventoryService
{
    public Task HandleMoveAsync(IClient client, Packet packet) =>
        itemMoveService.HandleAsync(client, packet);

    public Task HandleRemoveAsync(IClient client, Packet packet) =>
        itemRemoveService.HandleAsync(client, packet);
}
