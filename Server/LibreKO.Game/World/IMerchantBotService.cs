using System.Collections.Generic;
using System.Threading.Tasks;

namespace LibreKO.Game.World;

public interface IMerchantBotService
{
    IReadOnlyCollection<BotSession> ActiveBots { get; }
    Task<BotSession?> CloneFromGmAsync(UserSession gmSession, string advertMessage);
    Task<int> SaveActiveBotsAsync(UserSession? gmSession = null);
    Task<int> LoadAllBotsAsync(UserSession? gmSession = null);
    Task ClearAllBotsAsync(UserSession? gmSession = null);
}
