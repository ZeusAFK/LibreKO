using LibreKO.Common.Domain.Entities;

namespace LibreKO.Common.Domain.Services;

public interface IServerRepository
{
    Task<List<Server>> GetServers();

    Task UpdateOnlinePlayersAsync(int serverId, int onlinePlayers);
}
