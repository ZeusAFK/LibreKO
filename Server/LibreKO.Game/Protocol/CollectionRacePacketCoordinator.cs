using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public interface ICollectionRacePacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class CollectionRacePacketCoordinator(
    SessionManager sessionManager,
    ICollectionRaceService collectionRaceService) : ICollectionRacePacketCoordinator
{
    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session != null)
        {
            await collectionRaceService.SyncPlayerAsync(session);
        }
    }
}
