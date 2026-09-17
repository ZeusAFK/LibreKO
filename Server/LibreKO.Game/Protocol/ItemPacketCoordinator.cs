using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol;

public interface IItemPacketCoordinator
{
    Task HandleMoveAsync(IClient client, Packet packet);
    Task HandleRemoveAsync(IClient client, Packet packet);
    Task HandleRepairAsync(IClient client, Packet packet);
    Task HandleTradeAsync(IClient client, Packet packet);
    Task HandleUpgradeAsync(IClient client, Packet packet);
}

public class ItemPacketCoordinator(
    IItemInventoryService itemInventoryService,
    IItemTradeService itemTradeService,
    IItemUpgradeService itemUpgradeService) : IItemPacketCoordinator
{
    public Task HandleRemoveAsync(IClient client, Packet packet) =>
        itemInventoryService.HandleRemoveAsync(client, packet);

    public Task HandleRepairAsync(IClient client, Packet packet) =>
        itemTradeService.HandleRepairAsync(client, packet);

    public Task HandleTradeAsync(IClient client, Packet packet) =>
        itemTradeService.HandleTradeAsync(client, packet);

    public Task HandleMoveAsync(IClient client, Packet packet) =>
        itemInventoryService.HandleMoveAsync(client, packet);

    public Task HandleUpgradeAsync(IClient client, Packet packet) =>
        itemUpgradeService.HandleUpgradeAsync(client, packet);
}
