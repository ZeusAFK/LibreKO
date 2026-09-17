using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IWarehousePacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class WarehousePacketCoordinator(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService,
    ILogger<WarehousePacketCoordinator> logger) : IWarehousePacketCoordinator
{
    private const int ItemNoTrade = 900000001;
    private const int CoinMax = 2_100_000_000;
    private const int WarehousePageSize = 24;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
            return;

        var sub = (WarehouseSubOpcode)packet.ReadByte();
        logger.LogDebug("Warehouse operation {Sub} by {Name}", sub, session.Name);

        switch (sub)
        {
            case WarehouseSubOpcode.Open:
                await OpenAsync(session);
                break;

            case WarehouseSubOpcode.Input:
                await InputAsync(session, packet);
                break;

            case WarehouseSubOpcode.Output:
                await OutputAsync(session, packet);
                break;

            case WarehouseSubOpcode.Move:
                await MoveAsync(session, packet);
                break;

            case WarehouseSubOpcode.InventoryMove:
                await InventoryMoveAsync(session, packet);
                break;
        }
    }

    private static async Task OpenAsync(UserSession session)
    {
        var slots = new List<ItemSlot>(UserSession.WarehouseMax);
        for (var i = 0; i < UserSession.WarehouseMax; i++)
            slots.Add(session.Warehouse[i]);

        await session.Client.SendPacket(WarehousePacketWriter.Contents(
            WarehouseSubOpcode.Open, session.WarehouseMoney, slots));
    }

    private async Task InputAsync(UserSession session, Packet packet)
    {
        _ = packet.ReadInt(); // NPC unique ID (unused, validated via quest event NPC)
        var itemId = packet.ReadInt();
        var page = packet.ReadByte();
        var srcPos = packet.ReadByte();
        var dstPos = packet.ReadByte();
        var count = packet.ReadInt();


        if (itemId == InventoryConstants.ItemGold)
        {
            var goldOk = session.WithLock(s =>
            {
                if (count <= 0 || count > s.Money || s.WarehouseMoney > CoinMax - count)
                    return false;
                s.Money -= count;
                s.WarehouseMoney += count;
                return true;
            });
            await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Input, goldOk ? WarehousePacketWriter.Succeeded : WarehousePacketWriter.Failed));
            return;
        }

        var itemData = gameDataService.GetItem(itemId);
        if (itemData == null
            || srcPos >= InventoryConstants.HaveMax
            || dstPos >= WarehousePageSize
            || itemId >= ItemNoTrade
            || count <= 0
            || (itemData.Countable == 0 && count != 1))
        {
            await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Input, WarehousePacketWriter.Failed));
            return;
        }

        var absSrc = InventoryConstants.SlotMax + srcPos;
        var realDst = page * WarehousePageSize + dstPos;
        if (realDst >= UserSession.WarehouseMax)
        {
            await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Input, WarehousePacketWriter.Failed));
            return;
        }

        var success = session.WithLock(s =>
        {
            var source = s.Inventory[absSrc];
            if (source.ItemId != itemId || source.Count < count)
                return false;

            var destination = s.Warehouse[realDst];
            if (!CanMergeOrPlace(itemData, destination, itemId, count))
                return false;

            var destinationWasEmpty = destination.IsEmpty;
            destination.ItemId = itemId;
            destination.Count += (ushort)count;
            destination.Flag = source.Flag;
            if (destinationWasEmpty)
                destination.Durability = source.Durability;

            source.Count -= (ushort)count;
            if (source.Count == 0)
                source.Clear();

            var coefficient = gameDataService.GetCoefficient(s.Class);
            if (coefficient != null)
                s.RecalculateStats(coefficient, gameDataService);
            return true;
        });

        await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Input, success ? WarehousePacketWriter.Succeeded : WarehousePacketWriter.Failed));
        if (success)
            await userNotificationService.SendWeightChangeAsync(session);
    }

    private async Task OutputAsync(UserSession session, Packet packet)
    {
        _ = packet.ReadInt(); // NPC unique ID (unused, validated via quest event NPC)
        var itemId = packet.ReadInt();
        var page = packet.ReadByte();
        var srcPos = packet.ReadByte();
        var dstPos = packet.ReadByte();
        var count = packet.ReadInt();


        if (itemId == InventoryConstants.ItemGold)
        {
            var goldOk = session.WithLock(s =>
            {
                if (count <= 0 || count > s.WarehouseMoney || s.Money > CoinMax - count)
                    return false;
                s.WarehouseMoney -= count;
                s.Money += count;
                return true;
            });
            await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Output, goldOk ? WarehousePacketWriter.Succeeded : WarehousePacketWriter.Failed));
            return;
        }

        if (srcPos >= WarehousePageSize || dstPos >= InventoryConstants.HaveMax || count <= 0)
        {
            await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Output, WarehousePacketWriter.Failed));
            return;
        }

        var realSrc = page * WarehousePageSize + srcPos;
        if (realSrc >= UserSession.WarehouseMax)
        {
            await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Output, WarehousePacketWriter.Failed));
            return;
        }

        var itemData = gameDataService.GetItem(itemId);
        if (itemData == null)
        {
            await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Output, WarehousePacketWriter.Failed));
            return;
        }

        var absDst = InventoryConstants.SlotMax + dstPos;

        var success = session.WithLock(s =>
        {
            var source = s.Warehouse[realSrc];
            if (source.ItemId != itemId || source.Count < count || (itemData.Countable == 0 && count != 1))
                return false;

            var destination = s.Inventory[absDst];
            if (!CanMergeOrPlace(itemData, destination, itemId, count))
                return false;

            var destinationWasEmpty = destination.IsEmpty;
            destination.ItemId = itemId;
            destination.Count += (ushort)count;
            destination.Flag = source.Flag;
            if (destinationWasEmpty)
                destination.Durability = source.Durability;

            source.Count -= (ushort)count;
            if (source.Count == 0)
                source.Clear();

            var coefficient = gameDataService.GetCoefficient(s.Class);
            if (coefficient != null)
                s.RecalculateStats(coefficient, gameDataService);
            return true;
        });

        await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Output, success ? WarehousePacketWriter.Succeeded : WarehousePacketWriter.Failed));
        if (success)
            await userNotificationService.SendWeightChangeAsync(session);
    }

    private static async Task MoveAsync(UserSession session, Packet packet)
    {
        _ = packet.ReadInt(); // NPC unique ID (unused, validated via quest event NPC)
        var itemId = packet.ReadInt();
        var page = packet.ReadByte();
        var srcPos = packet.ReadByte();
        var dstPos = packet.ReadByte();


        if (srcPos >= WarehousePageSize || dstPos >= WarehousePageSize)
        {
            await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Move, WarehousePacketWriter.Failed));
            return;
        }

        var realSrc = page * WarehousePageSize + srcPos;
        var realDst = page * WarehousePageSize + dstPos;
        if (realSrc >= UserSession.WarehouseMax || realDst >= UserSession.WarehouseMax)
        {
            await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Move, WarehousePacketWriter.Failed));
            return;
        }

        var moved = session.WithLock(s =>
        {
            var source = s.Warehouse[realSrc];
            var destination = s.Warehouse[realDst];
            if (source.ItemId != itemId || !destination.IsEmpty)
                return false;

            destination.ItemId = source.ItemId;
            destination.Durability = source.Durability;
            destination.Count = source.Count;
            destination.Flag = source.Flag;
            source.Clear();
            return true;
        });

        await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.Move, moved ? WarehousePacketWriter.Succeeded : WarehousePacketWriter.Failed));
    }

    private static async Task InventoryMoveAsync(UserSession session, Packet packet)
    {
        _ = packet.ReadInt(); // NPC unique ID (unused, validated via quest event NPC)
        var itemId = packet.ReadInt();
        _ = packet.ReadByte();
        var srcPos = packet.ReadByte();
        var dstPos = packet.ReadByte();


        if (srcPos >= InventoryConstants.HaveMax || dstPos >= InventoryConstants.HaveMax)
        {
            await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.InventoryMove, WarehousePacketWriter.Failed));
            return;
        }

        var absSrc = InventoryConstants.SlotMax + srcPos;
        var absDst = InventoryConstants.SlotMax + dstPos;

        var moved = session.WithLock(s =>
        {
            var source = s.Inventory[absSrc];
            var destination = s.Inventory[absDst];
            if (source.ItemId != itemId || !destination.IsEmpty)
                return false;

            destination.ItemId = source.ItemId;
            destination.Durability = source.Durability;
            destination.Count = source.Count;
            destination.Flag = source.Flag;
            source.Clear();
            return true;
        });

        await session.Client.SendPacket(WarehousePacketWriter.Result(WarehouseSubOpcode.InventoryMove, moved ? WarehousePacketWriter.Succeeded : WarehousePacketWriter.Failed));
    }

    private static bool CanMergeOrPlace(ItemData itemData, ItemSlot destination, int itemId, int count)
    {
        if (destination.IsEmpty)
            return true;

        if (destination.ItemId != itemId || itemData.Countable == 0)
            return false;

        return destination.Count + count <= 9999;
    }
}
