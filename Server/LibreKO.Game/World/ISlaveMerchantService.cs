using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;

namespace LibreKO.Game.World;

public interface ISlaveMerchantService
{
    Task<bool> DeployAsync(UserSession owner, string advert);
    Task<bool> SellAsync(SlaveMerchantBotSession bot, MerchantPurchase purchase, Func<Task> announce);
    Task SendStatusAsync(UserSession owner);
    Task CollectAsync(UserSession owner);
    Task RedeemAsync(UserSession owner, byte bagPosition, int itemId);
    Task<int?> GiveHoursAsync(string characterName, int hours);
    Task ReportAwayAsync(UserSession owner);
    Task<bool> DeleteOwnerAsync(int characterId, AppDbContext unitOfWork, Func<Task> commit);
    Task TickAsync(DateTime utcNow);
    Task<int> LoadActiveAsync();
}
