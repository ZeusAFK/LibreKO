using System.Text.Json;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.World;

public class BotSession : UserSession
{
    public const byte DefaultBotLevel = 83;
    public const int DefaultBotHp = 10_000;
    public const int DefaultBotMp = 10_000;
    public const int DefaultBotMoney = 100_000_000;

    public override bool IsBot => true;
    public int? BotDatabaseId { get; set; }

    public BotSession(IClient client, int characterId, int accountId)
        : base(client, characterId, accountId)
    {
    }

    public static BotSession Create(
        int botCharacterId,
        string name,
        AccountNation nation,
        byte race,
        short classCode,
        byte level,
        byte face,
        int hair,
        byte zoneId,
        float x,
        float y,
        float z,
        short direction)
    {
        var client = new BotClient
        {
            CharacterId = botCharacterId,
            AccountId = -botCharacterId,
        };

        var bot = new BotSession(client, botCharacterId, -botCharacterId)
        {
            Name = name,
            Nation = nation,
            Race = race,
            Class = classCode,
            Level = level > 0 ? level : DefaultBotLevel,
            Face = face,
            Hair = hair,
            ZoneId = zoneId,
            X = x,
            Y = y,
            Z = z,
            Direction = direction,
            Hp = DefaultBotHp,
            MaxHp = DefaultBotHp,
            Mp = DefaultBotMp,
            MaxMp = DefaultBotMp,
            Money = DefaultBotMoney,
            IsSitting = true,
        };

        return bot;
    }
}
