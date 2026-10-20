using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.World;

public class SlaveMerchantBotSession : BotSession
{
    public int OwnerCharacterId { get; init; }
    public int OwnerAccountId { get; init; }
    public string OwnerName { get; init; } = string.Empty;
    public int SlaveDatabaseId { get; set; }
    public DateTime PaidUntil { get; set; }

    public SlaveMerchantBotSession(IClient client, int characterId, int accountId)
        : base(client, characterId, accountId)
    {
    }
}
