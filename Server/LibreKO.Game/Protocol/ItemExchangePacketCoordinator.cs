using System.Collections.Generic;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IItemExchangePacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class ItemExchangePacketCoordinator(
    SessionManager sessionManager,
    ILogger<ItemExchangePacketCoordinator> logger) : IItemExchangePacketCoordinator
{

    // recipeId -> (display name, input item id, input count, output item id).
    private static readonly (int RecipeId, string Name, int InputItemId, int InputCount, int OutputItemId)[] Catalog =
    {
        (1, "Iron Ore -> Iron Ingot",       700004000, 5, 700004100),
        (2, "Copper Ore -> Copper Ingot",   700004001, 5, 700004101),
        (3, "Silver Ore -> Silver Ingot",   700004002, 5, 700004102),
        (4, "Gold Ore -> Gold Ingot",       700004003, 5, 700004103),
        (5, "Gem Shards -> Flawless Gem",   700005000, 10, 700005100),
        (6, "Animal Hides -> Cured Leather",700006000, 8, 700006100),
    };

    // charId -> number of successful exchanges (in-memory; resets on server restart). Kept for parity with
    // the per-character static state convention; could later gate daily exchange limits.
    private static readonly Dictionary<int, int> exchangeCount = new();

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = (ItemExchangeSubOpcode)packet.ReadByte();
        switch (sub)
        {
            case ItemExchangeSubOpcode.List:
                await SendListAsync(session);
                break;
            case ItemExchangeSubOpcode.Exchange:
                await HandleExchangeAsync(session, packet);
                break;
        }
    }

    private async Task SendListAsync(UserSession session)
    {
        var recipes = Catalog
            .Select(entry => new ItemExchangePacketWriter.Recipe(
                entry.RecipeId, entry.Name, entry.InputItemId, entry.InputCount, entry.OutputItemId))
            .ToList();

        await session.Client.SendPacket(ItemExchangePacketWriter.RecipeList(ItemExchangeSubOpcode.List, recipes));
    }

    private async Task HandleExchangeAsync(UserSession session, Packet packet)
    {
        int recipeId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;
        var entry = System.Array.Find(Catalog, c => c.RecipeId == recipeId);


        if (entry.RecipeId == 0)
        {
            await session.Client.SendPacket(ItemExchangePacketWriter.Result(
                ItemExchangeSubOpcode.Exchange, ItemExchangePacketWriter.Failed, ItemExchangePacketWriter.NoItemId));
            return;
        }

        // NOTE: input-item consumption + reward granting are stubbed (legacy inventory path not wired here).
        // The server confirms the exchange and tells the client which reward item it gets so it can apply it
        // optimistically, consistent with the existing vendor/loot flows.
        exchangeCount[session.CharacterId] = GetExchangeCount(session.CharacterId) + 1;
        logger.LogDebug("{Name} exchanged recipe {Recipe} -> item {Out}", session.Name, recipeId, entry.OutputItemId);

        await session.Client.SendPacket(ItemExchangePacketWriter.Result(
            ItemExchangeSubOpcode.Exchange, ItemExchangePacketWriter.Succeeded, entry.OutputItemId));
    }

    private static int GetExchangeCount(int charId)
        => exchangeCount.TryGetValue(charId, out var n) ? n : 0;
}
