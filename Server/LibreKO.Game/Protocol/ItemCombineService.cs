using System.Globalization;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IItemCombineService
{
    Task HandleAsync(UserSession session, Packet packet);
}

public class ItemCombineService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IItemGrantService itemGrantService,
    IUserNotificationService userNotificationService,
    ICharacterStatePersister statePersister,
    ILogger<ItemCombineService> logger) : IItemCombineService
{
    public const int ShadowPiece = 700009000;
    public const int ShadowPieceBonus = 2_000;
    public const int MaterialSlots = 10;
    private const int ItemDigits = 9;
    private const int CountDigits = 3;
    private const int MaterialDigits = ItemDigits + CountDigits;
    private const int HeaderBytes = 4 + 4 + 1 + 1;

    private sealed record Material(byte BagSlot, int ItemId, int Count);

    private sealed record Request(int NpcUniqueId, int ShadowItem, byte ShadowSlot, IReadOnlyList<Material> Materials);

    private sealed record Recipe(ItemCombineRecipeData Data, IReadOnlyDictionary<int, int> Tally);

    private IReadOnlyDictionary<int, IReadOnlyList<Recipe>>? _recipesByNpc;

    public async Task HandleAsync(UserSession session, Packet packet)
    {
        var request = Read(packet);
        if (request == null)
        {
            await session.Client.SendPacket(ItemCombinePacketWriter.Refusal(ItemCombinePacketWriter.WrongMaterial));
            return;
        }

        var npc = sessionManager.Regions.GetNpc(request.NpcUniqueId);
        if (npc == null || !CanCombineAt(session, npc))
        {
            await session.Client.SendPacket(ItemCombinePacketWriter.Refusal(ItemCombinePacketWriter.Refused));
            return;
        }

        if (!MaterialsHeld(session, request) || Match(npc.NpcId, request.Materials) is not { } recipe
            || gameDataService.GetItem(recipe.ResultItemId) is not { } result)
        {
            await session.Client.SendPacket(ItemCombinePacketWriter.Refusal(ItemCombinePacketWriter.WrongMaterial));
            return;
        }

        var resultSlot = session.WithLock(s => s.FindSlotForItem(result.Num, gameDataService, (ushort)recipe.ResultCount));
        if (resultSlot < InventoryConstants.SlotMax)
        {
            await session.Client.SendPacket(ItemCombinePacketWriter.Refusal(ItemCombinePacketWriter.Refused));
            return;
        }

        var shadow = request.ShadowItem == ShadowPiece ? ShadowPieceBonus : 0;
        var rate = Math.Min(ItemCombineRecipeData.RateScale, recipe.SuccessRate + shadow);
        var success = Random.Shared.Next(ItemCombineRecipeData.RateScale) < rate;
        if (success && await itemGrantService.GrantAsync(session, result, recipe.ResultCount) < recipe.ResultCount)
        {
            await session.Client.SendPacket(ItemCombinePacketWriter.Refusal(ItemCombinePacketWriter.Refused));
            return;
        }

        TakeMaterials(session, request);
        await session.Client.SendPacket(success
            ? ItemCombinePacketWriter.Success(recipe.DisplayRow, result.Num, (byte)(resultSlot - InventoryConstants.SlotMax))
            : ItemCombinePacketWriter.Failure(recipe.DisplayRow));
        await sessionManager.Regions.SendToRegion(session,
            ItemCombinePacketWriter.Effect(success, (short)npc.NpcId, recipe.DisplayRow), excludeSender: false);
        await userNotificationService.SendWeightChangeAsync(session);
        await statePersister.SaveAsync(session);
        logger.LogInformation("{Name} combined recipe {Recipe} at {Npc}: {Outcome}",
            session.Name, recipe.Id, npc.NpcId, success ? "success" : "failure");
    }

    private static Request? Read(Packet packet)
    {
        if (packet.RemainingBytes < HeaderBytes)
            return null;
        var npc = packet.ReadInt();
        var shadowItem = packet.ReadInt();
        var shadowSlot = packet.ReadByte();
        int count = packet.ReadByte();
        if (count is 0 or > MaterialSlots || packet.RemainingBytes < count + 1)
            return null;

        var slots = new byte[count];
        for (var i = 0; i < count; i++)
            slots[i] = packet.ReadByte();
        var text = packet.ReadSByteString();
        if (text.Length != count * MaterialDigits)
            return null;

        var materials = new List<Material>(count);
        for (var i = 0; i < count; i++)
        {
            var at = i * MaterialDigits;
            if (!int.TryParse(text.AsSpan(at, ItemDigits), NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
                || !int.TryParse(text.AsSpan(at + ItemDigits, CountDigits), NumberStyles.None, CultureInfo.InvariantCulture, out var itemCount)
                || itemId == 0 || itemCount == 0)
                return null;
            materials.Add(new Material(slots[i], itemId, itemCount));
        }
        return new Request(npc, shadowItem, shadowSlot, materials);
    }

    private bool CanCombineAt(UserSession session, NpcInstance npc)
    {
        if (!npc.IsAlive || npc.ZoneId != session.ZoneId || session.Hp <= 0
            || session.Trade.IsTrading || session.Trade.IsMerchanting || session.IsGathering)
            return false;
        var dx = session.X - npc.X;
        var dz = session.Z - npc.Z;
        return dx * dx + dz * dz <= GameConstants.MaxNpcInteractionRangeSq && RecipesByNpc.ContainsKey(npc.NpcId);
    }

    private static bool Usable(ItemSlot slot) =>
        slot.State is not (ItemFlag.Rented or ItemFlag.CharacterSeal or ItemFlag.Duplicate or ItemFlag.Sealed or ItemFlag.Bound);

    private static bool MaterialsHeld(UserSession session, Request request)
    {
        var used = new HashSet<byte>();
        foreach (var material in request.Materials)
        {
            if (material.BagSlot >= InventoryConstants.HaveMax || !used.Add(material.BagSlot))
                return false;
            var slot = session.Inventory[InventoryConstants.SlotMax + material.BagSlot];
            if (slot.ItemId != material.ItemId || slot.Count < material.Count || !Usable(slot))
                return false;
        }

        if (request.ShadowItem == 0)
            return true;
        if (request.ShadowItem != ShadowPiece || request.ShadowSlot >= InventoryConstants.HaveMax || !used.Add(request.ShadowSlot))
            return false;
        var shadow = session.Inventory[InventoryConstants.SlotMax + request.ShadowSlot];
        return shadow.ItemId == ShadowPiece && Usable(shadow);
    }

    private ItemCombineRecipeData? Match(int npcId, IReadOnlyList<Material> materials)
    {
        if (!RecipesByNpc.TryGetValue(npcId, out var recipes))
            return null;
        var tally = Tally(materials.Select(m => (m.ItemId, m.Count)));
        return recipes.FirstOrDefault(r => r.Tally.Count == tally.Count
            && r.Tally.All(w => tally.TryGetValue(w.Key, out var have) && have == w.Value))?.Data;
    }

    private static Dictionary<int, int> Tally(IEnumerable<(int ItemId, int Count)> materials)
    {
        var tally = new Dictionary<int, int>();
        foreach (var (itemId, count) in materials)
            tally[itemId] = tally.GetValueOrDefault(itemId) + count;
        return tally;
    }

    private IReadOnlyDictionary<int, IReadOnlyList<Recipe>> RecipesByNpc =>
        _recipesByNpc ??= gameDataService.ItemCombineRecipes
            .GroupBy(r => r.NpcId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<Recipe>)g.OrderBy(r => r.Id)
                    .Select(r => new Recipe(r, Tally(gameDataService.ItemCombineMaterialsByRecipe[r.Id].Select(m => (m.ItemId, m.Count)))))
                    .ToList());

    private void TakeMaterials(UserSession session, Request request) =>
        session.WithLock(s =>
        {
            foreach (var material in request.Materials)
                Take(s.Inventory[InventoryConstants.SlotMax + material.BagSlot], material.Count);
            if (request.ShadowItem == ShadowPiece)
                Take(s.Inventory[InventoryConstants.SlotMax + request.ShadowSlot], 1);
            s.RecalculateStatsWithBuffs(gameDataService);
        });

    private static void Take(ItemSlot slot, int count)
    {
        if (slot.Count <= count)
            slot.Clear();
        else
            slot.Count -= (ushort)count;
    }
}
