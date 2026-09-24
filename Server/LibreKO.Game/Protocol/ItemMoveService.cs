using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IItemMoveService
{
    Task HandleAsync(IClient client, Packet packet);
}

public class ItemMoveService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IWorldPacketCoordinator worldPacketCoordinator,
    IItemInventoryRuleService itemInventoryRuleService,
    IItemEquipmentEffectService itemEquipmentEffectService,
    IUserNotificationService userNotificationService,
    ILogger<ItemMoveService> logger) : IItemMoveService
{
    private enum ItemMoveRequest : byte
    {
        Move = 1,
        Arrange = 2,
        ClientRefused = 3,
    }

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
            return;

        var requestType = (ItemMoveRequest)packet.ReadByte();
        if (requestType == ItemMoveRequest.ClientRefused)
        {
            logger.LogDebug("Rejected item move for {Name}: invalid request type {RequestType}", session.Name, requestType);
            await SendItemMoveResponseAsync(session, 0);
            return;
        }

        if (requestType == ItemMoveRequest.Arrange)
        {
            if (IsBusy(session))
            {
                logger.LogDebug("Refused to arrange the bag for {Name}: busy", session.Name);
                await session.Client.SendPacket(ItemMovePacketMapper.BuildArrangeRefusedResponse());
                return;
            }

            ArrangeBag(session);
            logger.LogDebug("Arranged the bag for {Name}", session.Name);
            await session.Client.SendPacket(ItemMovePacketMapper.BuildArrangedResponse(session));
            return;
        }

        if (requestType != ItemMoveRequest.Move)
        {
            logger.LogDebug("Rejected item move for {Name}: unknown request type {RequestType}", session.Name, requestType);
            await SendItemMoveResponseAsync(session, 0);
            return;
        }

        var directionByte = packet.ReadByte();
        if (!Enum.IsDefined(typeof(ItemMoveDirection), directionByte))
        {
            logger.LogDebug("Rejected item move for {Name}: invalid direction {Direction}", session.Name, directionByte);
            return;
        }

        var direction = (ItemMoveDirection)directionByte;
        var itemId = packet.ReadInt();
        var sourcePosition = packet.ReadByte();
        var destinationPosition = packet.ReadByte();
        var resolvedSourcePosition = sourcePosition;
        var resolvedDestinationPosition = destinationPosition;

        if (!TryResolveEquipmentPositions(
                itemInventoryRuleService,
                direction,
                ref resolvedSourcePosition,
                ref resolvedDestinationPosition))
        {
            logger.LogDebug(
                "Rejected item move for {Name}: could not resolve positions dir={Direction} src={Source} dst={Destination}",
                session.Name,
                direction,
                sourcePosition,
                destinationPosition);
            await SendItemMoveResponseAsync(session, 0);
            return;
        }

        if (IsBusy(session))
        {
            logger.LogDebug(
                "Rejected item move for {Name}: blocked state trading={Trading} merchanting={Merchanting} gathering={Gathering}",
                session.Name,
                session.Trade.IsTrading,
                session.Trade.IsMerchanting,
                session.IsGathering);
            await SendItemMoveResponseAsync(session, 0);
            return;
        }

        var itemData = gameDataService.GetItem(itemId);
        if (itemData == null)
        {
            logger.LogDebug("Rejected item move for {Name}: item {ItemId} not found", session.Name, itemId);
            await SendItemMoveResponseAsync(session, 0);
            return;
        }

        if (direction is ItemMoveDirection.InventoryToSlot or ItemMoveDirection.SlotToSlot
            && EquipRequirements.Check(session, itemData) is var refusal and not EquipRefusal.None)
        {
            logger.LogDebug("Rejected equip for {Name}: item {ItemId} refused ({Refusal})", session.Name, itemId, refusal);
            await SendItemMoveResponseAsync(session, 0);
            return;
        }

        if (!itemInventoryRuleService.TryResolveMoveIndices(
                session,
                itemData,
                direction,
                resolvedSourcePosition,
                resolvedDestinationPosition,
                out var sourceIndex,
                out var destinationIndex))
        {
            logger.LogDebug(
                "Rejected item move for {Name}: move rule failure dir={Direction} item={ItemId} src={Source}->{ResolvedSource} dst={Destination}->{ResolvedDestination}",
                session.Name,
                direction,
                itemId,
                sourcePosition,
                resolvedSourcePosition,
                destinationPosition,
                resolvedDestinationPosition);
            await SendItemMoveResponseAsync(session, 0);
            return;
        }

        var sourceItem = session.Inventory[sourceIndex];
        var destinationItem = session.Inventory[destinationIndex];
        if (sourceItem.ItemId != itemId)
        {
            logger.LogDebug(
                "Rejected item move for {Name}: source slot {SourceIndex} contains {SourceItemId} instead of {ItemId}",
                session.Name,
                sourceIndex,
                sourceItem.ItemId,
                itemId);
            await SendItemMoveResponseAsync(session, 0);
            return;
        }

        var sourceItemIdBeforeMove = sourceItem.ItemId;
        var destinationItemIdBeforeMove = destinationItem.ItemId;

        if (!destinationItem.IsEmpty)
            SwapItems(sourceItem, destinationItem);
        else
            MoveItem(sourceItem, destinationItem);

        await itemEquipmentEffectService.ApplyMoveEffectsAsync(
            session,
            direction,
            sourceItemIdBeforeMove,
            destinationItemIdBeforeMove);

        await SendItemMoveResponseAsync(session, 1);
        await userNotificationService.SendWeightChangeAsync(session);

        if (!itemInventoryRuleService.IsEquipmentChange(direction))
            return;

        switch (direction)
        {
            case ItemMoveDirection.InventoryToSlot:
                    await worldPacketCoordinator.BroadcastUserLookChangeAsync(
                        session,
                        resolvedDestinationPosition,
                        destinationItem.ItemId,
                        destinationItem.Durability);
                break;

            case ItemMoveDirection.SlotToInventory:
                await worldPacketCoordinator.BroadcastUserLookChangeAsync(session, resolvedSourcePosition, 0, 0);
                break;

            case ItemMoveDirection.SlotToSlot:
                await worldPacketCoordinator.BroadcastUserLookChangeAsync(
                    session,
                    resolvedSourcePosition,
                    sourceItem.ItemId,
                    sourceItem.Durability);
                await worldPacketCoordinator.BroadcastUserLookChangeAsync(
                    session,
                    resolvedDestinationPosition,
                    destinationItem.ItemId,
                    destinationItem.Durability);
                break;

            case ItemMoveDirection.InventoryToCospre:
                if (itemInventoryRuleService.TryGetCospreVisualSlot(destinationPosition, out var equippedLookSlot))
                {
                    await worldPacketCoordinator.BroadcastUserLookChangeAsync(
                        session,
                        equippedLookSlot,
                        destinationItem.ItemId,
                        destinationItem.Durability);
                }

                break;

            case ItemMoveDirection.CospreToInventory:
                if (itemInventoryRuleService.TryGetCospreVisualSlot(sourcePosition, out var removedLookSlot))
                    await worldPacketCoordinator.BroadcastUserLookChangeAsync(session, removedLookSlot, 0, 0);
                break;
        }
    }

    private static bool IsBusy(UserSession session) =>
        session.Trade.IsTrading || session.Trade.IsMerchanting || session.IsGathering;

    private static void ArrangeBag(UserSession session)
    {
        var start = InventoryConstants.InventoryStart;
        var arranged = Enumerable.Range(start, InventoryConstants.HaveMax)
            .Select(slot => session.Inventory[slot])
            .Select(item => (item.ItemId, item.Durability, item.Count, item.Flag))
            .OrderByDescending(item => item.ItemId)
            .ToArray();

        for (var offset = 0; offset < arranged.Length; offset++)
        {
            var item = session.Inventory[start + offset];
            item.ItemId = arranged[offset].ItemId;
            item.Durability = arranged[offset].Durability;
            item.Count = arranged[offset].Count;
            item.Flag = arranged[offset].Flag;
        }
    }

    private static void SwapItems(ItemSlot sourceItem, ItemSlot destinationItem)
    {
        (sourceItem.ItemId, destinationItem.ItemId) = (destinationItem.ItemId, sourceItem.ItemId);
        (sourceItem.Durability, destinationItem.Durability) = (destinationItem.Durability, sourceItem.Durability);
        (sourceItem.Count, destinationItem.Count) = (destinationItem.Count, sourceItem.Count);
        (sourceItem.Flag, destinationItem.Flag) = (destinationItem.Flag, sourceItem.Flag);
    }

    private static void MoveItem(ItemSlot sourceItem, ItemSlot destinationItem)
    {
        destinationItem.ItemId = sourceItem.ItemId;
        destinationItem.Durability = sourceItem.Durability;
        destinationItem.Count = sourceItem.Count;
        destinationItem.Flag = sourceItem.Flag;
        sourceItem.Clear();
    }

    private static async Task SendItemMoveResponseAsync(UserSession session, byte subcommand)
    {
        await session.Client.SendPacket(ItemMovePacketMapper.BuildResponse(session, subcommand));
    }

    private static bool TryResolveEquipmentPositions(
        IItemInventoryRuleService itemInventoryRuleService,
        ItemMoveDirection direction,
        ref byte sourcePosition,
        ref byte destinationPosition)
    {
        return direction switch
        {
            ItemMoveDirection.InventoryToSlot => itemInventoryRuleService.TryResolveEquipmentPosition(destinationPosition, out destinationPosition),
            ItemMoveDirection.SlotToInventory => itemInventoryRuleService.TryResolveEquipmentPosition(sourcePosition, out sourcePosition),
            ItemMoveDirection.SlotToSlot => itemInventoryRuleService.TryResolveEquipmentPosition(sourcePosition, out sourcePosition)
                                && itemInventoryRuleService.TryResolveEquipmentPosition(destinationPosition, out destinationPosition),
            _ => true,
        };
    }
}
