using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.DependencyInjection;

namespace LibreKO.Game.World;

public interface IRankerStatueService
{
    Task RefreshAsync(IReadOnlyDictionary<AccountNation, IReadOnlyList<int>> leaders);
}

public class RankerStatueService(IServiceScopeFactory scopeFactory, SessionManager sessionManager) : IRankerStatueService
{
    public static (AccountNation Nation, int Place) PlaceOf(int npcType) =>
        npcType >= NpcData.TypeRankerElMoradFirst
            ? (AccountNation.ElMorad, npcType - NpcData.TypeRankerElMoradFirst + 1)
            : (AccountNation.Karus, npcType - NpcData.TypeRankerKarusFirst + 1);

    public async Task RefreshAsync(IReadOnlyDictionary<AccountNation, IReadOnlyList<int>> leaders)
    {
        var statues = sessionManager.Regions.GetAllNpcs()
            .Where(npc => NpcSpawnPacketWriter.IsRankerStatue(npc.NpcType))
            .ToList();
        if (statues.Count == 0) return;

        using var scope = scopeFactory.CreateScope();
        var characters = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
        foreach (var statue in statues)
        {
            var (nation, place) = PlaceOf(statue.NpcType);
            NpcSpawnPacketWriter.StatueLook? look = null;
            if (leaders.TryGetValue(nation, out var ranked) && place <= ranked.Count)
                look = await LookOfAsync(ranked[place - 1], characters);
            if (look == statue.StatueLook) continue;

            statue.StatueLook = look;
            await sessionManager.Regions.BroadcastFromNpc(statue, NpcPacketMapper.BuildInOutPacket(statue, InOutType.Out));
            await sessionManager.Regions.BroadcastFromNpc(statue, NpcPacketMapper.BuildInOutPacket(statue, InOutType.In));
        }
    }

    private async Task<NpcSpawnPacketWriter.StatueLook?> LookOfAsync(int characterId, ICharacterRepository characters)
    {
        if (sessionManager.GetByCharacterId(characterId) is { } online)
            return LookOf(online.Name, online.Race, online.Class, online.Face, online.Hair, online.GetEquippedItem);

        if (await characters.GetById(characterId) is not { } character)
            return null;
        var inventory = new ItemSlot[InventoryConstants.InventoryTotal];
        for (var i = 0; i < inventory.Length; i++)
            inventory[i] = new ItemSlot();
        UserSessionBinaryState.LoadSlots(inventory, character.Items);
        return LookOf(character.Name, character.Race, character.Class, character.Face, character.Hair, slot => inventory[slot]);
    }

    public static NpcSpawnPacketWriter.StatueLook LookOf(
        string name, byte race, short cls, byte face, int hair, Func<int, ItemSlot> equipped) => new(
        name, race, cls, face, hair,
        equipped(InventoryConstants.Head).ItemId,
        equipped(InventoryConstants.Breast).ItemId,
        equipped(InventoryConstants.Leg).ItemId,
        equipped(InventoryConstants.Glove).ItemId,
        equipped(InventoryConstants.Foot).ItemId,
        equipped(InventoryConstants.RightHand).ItemId,
        equipped(InventoryConstants.LeftHand).ItemId);
}
