using System.Collections.Generic;
using System.Threading.Tasks;

namespace LibreKO.Game.World;

public interface ISlaveMerchantService
{
    IReadOnlyCollection<SlaveMerchantBotSession> ActiveSlaves { get; }
    SlaveMerchantBotSession? GetActiveSlaveByOwner(int ownerCharacterId);
    Task<SlaveMerchantBotSession?> CreateSlaveMerchantAsync(UserSession session, string advertMessage);
    Task RecordSaleAsync(SlaveMerchantBotSession bot, byte slot, int itemId, int count, long totalCost, string buyerName);
    Task<long> ClaimEarningsAsync(UserSession session);
    Task<bool> CloseSlaveAsync(UserSession session);
    Task<string> GetStatusAsync(UserSession session);
    Task SendSlaveStatusAsync(UserSession session);
    Task<int> LoadActiveSlaveMerchantsAsync();
}
