using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.World;

public class SlaveMerchantBotSession : BotSession
{
    public int OwnerCharacterId { get; set; }
    public int OwnerAccountId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public int SlaveDatabaseId { get; set; }
    public System.DateTime CreatedAt { get; set; } = System.DateTime.UtcNow;
    public System.DateTime ExpiresAt { get; set; } = System.DateTime.UtcNow.AddHours(1);

    public SlaveMerchantBotSession(IClient client, int characterId, int accountId)
        : base(client, characterId, accountId)
    {
    }
}
