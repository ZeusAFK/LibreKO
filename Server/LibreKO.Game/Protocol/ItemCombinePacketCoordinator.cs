using System.Collections.Generic;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IItemCombinePacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class ItemCombinePacketCoordinator(
    SessionManager sessionManager,
    ILogger<ItemCombinePacketCoordinator> logger) : IItemCombinePacketCoordinator
{
    private const byte ItemCombineSubList = 1;
    private const byte ItemCombineSubCombine = 2;

    // recipeId -> (display name, output item id, input item ids). Static in-memory catalog.
    private static readonly (int RecipeId, string Name, int OutputItemId, int[] Inputs)[] ItemCombineCatalog =
    {
        (1, "Iron Ingot (2x Iron Ore)",          700001000, new[] { 379022000, 379022000 }),
        (2, "Steel Ingot (Iron Ingot + Coal)",   700002000, new[] { 700001000, 379023000 }),
        (3, "Lesser Mana Stone (3x Shard)",      379100000, new[] { 379090000, 379090000, 379090000 }),
        (4, "Greater HP Potion (2x HP Potion)",  379080000, new[] { 379070000, 379070000 }),
        (5, "Enchant Scroll (Dust + Essence)",   379200000, new[] { 379210000, 379220000 }),
    };

    // charId -> recipe ids this character has successfully combined (in-memory; resets on restart).
    private static readonly Dictionary<int, HashSet<int>> itemCombineCrafted = new();

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case ItemCombineSubList:
                await SendListAsync(session);
                break;
            case ItemCombineSubCombine:
                await HandleCombineAsync(session, packet);
                break;
        }
    }

    private async Task SendListAsync(UserSession session)
    {
        var recipes = new List<ItemCombinePacketWriter.Recipe>(ItemCombineCatalog.Length);
        foreach (var (recipeId, name, outputItemId, _) in ItemCombineCatalog)
            recipes.Add(new ItemCombinePacketWriter.Recipe(recipeId, name, outputItemId));

        await session.Client.SendPacket(
            ItemCombinePacketWriter.RecipeList(ItemCombineSubList, recipes));
    }

    private async Task HandleCombineAsync(UserSession session, Packet packet)
    {

        int recipeId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;
        var entry = System.Array.Find(ItemCombineCatalog, c => c.RecipeId == recipeId);

        if (entry.RecipeId == 0)
        {
            await session.Client.SendPacket(ItemCombinePacketWriter.Result(
                ItemCombineSubCombine, ItemCombinePacketWriter.Failed,
                ItemCombinePacketWriter.NoItemId));
            return;
        }

        // NOTE: input-item consumption / output grant is stubbed until wired to the inventory path.
        GetCrafted(session.CharacterId).Add(recipeId);
        logger.LogDebug("{Name} combined recipe {Recipe} -> item {Output}",
            session.Name, recipeId, entry.OutputItemId);

        await session.Client.SendPacket(ItemCombinePacketWriter.Result(
            ItemCombineSubCombine, ItemCombinePacketWriter.Succeeded, entry.OutputItemId));
    }

    private static HashSet<int> GetCrafted(int charId)
    {
        if (!itemCombineCrafted.TryGetValue(charId, out var set))
        {
            set = new HashSet<int>();
            itemCombineCrafted[charId] = set;
        }
        return set;
    }
}
